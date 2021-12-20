﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Text.RegularExpressions;
using Z.Expressions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins.Cache;
using FastMember;
using System.Linq.Expressions;

namespace SynthEBD
{
    public class RecordPathParser
    {
        public static bool GetObjectAtPath(dynamic rootObj, string relativePath, Dictionary<dynamic, Dictionary<string, dynamic>> objectLinkMap, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, out dynamic outputObj)
        {
            return GetObjectAtPath(rootObj, relativePath, objectLinkMap, linkCache, out outputObj, out int? unusedArrayIndex);
        }
        public static bool GetObjectAtPath(dynamic rootObj, string relativePath, Dictionary<dynamic, Dictionary<string, dynamic>> objectLinkMap, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, out dynamic outputObj, out int? indexInParent)
        {
            outputObj = null;
            indexInParent = null;
            if (rootObj == null)
            {
                return false;
            }

            Dictionary<string, dynamic> objectCache;

            if (objectLinkMap.ContainsKey(rootObj))
            {
                objectCache = objectLinkMap[rootObj];
            }
            else
            {
                objectCache = new Dictionary<string, dynamic>();
                objectLinkMap.Add(rootObj, objectCache);
            }

            string[] splitPath = SplitPath(relativePath);
            dynamic currentObj = rootObj;

            for (int i = 0; i < splitPath.Length; i++)
            {
                if (currentObj == null)
                {
                    return false;
                }

                // check object cache to see if the given object has already been resolved
                string concatPath = String.Join(".", splitPath.ToList().GetRange(0, i+1));
                if (objectCache.ContainsKey(concatPath))
                {
                    currentObj = objectCache[concatPath];
                    continue;
                }

                // otherwise search for the given value via Reflection
                string currentSubPath = splitPath[i];

                // handle arrays
                //if (PathIsArray(currentSubPath, out string arraySubPath, out string arrIndex))
                if (PathIsArray(currentSubPath, out string arrIndex))
                {
                    if (!GetArrayObjectAtIndex(currentObj, arrIndex, objectLinkMap, linkCache, out currentObj, out indexInParent))
                    {
                        return false;
                    }
                }
                else if (!GetSubObject(currentObj, currentSubPath, out currentObj))
                {
                    return false;
                }

                // if the current property is another record, resolve it to traverse
                if (ObjectHasFormKey(currentObj, out FormKey? subrecordFK))
                {
                    if (subrecordFK != null && !subrecordFK.Value.IsNull && linkCache.TryResolve(subrecordFK.Value, (Type)currentObj.Type, out var subRecordGetter))
                    {
                        currentObj = subRecordGetter;
                    }
                }
            }

            if (!objectCache.ContainsKey(relativePath))
            {
                objectCache.Add(relativePath, currentObj);
            }

            outputObj = currentObj;
            return true;
        }

        public static bool GetNearestParentGetter(IMajorRecordGetter rootGetter, string path, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, out IMajorRecordGetter parentRecordGetter, out string relativePath)
        {
            string[] splitPath = SplitPath(path);
            dynamic currentObj = rootGetter;
            parentRecordGetter = null;
            relativePath = "";

            for (int i = 0; i < splitPath.Length; i++)
            {
                if (GetObjectAtPath(currentObj, splitPath[i], new Dictionary<dynamic, Dictionary<string, dynamic>>(), linkCache, out currentObj))
                {
                    if (ObjectHasFormKey(currentObj))
                    {
                        parentRecordGetter = currentObj;
                        relativePath = "";
                    }
                    else
                    {
                        relativePath += splitPath[i];
                        if (i < splitPath.Length - 1)
                        {
                            relativePath += ".";
                        }
                    }
                }
                else
                {
                    return false;
                }
            }

            return true;
        }
        private static bool GetArrayObjectAtIndex(dynamic currentObj, string arrIndex, Dictionary<dynamic, Dictionary<string, dynamic>> objectLinkMap, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, out dynamic outputObj)
        {
            return GetArrayObjectAtIndex(currentObj, arrIndex, objectLinkMap, linkCache, out outputObj, out int? unusedIndex);
        }
        private static bool GetArrayObjectAtIndex(dynamic currentObj, string arrIndex, Dictionary<dynamic, Dictionary<string, dynamic>> objectLinkMap, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, out dynamic outputObj, out int? indexInParent)
        {
            outputObj = null;
            indexInParent = null;

            var collectionObj = currentObj as IReadOnlyList<dynamic>;
            if (collectionObj == null)
            {
                Logger.LogError("Could not cast " + currentObj.GetType() + "as an XXX");
                return false;
            }

            //if array index is numeric
            if (int.TryParse(arrIndex, out int iIndex))
            {
                if (iIndex < 0 || iIndex < collectionObj.Count())
                {
                    currentObj = collectionObj.ElementAt(iIndex);
                    indexInParent = iIndex;
                }
                else
                {
                    string currentSubPath = "[" + arrIndex + "]";
                    Logger.LogError("Could not get object at " + currentSubPath + " because the " + currentObj.GetType() + " does not have an element at index " + iIndex);
                    return false;
                }
            }

            // if array index specifies object by property, figure out which index is the right one
            else
            {
                if (!ChooseWhichArrayObject(collectionObj, arrIndex, objectLinkMap, linkCache, out currentObj, out indexInParent))
                {
                    string currentSubPath = "[" + arrIndex + "]";
                    Logger.LogError("Could not get object at " + currentSubPath + " because " + currentObj.GetType() + " does not have an element that matches condition: " + arrIndex);
                    return false;
                }
            }

            outputObj = currentObj;
            return true;
        }

