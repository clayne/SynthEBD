using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Mutagen.Bethesda.Skyrim;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace SynthEBD;

public class ConfigInstaller
{
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly SettingsIO_AssetPack _assetPackIO;
    private readonly SettingsIO_BodyGen _bodyGenIO;
    private readonly IEnvironmentStateProvider _environmentProvider;
    public ConfigInstaller(Logger logger, SynthEBDPaths synthEBDPaths, SettingsIO_AssetPack assetPackIO, SettingsIO_BodyGen bodyGenIO, IEnvironmentStateProvider environmentProvider)
    {
        _logger = logger;
        _paths = synthEBDPaths;
        _assetPackIO = assetPackIO;
        _bodyGenIO = bodyGenIO;
        _environmentProvider = environmentProvider;
    }
    public List<string> InstallConfigFile()
    {
        var installedConfigs = new List<string>();
        if (PatcherSettings.ModManagerIntegration.ModManagerType != ModManager.None && string.IsNullOrWhiteSpace(PatcherSettings.ModManagerIntegration.CurrentInstallationFolder))
        {
            CustomMessageBox.DisplayNotificationOK("Installation failed", "You must set the location of your mod manager's Mods folder before installing a config file archive.");
            return installedConfigs;
        }

        if (!IO_Aux.SelectFile(_paths.AssetPackDirPath, "Archive Files (*.7z;*.zip;*.rar)|*.7z;*.zip;*.rar|" + "All files (*.*)|*.*", "Select config archive", out string path))
        {
            return installedConfigs;
        }

        string tempFolderPath = Path.Combine(PatcherSettings.ModManagerIntegration.TempExtractionFolder, DateTime.Now.ToString("yyyy-MM-dd-HH-mm", System.Globalization.CultureInfo.InvariantCulture));
            
        try
        {
            Directory.CreateDirectory(tempFolderPath);
        }
        catch (Exception ex)
        {
            _logger.LogError("Could not create or access the temp folder at " + tempFolderPath + ". Details: " + ex.Message);
            return installedConfigs;
        }

        try
        {
            if (!ExtractArchiveNew(path, tempFolderPath, true))
            {
                return installedConfigs;
            }
        }
        catch (Exception ex)
        {
            CustomMessageBox.DisplayNotificationOK("Installation failed", "Archive extraction failed. This may be because the resulting file paths were too long. Try moving your Temp Folder in Mod Manager Integration to a short path such as your desktop. Installation aborted. Exception Message: " + Environment.NewLine + ExceptionLogger.GetExceptionStack(ex));
        }

        string manifestPath = Path.Combine(tempFolderPath, "Manifest.json");
        if (!File.Exists(manifestPath))
        {
            CustomMessageBox.DisplayNotificationOK("Installation failed", "Could not find Manifest.json in " + tempFolderPath + ". Installation aborted.");
            return installedConfigs;
        }

        Manifest manifest = JSONhandler<Manifest>.LoadJSONFile(manifestPath, out bool parsed, out string exceptionStr);
        if (!parsed)
        {
            CustomMessageBox.DisplayNotificationOK("Installation failed", "Could not parse Manifest.json in " + tempFolderPath + ". Installation aborted.");
            _logger.LogError(exceptionStr);
            return installedConfigs;
        }
        else if (!ValidateManifest(manifest))
        {
            return installedConfigs;
        }

        var installerWindow = new Window_ConfigInstaller();
        var installerVM = new VM_ConfigInstaller(manifest, installerWindow); // note: installerVM edits the Manifest to use as a convenient DTO.
        installerWindow.DataContext = installerVM;
        installerWindow.ShowDialog();

        if (installerVM.Cancelled || !installerVM.Completed)
        {
            return installedConfigs;
        }

        if (PatcherSettings.ModManagerIntegration.ModManagerType != ModManager.None && (manifest.DestinationModFolder == null || string.IsNullOrWhiteSpace(manifest.DestinationModFolder)))
        {
            CustomMessageBox.DisplayNotificationOK("Installation warning", "Manifest did not include a destination folder. A new folder called \"New SynthEBD Config\" will appear in your mod list. Pleast rename this folder to something sensible after completing installation.");
            manifest.DestinationModFolder = "New SynthEBD Config";
        }

        #region load potential required dependencies for validating asset pack
        //record templates
        HashSet<string> recordTemplatePaths = new HashSet<string>();
        foreach (var rtPath in manifest.RecordTemplatePaths)
        {
            recordTemplatePaths.Add(Path.Combine(tempFolderPath, rtPath));
        }
        List<SkyrimMod> validationRecordTemplates = _assetPackIO.LoadRecordTemplates(recordTemplatePaths, out bool loadSuccess);
        if (!loadSuccess)
        {
            CustomMessageBox.DisplayNotificationOK("Installation failed", "Could not parse all Record Template Plugins at " + string.Join(", ", recordTemplatePaths) + ". Installation aborted.");
            return new List<string>();
        }

        // BodyGen config
        HashSet<string> bodyGenConfigPaths = new HashSet<string>();
        foreach (var bgPath in manifest.BodyGenConfigPaths)
        {
            bodyGenConfigPaths.Add(Path.Combine(tempFolderPath, bgPath));
        }
        BodyGenConfigs validationBG = _bodyGenIO.LoadBodyGenConfigs(bodyGenConfigPaths.ToArray(), PatcherSettings.General.RaceGroupings, out loadSuccess);
        if (!loadSuccess)
        {
            CustomMessageBox.DisplayNotificationOK("Installation failed", "Could not parse all BodyGen configs at " + string.Join(", ", bodyGenConfigPaths) + ". Installation aborted.");
            return new List<string>();
        }

        #endregion

        Dictionary<string, string> assetPathMapping = new Dictionary<string, string>();
        HashSet<string> referencedFilePaths = new HashSet<string>();

        HashSet<string> skippedConfigs = new HashSet<string>();

        #region Load, validate, and resave Asset Packs
        foreach (var configPath in manifest.AssetPackPaths)
        {
            string extractedPath = Path.Combine(tempFolderPath, configPath);
            var validationAP = _assetPackIO.LoadAssetPack(extractedPath, PatcherSettings.General.RaceGroupings, validationRecordTemplates, validationBG, out loadSuccess);
            if (!loadSuccess)
            {
                CustomMessageBox.DisplayNotificationOK("Installation failed", "Could not parse Asset Pack " + configPath + ". Installation aborted.");
                continue;
            }

            string destinationPath = Path.Combine(_paths.AssetPackDirPath, validationAP.GroupName + ".json");

            if (!HandleLongFilePaths(validationAP, manifest, out assetPathMapping))
            {
                continue;
            }

            if (!File.Exists(destinationPath))
            {
                validationAP.FilePath = destinationPath;
                _assetPackIO.SaveAssetPack(validationAP, out bool saveSuccess); // save as Json instead of moving in case the referenced paths were modified by HandleLongFilePaths()
                if (!saveSuccess)
                {
                    CustomMessageBox.DisplayNotificationOK("Installation failed", "Could not save Asset Pack to " + destinationPath + ". Installation aborted.");
                    continue;
                }
            }
            else
            {
                skippedConfigs.Add(Path.GetFileName(extractedPath));
                continue;
            }

            referencedFilePaths.UnionWith(GetAssetPackSourcePaths(validationAP));

            installedConfigs.Add(validationAP.GroupName);
        }
        #endregion

        #region move bodygen configs
        foreach (var bgPath in manifest.BodyGenConfigPaths)
        {
            string destPath = Path.Combine(_paths.BodyGenConfigDirPath, Path.GetFileName(bgPath));
            if (!File.Exists(destPath))
            {
                string sourcePath = Path.Combine(tempFolderPath, bgPath);
                try
                {
                    File.Move(sourcePath, destPath, false);
                }
                catch (Exception ex)
                {
                    CustomMessageBox.DisplayNotificationOK("Installation warning", "Could not move " + sourcePath + " to " + destPath + ": " + ex.Message);
                }
            }
            else
            {
                skippedConfigs.Add(Path.GetFileName(bgPath));
            }
        }
        #endregion

        #region Move record templates
        foreach (var templatePath in manifest.RecordTemplatePaths)
        {
            string destPath = Path.Combine(_paths.RecordTemplatesDirPath, Path.GetFileName(templatePath));
            if (!File.Exists(destPath))
            {
                string sourcePath = Path.Combine(tempFolderPath, templatePath);
                try
                {
                    File.Move(sourcePath, destPath, false);
                }
                catch (Exception ex)
                {
                    CustomMessageBox.DisplayNotificationOK("Installation warning", "Could not move " + sourcePath + " to " + destPath + ": " + ex.Message);
                }
            }
            else
            {
                skippedConfigs.Add(Path.GetFileName(templatePath));
            }
        }
        #endregion

        if (skippedConfigs.Any())
        {
            CustomMessageBox.DisplayNotificationOK("Installation warning", "The following resources were not installed because they already exist in your settings:" + Environment.NewLine + String.Join(Environment.NewLine, skippedConfigs));
        }

        #region move dependency files

        //System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => await _logger.ArchiveStatusAsync());
        //_ = _logger.ArchiveStatusAsync();
        //System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => await _logger.UpdateStatusAsync("Extracting mods - please wait.", false));
        //_ = _logger.UpdateStatusAsync("Extracting mods - please wait.", false);
        foreach(string dependencyArchive in installerVM.DownloadMenu.DownloadInfo.Select(x => x.Path))
        {
            ExtractArchiveNew(dependencyArchive, tempFolderPath, false);
        }
        //System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => await _logger.UnarchiveStatusAsync());
        //_ = _logger.DeArchiveStatusAsync();

        List<string> missingFiles = new List<string>();
        Dictionary<string, string> reversedAssetPathMapping = new Dictionary<string, string>();
        if (assetPathMapping.Keys.Any())
        {
            reversedAssetPathMapping = assetPathMapping.ToDictionary(x => x.Value, x => x.Key);
        }

        bool assetPathCopyErrors = false;

        foreach (string assetPath in referencedFilePaths)
        {
            if (PathStartsWithModName(assetPath))
            {
                continue;
            }
            else if (reversedAssetPathMapping.ContainsKey(assetPath))
            {
                
                var pathInConfigFile = reversedAssetPathMapping[assetPath];
                string extractedSubPath = GetPathWithoutSynthEBDPrefix(pathInConfigFile, manifest);
                string extractedFullPath = Path.Combine(tempFolderPath, extractedSubPath);

                if (!File.Exists(extractedFullPath))
                {
                    missingFiles.Add(assetPath);
                    continue;
                }

                string destinationSubPath = GetPathWithoutSynthEBDPrefix(assetPath, manifest);
                string destinationFullPath = GenerateInstalledPath(destinationSubPath, manifest);
                if (!File.Exists(destinationFullPath))
                {
                    try
                    {
                        PatcherIO.CreateDirectoryIfNeeded(destinationFullPath, PatcherIO.PathType.File);
                    }
                    catch (Exception ex)
                    {
                        assetPathCopyErrors = true;
                        _logger.LogError("Could not create or access directory " + destinationFullPath + ": " + ex.Message);
                    }

                    try
                    {
                        File.Move(extractedFullPath, destinationFullPath);
                    }
                    catch (Exception ex)
                    {
                        assetPathCopyErrors = true;
                        _logger.LogError("Could not move " + extractedFullPath + " to " + destinationFullPath + ": " + ex.Message);
                    }
                }
            }
            else
            {
                string extractedSubPath = GetPathWithoutSynthEBDPrefix(assetPath, manifest);
                string extractedFullPath = Path.Combine(tempFolderPath, extractedSubPath);

                if (!File.Exists(extractedFullPath))
                {
                    missingFiles.Add(assetPath);
                    continue;
                }

                string destinationFullPath = GenerateInstalledPath(extractedSubPath, manifest);
                if (!File.Exists(destinationFullPath))
                {
                    try
                    {
                        PatcherIO.CreateDirectoryIfNeeded(destinationFullPath, PatcherIO.PathType.File);
                    }
                    catch (Exception ex)
                    {
                        assetPathCopyErrors = true;
                        _logger.LogError("Could not create or access directory " + destinationFullPath + ": " + ex.Message);
                    }

                    try
                    {
                        File.Move(extractedFullPath, destinationFullPath);
                    }
                    catch (Exception ex)
                    {
                        assetPathCopyErrors = true;
                        _logger.LogError("Could not move " + extractedFullPath + " to " + destinationFullPath + ": " + ex.Message);
                    }
                }
            }
        }

        #endregion

        if (missingFiles.Any())
        {
            CustomMessageBox.DisplayNotificationOK("Installation warning", "The following expected files were not found in the selected mod archives:" + Environment.NewLine + string.Join(Environment.NewLine, missingFiles));
        }
        if (assetPathCopyErrors)
        {
            CustomMessageBox.DisplayNotificationOK("Installation warning", "Some installation errors occurred. Please see the Status Log.");
        }

        /*
        #region Import new BodyGen configs as VMs to be saved upon close
        foreach (var mConfig in validationBG.Male)
        {
            if (!mainViewModel.BGVM.MaleConfigs.Where(x => x.Label == mConfig.Label).Any())
            {
                var newMaleVM = VM_BodyGenConfig.GetViewModelFromModel(mConfig, mainViewModel.SGVM.RaceGroupings);
                newMaleVM.SourcePath = ""; // will get auto-updated upon save
                mainViewModel.BGVM.MaleConfigs.Add(newMaleVM);
            }
        }
        foreach (var fConfig in validationBG.Female)
        {
            if (!mainViewModel.BGVM.MaleConfigs.Where(x => x.Label == fConfig.Label).Any())
            {
                var newFemaleVM = VM_BodyGenConfig.GetViewModelFromModel(fConfig, mainViewModel.SGVM.RaceGroupings);
                newFemaleVM.SourcePath = ""; // will get auto-updated upon save
                mainViewModel.BGVM.FemaleConfigs.Add(newFemaleVM);
            }
        }
        #endregion

        #region Import new Asset Packs as VMs to be saved upon close
        foreach (var ap in assetPacks)
        {
            if (!mainViewModel.AssetPacks.Where(x => x.GroupName == ap.GroupName).Any())
            {
                var newAssetPackVM = VM_AssetPack.GetViewModelFromModel(ap, mainViewModel.SGVM, mainViewModel.TMVM.AssetPacks, mainViewModel.vm)
            }
        }
        */

        try
        {
            IO_Aux.DeleteDirectoryAF(tempFolderPath);
        }
        catch
        {
            CustomMessageBox.DisplayNotificationOK("Installation warning", "Could not delete the temp folder located at " + tempFolderPath + ". This may be because the folder path or some of the contained file paths exceeded 260 characters. You may delete this folder manually.");
        }

        if (PatcherSettings.ModManagerIntegration.ModManagerType != ModManager.None && referencedFilePaths.Any())
        {
            CustomMessageBox.DisplayNotificationOK("Installation success", "Installation complete. You will need to restart your mod manager to rebuild the VFS in order for SynthEBD to see the newly installed asset files.");
        }

        return installedConfigs;
    }

