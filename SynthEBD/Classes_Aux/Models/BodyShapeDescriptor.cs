using Newtonsoft.Json;

namespace SynthEBD;

public class BodyShapeDescriptor
{
    public BodyShapeDescriptor()
    {
        // empty - used for everything besides backward-compatibile deserialization
    }

    [JsonConstructor] // https://stackoverflow.com/questions/73675879/newtonsoft-json-if-deserialization-error-try-different-class
    public BodyShapeDescriptor(LabelSignature deprecated, string category, string Value)
    {
        if (deprecated != null)
        {
            ID.Category = deprecated.Category;
            ID.Value = deprecated.Value;
        }
        else
        {
            this.ID = new() { Category = category, Value = Value };
        }
    }

    public LabelSignature ID { get; set; } = new();
    public BodyShapeDescriptorRules AssociatedRules { get; set; } = new();

    public override bool Equals(Object obj)
    {
        if (obj == null) return false;
        if (obj is BodyShapeDescriptor)
        {
            var other = obj as BodyShapeDescriptor;
            return this.ID.Equals(other.ID);
        }
        else if (obj is LabelSignature)
        {
            var other = obj as LabelSignature;
            return other.Category == ID.Category && other.Value == ID.Value;
        }
        else
        {
            return false;
        }
    }


    public class LabelSignature
    {
        public string Category { get; set; } = "";
        public string Value { get; set; } = "";

        public override bool Equals(Object obj)
        {
            if (obj == null) return false;
            if (obj is BodyShapeDescriptor)
            {
                var other = obj as BodyShapeDescriptor;
                return this.Equals(other.ID);
            }
            else if (obj is LabelSignature)
            {
                var other = obj as LabelSignature;
                return other.Category == Category && other.Value == Value;
            }
            else
            {
                return false;
            }
        }
        public bool MapsTo(string category, string value)
        {
            if (Category == category && Value == value)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        public bool CollectionContainsThisDescriptor(IEnumerable<LabelSignature> collection)
        {
            foreach (var d in collection)
            {
                if (d.Equals(this))
                {
                    return true;
                }
            }
            return false;
        }
        public bool CollectionContainsThisDescriptor(IEnumerable<BodyShapeDescriptor> collection)
        {
            foreach (var d in collection)
            {
                if (d.Equals(this))
                {
                    return true;
                }
            }
            return false;
        }
        public override string ToString()
        {
            return Category + ": " + Value;
        }
        public static bool FromString(string s, out LabelSignature descriptor)
        {
            descriptor = new();
            if (s == null) 
            {
                Logger.LogError("Could not convert null string into a Body Shape Descriptor");
                return false; 
            }
            string[] split = s.Split(':');
            if (split.Length != 2)
            {
                Logger.LogError("Could not convert \"" + s + "\" into a Body Shape Descriptor - Expected 'Category: Value' format");
                return false;
            }
            else
            {
                descriptor.Category = split[0].Trim();
                descriptor.Value = split[1].Trim();
                return true;
            }
        }
    }

    public bool MapsTo(string category, string value)
    {
        return ID.MapsTo(category, value);
    }

    public bool PermitNPC(NPCInfo npcInfo, out string reportStr)
    {
        return BodyShapeDescriptorRules.NPCisValid(this, npcInfo, out reportStr);
    }

    public static bool DescriptorsMatch(Dictionary<string, HashSet<string>> DescriptorSet, HashSet<LabelSignature> shapeDescriptors, out string firstMatch)
    {
        firstMatch = "";
        foreach (var d in shapeDescriptors)
        {
            if (DescriptorsContainThis(DescriptorSet, d, out firstMatch))
            {
                return true;
            }
        }
        return false;
    }

    public static bool DescriptorsContainThis(Dictionary<string, HashSet<string>> Descriptors, LabelSignature currentDescriptor, out string firstMatch)
    {
        firstMatch = "";
        if (Descriptors.ContainsKey(currentDescriptor.Category))
        {
            if (Descriptors[currentDescriptor.Category].Contains(currentDescriptor.Value))
            {
                firstMatch = currentDescriptor.Category + ": " + currentDescriptor.Value;
                return true;
            }
        }
        return false;
    }
}