        private class ArrayPathCondition
        {
            private ArrayPathCondition(string strIndex)
            {
                int sepIndex = strIndex.LastIndexOf(',');
                Path = strIndex.Substring(0, sepIndex).Trim();
                ReplacerTemplate = strIndex.Substring(0, sepIndex) + ","; // unlike Path, can include whitespace provided by the user and also includes the separator comma
                MatchCondition = strIndex.Substring(sepIndex + 1, strIndex.Length - sepIndex - 1).Trim();
                if (Path.StartsWith('!'))
                {
                    Path = Path.Remove(0, 1).Trim();
                }
            }
            private ArrayPathCondition()
            {

            }
            public string Path;
            public string ReplacerTemplate;
            public string MatchCondition;
            public string SpecialHandling = "";

            public static List<ArrayPathCondition> GetConditionsFromString(string input)
            {
                String[] result = input.Split(new Char[] { '|', '&' }, StringSplitOptions.RemoveEmptyEntries); // split on logical operators
                List<ArrayPathCondition> output = new List<ArrayPathCondition>();
                foreach (var conditionStr in result)
                {
                    if (conditionStr.Contains("PatchableRaces")) // special command
                    {
                        var patchableRaceArgs = conditionStr.Split("(");
                        var patchableRaceSubject = patchableRaceArgs[1].Trim();
                        var patchableRaceMethod = patchableRaceArgs[0].Replace("PatchableRaces.", "");

                        var patchableRaceCondition = new ArrayPathCondition { Path = patchableRaceSubject.Substring(0, patchableRaceSubject.Length - 1), MatchCondition = patchableRaceMethod.Trim(), SpecialHandling = "PatchableRaces"};
                        patchableRaceCondition.ReplacerTemplate = patchableRaceCondition.Path;
                        output.Add(patchableRaceCondition);
                        continue;
                    }
                    output.Add(new ArrayPathCondition(conditionStr));
                }
                return output;
            }
        }