    public static bool ValidateManifest(Manifest manifest)
    {
        if (manifest.ConfigPrefix == null || string.IsNullOrWhiteSpace(manifest.ConfigPrefix))
        {
            CustomMessageBox.DisplayNotificationOK("Installation error", "Manifest did not include a destination prefix. This must match the second directory of each file path in the config file (e.g. textures\\PREFIX\\some\\texture.dds). Please fix the manifest file.");
            return false;
        }
        return true;
    }

    private bool ExtractArchiveNew(string archivePath, string destinationPath, bool hideWindow)
    {
        try
        {
            var sevenZipPath = Path.Combine(_environmentProvider.InternalDataPath, "7Zip",
                        Environment.Is64BitProcess ? "x64" : "x86", "7za.exe");

            ProcessStartInfo pro = new ProcessStartInfo();
            pro.WindowStyle = ProcessWindowStyle.Hidden;
            pro.FileName = sevenZipPath;
            pro.Arguments = string.Format("x \"{0}\" -y -o\"{1}\"", archivePath, destinationPath);
            Process x = Process.Start(pro);
            x.WaitForExit();
        }

        catch (Exception e)
        {
            CustomMessageBox.DisplayNotificationOK("File Extraction Error", "Extraction of " + archivePath + " failed with message: " + Environment.NewLine + ExceptionLogger.GetExceptionStack(e));
            return false;
        }
        return true;   
    }