        private static bool ChooseWhichArrayObject(IReadOnlyList<dynamic> variants, string matchConditionStr, Dictionary<dynamic, Dictionary<string, dynamic>> objectLinkMap, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, out dynamic outputObj, out int? indexInParent)
        {
            outputObj = null;
            indexInParent = null;

            var arrayMatchConditions = ArrayPathCondition.GetConditionsFromString(matchConditionStr);

            int argIndex = 0;
            foreach (var condition in arrayMatchConditions)
            {
                string argStr = '{' + argIndex.ToString() + '}';
                for (int i = 0; i < matchConditionStr.Length - condition.ReplacerTemplate.Length; i++)
                {
                    if (matchConditionStr.Substring(i, condition.ReplacerTemplate.Length) == condition.ReplacerTemplate && (i == 0 || matchConditionStr[i - 1] == '(' || matchConditionStr[i - 1] == ' '))
                    {
                       matchConditionStr = matchConditionStr.Remove(i, condition.ReplacerTemplate.Length);
                       matchConditionStr = matchConditionStr.Insert(i, argStr);
                    }
                }
                argIndex++;
            }

            int patchableRaceArgIndex = argIndex;
            bool addPatchableRaceArg = false;

            //foreach (var candidateObj in variants)
            for (int i = 0; i < variants.Count(); i++)
            {
                var candidateObj = variants[i];
                List<dynamic> evalParameters = new List<dynamic>();
                argIndex = 0;
                bool skipToNext = false;

                IMajorRecordCommonGetter candidateRecordGetter = null;

                bool candidateObjIsRecord = ObjectHasFormKey(candidateObj, out FormKey? objFormKey) && objFormKey != null;
                bool candidateObjIsResolved = !objFormKey.Value.IsNull && linkCache.TryResolve(objFormKey.Value, (Type)candidateObj.Type, out candidateRecordGetter);

                foreach (var condition in arrayMatchConditions)
                {
                    dynamic comparisonObject;
                    
                    if (candidateObjIsResolved && candidateRecordGetter != null && GetObjectAtPath(candidateRecordGetter, condition.Path, objectLinkMap, linkCache, out comparisonObject))
                    {
                        evalParameters.Add(comparisonObject);
                    }
                    else if (candidateObjIsRecord) // warn if the object is a record but the corresponding Form couldn't be resolved
                    {
                        Logger.LogError("Could not resolve record for array member object " + objFormKey.Value.ToString());
                        skipToNext = true;
                        break;
                    }
                    else if (GetObjectAtPath(candidateObj, condition.Path, objectLinkMap, linkCache, out comparisonObject))
                    {
                        evalParameters.Add(comparisonObject);
                    }
                    else
                    {
                        return false; 
                    }
                    argIndex++;

                    if (condition.SpecialHandling == "PatchableRaces")
                    {
                        matchConditionStr = matchConditionStr.Replace("PatchableRaces", '{' + patchableRaceArgIndex.ToString() + "}");
                        addPatchableRaceArg = true;
                        evalParameters[evalParameters.Count - 1] = evalParameters[evalParameters.Count - 1].FormKey.AsLinkGetter<IRaceGetter>();
                    }
                }
                if(skipToNext) { continue; }
                
                // reference PatchableRaces if necessary
                if (addPatchableRaceArg) 
                {
                    evalParameters.Add(Patcher.PatchableRaces); 
                }

                if (Eval.Execute<bool>(matchConditionStr, evalParameters.ToArray()))
                {
                    outputObj = candidateObj;
                    indexInParent = i;
                    return true;
                }
            }
            return false;
        }

        public static HashSet<IFormLinkGetter<IRaceGetter>> testHashSet = new HashSet<IFormLinkGetter<IRaceGetter>>() {Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.DefaultRace};

        //deprecated, use version with two arguments instead
        private static bool PathIsArray(string path, out string subPath, out string index) //correct input is of form x[y]
        {
            if (path.Contains('['))
            {
                var tmp = path.Split('[');

                subPath = tmp[0]; //x
                var tmp1 = tmp[1]; //y]

                if (tmp1.Contains(']'))
                {
                    index = tmp1.Split(']')[0]; //y
                    return true;
                }
            }

            subPath = "";
            index = "";
            return false;
        }

        private static bool PathIsArray(string path, out string index) //correct input is of form [y]
        {
            index = "";
            if (path.StartsWith('[') && path.EndsWith(']'))
            {
                index = path.Substring(1, path.Length - 2);
                return true;
            }
            return false;
        }

        public static bool PathIsArray(string path) //correct input is of form [y]
        {
            return PathIsArray(path, out string unused);
        }

        public static bool GetSubObject(dynamic root, string propertyName, out dynamic outputObj)
        {
            outputObj = null;
            if (GetAccessor(root, propertyName, AccessorType.Getter, out Delegate getter))
            {
                outputObj = getter.DynamicInvoke(root);
                return true;
            }
            else
            {
                return false;
            }
        }

        public static bool GetSubObjectOld(dynamic root, string propertyName, out dynamic outputObj)
        {
            //FastMember
            /*
            var accessor = TypeAccessor.Create(root.GetType());
            MemberSet members = accessor.GetMembers();
            if (members.Where(x => x.Name == propertyName).Any())
            {
                return accessor[root, propertyName];
            }
            else
            {
                return null;
            }
            */

            outputObj = null;
            Type type = root.GetType();
            
            if (PropertyCache.ContainsKey(type))
            {
                var subDict = PropertyCache[type];
                if (subDict.ContainsKey(propertyName))
                {
                    var cachedProperty = subDict[propertyName];
                    if (cachedProperty != null)
                    {
                        outputObj = cachedProperty.GetValue(root);

                        // Test
                        var delegateType = Expression.GetFuncType(cachedProperty.DeclaringType, cachedProperty.PropertyType);
                        var getter = cachedProperty.GetMethod.CreateDelegate(delegateType);
                        //Test

                        return true;
                    }
                }
                else
                {
                    var newProperty = type.GetProperty(propertyName);
                    subDict.Add(propertyName, newProperty);
                    if (newProperty != null)
                    {
                        outputObj = newProperty.GetValue(root);
                        return true;
                    }
                }
            }
            else
            {
                var newSubDict = new Dictionary<string, PropertyInfo>();
                var newProperty2 = type.GetProperty(propertyName);
                newSubDict.Add(propertyName, newProperty2);
                PropertyCache.Add(type, newSubDict);
                if (newProperty2 != null)
                {
                    outputObj = newProperty2.GetValue(root);
                    return true;
                }
            }
            return false;
            /*
            var property = root.GetType().GetProperty(propertyName);
            if (property != null)
            {
                return property.GetValue(root);
            }
            else
            {
                return null;
            }
            */

        }

        public static void SetSubObject(dynamic root, string propertyName, dynamic value)
        {
            //FastMember
            /*
            var accessor = TypeAccessor.Create(root.GetType());
            MemberSet members = accessor.GetMembers();
            if (members.Where(x => x.Name == propertyName).Any())
            {
                accessor[root, propertyName] = value;
            }
            else
            {
                Logger.LogReport("Error: Could not set " + propertyName + " to " + value + " because the root object of type " + root.GetType() + " does not contain this property.");
            }
            */

            Type type = root.GetType();

            if (PropertyCache.ContainsKey(type))
            {
                var subDict = PropertyCache[type];
                if (subDict.ContainsKey(propertyName))
                {
                    var cachedProperty = subDict[propertyName];
                    if (cachedProperty != null)
                    {
                        cachedProperty.SetValue(root, value);
                    }
                }
                else
                {
                    var newProperty = type.GetProperty(propertyName);
                    subDict.Add(propertyName, newProperty);
                    if (newProperty != null)
                    {
                        newProperty.SetValue(root, value);
                    }
                }
            }
            else
            {
                var newSubDict = new Dictionary<string, PropertyInfo>();
                var newProperty2 = type.GetProperty(propertyName);
                newSubDict.Add(propertyName, newProperty2);
                PropertyCache.Add(type, newSubDict);
                if (newProperty2 != null)
                {
                    newProperty2.SetValue(root, value);
                }
            }

            /*
            var property = root.GetType().GetProperty(propertyName);
            if (property != null)
            {
                property.SetValue(root, value);
            }
            */
        }