    private static bool ExtractArchive(string archivePath, string destinationPath)
    {
        Cursor.Current = Cursors.WaitCursor;
        FileInfo archiveInfo = new FileInfo(archivePath);
        if (SevenZipArchive.IsSevenZipFile(archiveInfo))
        {
            var zArchive = SevenZipArchive.Open(archiveInfo, new ReaderOptions());
            using (var reader = zArchive.ExtractAllEntries())
            {
                var options = new ExtractionOptions() { Overwrite = true };
                options.ExtractFullPath = true;
                reader.WriteAllToDirectory(destinationPath, options);
            }
            Cursor.Current = Cursors.Default;
            return true;
        }
        else if (RarArchive.IsRarFile(archivePath))
        {
            var rArchive = RarArchive.Open(archiveInfo, new ReaderOptions());
            using (var reader = rArchive.ExtractAllEntries())
            {
                var options = new ExtractionOptions() { Overwrite = true };
                options.ExtractFullPath = true;
                reader.WriteAllToDirectory(destinationPath, options);
            }
            Cursor.Current = Cursors.Default;
            return true;
        }
        else if (ZipArchive.IsZipFile(archivePath))
        {
            var ziArchive = ZipArchive.Open(archiveInfo, new ReaderOptions());
            using (var reader = ziArchive.ExtractAllEntries())
            {
                var options = new ExtractionOptions() { Overwrite = true };
                options.ExtractFullPath = true;
                reader.WriteAllToDirectory(destinationPath, options);
            }
            Cursor.Current = Cursors.Default;
            return true;
        }
        else
        {
            Cursor.Current = Cursors.Default;
            CustomMessageBox.DisplayNotificationOK("Installation error", "Could not extract the config archive. Valid formats are .7z, .zip, and .rar.");
            return false;
        }
    }