        /// <summary>
        /// Determines if root[propertyName] expects a record (as opposed to a generic struct). Outs the FormKey of root[propertyName].value if one exists, or outs null if there is no value at that property
        /// </summary>
        /// <param name="root">Root object</param>
        /// <param name="propertyName">Property to search relative to root object</param>
        /// <param name="formKey">Nullable formkey of root[propertyName] if it exists</param>
        /// <returns></returns>
        public static bool PropertyIsRecord(dynamic root, string propertyName, out FormKey? formKey, Dictionary<dynamic, Dictionary<string, dynamic>> objectLinkMap, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            formKey = null;
            dynamic formKeyDyn = null;
            // handle arrays
            if (PathIsArray(propertyName, out string arrIndex))
            {
                if (GetArrayObjectAtIndex(root, arrIndex, objectLinkMap, linkCache, out dynamic specifiedArrayObj) && ObjectHasFormKey(specifiedArrayObj, out formKey))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                var property = root.GetType().GetProperty(propertyName);
                if (property != null && property.PropertyType.Name.StartsWith("IFormLinkNullableGetter") && GetSubObject(property.GetValue(root), "FormKey", out formKeyDyn))
                {
                    formKey = (FormKey?)formKeyDyn;
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public static bool PropertyIsRecord(dynamic root, string propertyName, Dictionary<dynamic, Dictionary<string, dynamic>> objectLinkMap, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            // handle arrays
            if (PathIsArray(propertyName, out string arrIndex) && !GetArrayObjectAtIndex(root, arrIndex, objectLinkMap, linkCache, out root)) // root gets updated here
            {
                return false;
            }

            var property = root.GetType().GetProperty(propertyName);
            if (property != null && property.PropertyType.Name.StartsWith("IFormLinkNullableGetter"))
            {
                return true;
            }
            return false;
        }

        public static bool ObjectHasFormKey(dynamic obj, out FormKey? formKey)
        {
            if (obj != null)
            {
                var property = obj.GetType().GetProperty("FormKey");
                if (property != null)
                {
                    formKey = (FormKey)property.GetValue(obj);
                    return true;
                }
            }

            formKey = null;
            return false;
        }

        public static bool ObjectHasFormKey(dynamic obj)
        {
            var property = obj.GetType().GetProperty("FormKey");
            if (property != null)
            {
                return true;
            }
            return false;
        }

        public static bool HasProperty(Type type, string propertyName)
        {
            return type.GetProperty(propertyName) != null;
        }

        public static string[] SplitPath(string input)
        {
            var pattern = @"\.(?![^\[]*[\]])";
            var split = Regex.Split(input, pattern);

            List<string> output = new List<string>();
            foreach (var substr in split)
            {
                if (!substr.StartsWith('[') && substr.Contains('[') && substr.EndsWith(']'))
                {
                    int bracketIndex = substr.IndexOf('[');
                    output.Add(substr.Substring(0, bracketIndex));
                    output.Add(substr.Substring(bracketIndex, substr.Length - bracketIndex));
                }
                else
                {
                    output.Add(substr);
                }
            }

            return output.ToArray();
        }

        public static Dictionary<Type, Dictionary<string, System.Reflection.PropertyInfo>> PropertyCache = new Dictionary<Type, Dictionary<string, PropertyInfo>>();

        public static bool GetPropertyInfo(dynamic obj, string propertyName, out System.Reflection.PropertyInfo property)
        {
            property = null;
            Type type = obj.GetType();
            if (PropertyCache.ContainsKey(type))
            {
                var properties = PropertyCache[type];
                if (properties.ContainsKey(propertyName))
                {
                    property = properties[propertyName];
                }
                else
                {
                    property = type.GetProperty(propertyName);
                    PropertyCache[type].Add(propertyName, property);
                }
            }
            else
            {
                Dictionary<string, System.Reflection.PropertyInfo> subDict = new Dictionary<string, PropertyInfo>();
                property = type.GetProperty(propertyName);
                subDict.Add(propertyName, property);
                PropertyCache.Add(type, subDict);
            }

            if (property != null)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static Dictionary<Type, Dictionary<string, Delegate>> GetterEmbassy = new Dictionary<Type, Dictionary<string, Delegate>>();
        public static Dictionary<Type, Dictionary<string, Delegate>> SetterEmbassy = new Dictionary<Type, Dictionary<string, Delegate>>();

        public enum AccessorType
        {
            Getter,
            Setter
        }

        public static bool GetAccessor(dynamic obj, string propertyName, AccessorType accessorType, out Delegate accessor)
        {
            accessor = null;
            PropertyInfo property = null;
            Dictionary<Type, Dictionary<string, Delegate>> cache = null;

            switch(accessorType)
            {
                case AccessorType.Getter: cache = GetterEmbassy; break;
                case AccessorType.Setter: cache = SetterEmbassy; break;
            }

            Type type = obj.GetType();

            if (cache.ContainsKey(type))
            {
                if (cache[type].ContainsKey(propertyName))
                {
                    accessor = cache[type][propertyName];
                }
                else if (GetPropertyInfo(obj, propertyName, out property))
                {
                    switch(accessorType)
                    {
                        case AccessorType.Getter: accessor = CreateDelegateGetter(property); break;
                        case AccessorType.Setter: accessor = CreateDelegateSetter(property); break;
                    }
                    
                    cache[type].Add(propertyName, accessor);
                }
            }
            else
            {
                if (GetPropertyInfo(obj, propertyName, out property))
                {
                    switch (accessorType)
                    {
                        case AccessorType.Getter: accessor = CreateDelegateGetter(property); break;
                        case AccessorType.Setter: accessor = CreateDelegateSetter(property); break;
                    }
                    Dictionary<string, Delegate> subDict = new Dictionary<string, Delegate>();
                    subDict.Add(propertyName, accessor);
                    cache.Add(type, subDict);
                }
                else
                {
                    accessor = null; // just for readability
                }
            }

            if (accessor != null)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static Delegate CreateDelegateGetter(PropertyInfo property)
        {
            var delegateType = Expression.GetFuncType(property.DeclaringType, property.PropertyType);
            return property.GetMethod.CreateDelegate(delegateType);
        }

        public static Delegate CreateDelegateSetter(PropertyInfo property)
        {
            var delegateType = Expression.GetFuncType(property.DeclaringType, property.PropertyType);
            return property.SetMethod.CreateDelegate(delegateType);
        }
    }
}