    public static HashSet<string> GetAssetPackSourcePaths(AssetPack assetPack)
    {
        // get paths in main subgroups
        var referencedPaths = GetSubgroupListPaths(assetPack.Subgroups, new HashSet<string>());

        // get paths in replacer subgroups
        foreach (var replacer in assetPack.ReplacerGroups)
        {
            referencedPaths = GetSubgroupListPaths(replacer.Subgroups, referencedPaths);
        }
        return referencedPaths;
    }

    public static HashSet<string> GetSubgroupListPaths(IEnumerable<AssetPack.Subgroup> subgroups, HashSet<string> collectedPaths)
    { 
        foreach (var subgroup in subgroups)
        {
            collectedPaths.UnionWith(subgroup.Paths.Select(x => x.Source));
            collectedPaths = GetSubgroupListPaths(subgroup.Subgroups, collectedPaths);
        }
        return collectedPaths;
    }

    public bool HandleLongFilePaths(AssetPack assetPack, Manifest manifest, out Dictionary<string, string> pathMap)
    {
        pathMap = new Dictionary<string, string>();
        int pathLengthLimit = 260;

        switch(PatcherSettings.ModManagerIntegration.ModManagerType)
        {
            case ModManager.None: pathLengthLimit = PatcherSettings.ModManagerIntegration.FilePathLimit; break;
            case ModManager.ModOrganizer2: pathLengthLimit = PatcherSettings.ModManagerIntegration.MO2Settings.FilePathLimit; break;
            case ModManager.Vortex: pathLengthLimit = PatcherSettings.ModManagerIntegration.VortexSettings.FilePathLimit; break;
        }

        int originalLongestPathLength = GetLongestPathLength(assetPack, manifest, out string longestPath);

        if (originalLongestPathLength > pathLengthLimit)
        {
            pathMap = RemapDirectoryNames(assetPack, manifest); // from list of paths, generate a map of old -> new paths
            RemapAssetPackPaths(assetPack, pathMap); // from path map, reassign the paths referenced in the asset pack

            int newLongestPathLength = GetLongestPathLength(assetPack, manifest, out _);
            if (newLongestPathLength > pathLengthLimit)
            {
                CustomMessageBox.DisplayNotificationOK("Installation error", "Cannot extract the required asset files for config file " + assetPack.GroupName + ". The longest path (" + longestPath + ") is " + originalLongestPathLength + " characters and a maximum of " + pathLengthLimit + " are allowed. After automatic renaming the longest path was still " + newLongestPathLength + " charactersl long. Please consider moving the destination directory to a shorter path");
                return false;
            }
            else
            {
                CustomMessageBox.DisplayNotificationOK("Installation notice", "Config file " + assetPack.GroupName + " was modified to comply with the path length limit (" + pathLengthLimit + ")." + Environment.NewLine + "The longest file path (" + longestPath + ") would have been " + originalLongestPathLength + " characters long." + Environment.NewLine + "The longest file path is now truncated to " + newLongestPathLength + " characters." + Environment.NewLine + "All paths within the config file and the destination data folder were automatically modified. No additional action is required.");
            }
        }

        return true;
    }

    public int GetLongestPathLength(AssetPack assetPack, Manifest manifest, out string longestPath)
    {
        longestPath = "";
            
        var referencedPaths = GetAssetPackSourcePaths(assetPack);

        foreach (var referencedPath in referencedPaths)
        {
            if (PathStartsWithModName(referencedPath)) { continue; }

            if (referencedPath.Length > longestPath.Length)
            {
                longestPath = referencedPath;
            }
        }

        longestPath = GenerateInstalledPath(GetPathWithoutSynthEBDPrefix(longestPath, manifest), manifest);

        return longestPath.Length;
    }

    public static bool TryTrimModFolder(Manifest manifest) // currently deprectated - I don't think this is an intuitive functionality but leaving for now as a future consideration.
    {
        if (!manifest.DestinationModFolder.Any())
        {
            return false;
        }

        string candidateFolderName = manifest.DestinationModFolder.Remove(manifest.DestinationModFolder.Length - 1, 1);
        DirectoryInfo destDir = new DirectoryInfo(PatcherSettings.ModManagerIntegration.CurrentInstallationFolder);
        if (destDir.GetDirectories().Select(x => x.Name).Contains(candidateFolderName))
        {
            return false;
        }
        else
        {
            manifest.DestinationModFolder = candidateFolderName;
            return true;
        }
    }

    public string GenerateInstalledPath(string extractedSubPath, Manifest manifest)
    {
        if (GetExpectedDataFolderFromExtension(extractedSubPath, manifest, out string extensionFolder))
        {
            if (PatcherSettings.ModManagerIntegration.ModManagerType == ModManager.None)
            {
                return Path.Combine(_environmentProvider.DataFolderPath, extensionFolder, manifest.ConfigPrefix, extractedSubPath);
            }
            else
            {
                return Path.Combine(PatcherSettings.ModManagerIntegration.CurrentInstallationFolder, manifest.DestinationModFolder, extensionFolder, manifest.ConfigPrefix, extractedSubPath);
            }
        }
        else
        {
            if (PatcherSettings.ModManagerIntegration.ModManagerType == ModManager.None)
            {
                return Path.Combine(_environmentProvider.DataFolderPath, extractedSubPath);
            }
            else
            {
                return Path.Combine(PatcherSettings.ModManagerIntegration.CurrentInstallationFolder, manifest.DestinationModFolder, extractedSubPath);
            }
        }
    }

    public static string GetPathWithoutSynthEBDPrefix(string path, Manifest manifest) // expects path straight from Config file, e.g. textures\\foo\\textures\\blah.dds
    {
        if(GetExpectedDataFolderFromExtension(path, manifest, out string extensionFolder))
        {
            string synthEBDPrefix = Path.Combine(extensionFolder, manifest.ConfigPrefix);
            return Path.GetRelativePath(synthEBDPrefix, path);
        }
        else
        {
            return path;
        }
    }

    public static bool GetExpectedDataFolderFromExtension(string path, Manifest manifest, out string extensionFolder)
    {
        string extension = Path.GetExtension(path).TrimStart('.');
        extensionFolder = "";
        if (manifest.FileExtensionMap.ContainsKey(extension))
        {
            extensionFolder = manifest.FileExtensionMap[extension]; // otherwise the file will be installed directly to the data folder or top-level of the mod folder
            return true;
        }
        else
        {
            var trimPath = PatcherSettings.TexMesh.TrimPaths.Where(x => x.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            if (trimPath is not null)
            {
                extensionFolder = trimPath.PathToTrim;
                return true;
            }
        }
        return false;
    }

    public static Dictionary<string, string> RemapDirectoryNames(AssetPack extractedPack, Manifest manifest)
    {
        Dictionary<string, string> pathMap = new Dictionary<string, string>();

        int newFileNameIndex;

        var containedPaths = GetAssetPackSourcePaths(extractedPack);

        Dictionary<string, int> pathCountsByFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in containedPaths)
        {
            if (!pathMap.ContainsKey(path) && !PathStartsWithModName(path) && GetExpectedDataFolderFromExtension(path, manifest, out _))
            {
                string fileName = Path.GetFileNameWithoutExtension(path);
                if(pathCountsByFile.ContainsKey(fileName))
                {
                    pathCountsByFile[fileName]++;
                    newFileNameIndex = pathCountsByFile[fileName];
                }
                else
                {
                    pathCountsByFile.Add(fileName, 1);
                    newFileNameIndex = 1;
                }

                pathMap.Add(path, GenerateRemappedPath(path, manifest, fileName, newFileNameIndex));
                newFileNameIndex++;
            }
        }

        return pathMap;
    }

    public static void RemapAssetPackPaths(AssetPack assetPack, Dictionary<string, string> pathMap)
    {
        // remap paths in main subgroups
        RemapSubgroupListPaths(assetPack.Subgroups, pathMap);

        // remap paths in replacer subgroups
        foreach (var replacer in assetPack.ReplacerGroups)
        {
            RemapSubgroupListPaths(replacer.Subgroups, pathMap);
        }
    }

    public static void RemapSubgroupListPaths(IEnumerable<AssetPack.Subgroup> subgroups, Dictionary<string, string> pathMap)
    {
        foreach (var subgroup in subgroups)
        {
            foreach (var path in subgroup.Paths)
            {
                if (pathMap.ContainsKey(path.Source))
                {
                    path.Source = pathMap[path.Source];
                }
            }
            RemapSubgroupListPaths(subgroup.Subgroups, pathMap);
        }
    }

    public static string GenerateRemappedPath(string path, Manifest manifest, string folderName, int fileName)
    {
        if (GetExpectedDataFolderFromExtension(path, manifest, out string parentFolder))
        {
            return Path.Join(parentFolder, manifest.ConfigPrefix, folderName, fileName.ToString() + Path.GetExtension(path)); // Path.GetExtension returns a string starting with '.'
        }
        else
        {
            return path;
        }
    }

    public enum PathModifications
    {
        None,
        TrimmedModFolder,
        TrimmedSubFolders
    }

    public static bool PathStartsWithModName(string path)
    {
        string[] split = path.Split(Path.DirectorySeparatorChar);
        if (!split.Any())
        {
            return false;
        }

        string[] split2 = split[0].Split('.');
        if (split2.Length != 2)
        {
            return false;
        }

        string extension = split2[1].ToLower();
        if (extension == "esp" || extension == "esm" || extension == "esl")
        {
            return true;
        }
        return false;
    }


    /* Started work on more intelligent path shortening to more human-readable file paths, but doesn't seem worth the effort. Might come back to it later. 
    public class SimulatedDirectory
    {
        public SimulatedDirectory(string name)
        {
            Name = name;
            Directories = new HashSet<SimulatedDirectory>();
            Files = new HashSet<string>();
        }

        public string Name { get; set; }
        public HashSet<SimulatedDirectory> Directories { get; set; }
        public HashSet<string> Files { get; set; }

        public static SimulatedDirectory GetDirectory(string path, SimulatedDirectory root)
        {
            SimulatedDirectory currentDirectory = root;
            string[] split = path.Split('\\');
            foreach (string s in split)
            {
                currentDirectory = currentDirectory.GetSubDirectory(s);
                if (currentDirectory == null)
                {
                    return null;
                }
            }
            return currentDirectory;
        }

        public SimulatedDirectory GetSubDirectory(string directoryName)
        {
            return this.Directories.Where(x => x.Name == directoryName).FirstOrDefault();
        }
        public SimulatedDirectory CreateSubDirectory(string directoryName)
        {
            SimulatedDirectory newDir = new SimulatedDirectory(directoryName);
            this.Directories.Add(newDir);
            return newDir;
        }
        public bool HasDirectory(string directoryName)
        {
            return this.Directories.Where(x => x.Name == directoryName).Any();
        }

        public static void CreateDirectory(SimulatedDirectory root, string directoryPath)
        {
            SimulatedDirectory currentDirectory = root;
            string[] split = directoryPath.Split('\\');
            foreach (string s in split)
            {
                if (!currentDirectory.HasDirectory(s))
                {
                    currentDirectory.CreateSubDirectory(s);
                }
                currentDirectory = currentDirectory.GetSubDirectory(s);
            }
        }

        public static void CreateFile(SimulatedDirectory root, string filePath)
        {
            SimulatedDirectory currentDirectory = root;
            string[] split = filePath.Split('\\');
            for (int i = 0; i < split.Length - 1; i++)
            {
                string s = split[i];
                if (!currentDirectory.HasDirectory(s))
                {
                    currentDirectory.CreateSubDirectory(s);
                }
                currentDirectory = currentDirectory.GetSubDirectory(s);
            }
            currentDirectory.Files.Add(filePath);
        }
    }*/
}