using DynamicData;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Noggog;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Mutagen.Bethesda.Plugins.Binary.Processing.BinaryFileProcessor;

namespace SynthEBD;

public class VM_ConfigDrafter : VM
{
    private readonly ConfigDrafter _configDrafter;
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly VM_DrafterArchiveContainer.Factory _archiveContainerFactory;
    private readonly VM_7ZipInterface.Factory _7ZipInterfaceVM;
    private readonly _7ZipInterface temp7z;

    public VM_ConfigDrafter(ConfigDrafter configDrafter, IEnvironmentStateProvider environmentProvider, PatcherState patcherState, VM_DrafterArchiveContainer.Factory archiveContainerFactory, VM_7ZipInterface.Factory sevenZipInterfaceVM, _7ZipInterface tmp)
    {
        _configDrafter = configDrafter;
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _archiveContainerFactory = archiveContainerFactory;
        _7ZipInterfaceVM = sevenZipInterfaceVM;
        temp7z = tmp;

        DraftConfigButton = ReactiveCommand.CreateFromTask(
            execute: async _ =>
            {
                HashSet<string> unmatchedTextures = new();
                switch (SelectedSource)
                {
                    case DrafterTextureSource.Archives: 
                        if (await ValidateContainers())
                        {
                            var destinationDirs = await ExtractArchives();
                            _configDrafter.DraftConfigFromTextures(CurrentConfig, destinationDirs, true, unmatchedTextures);
                        }
                        break;
                    case DrafterTextureSource.Directory:
                        if (ValidateExistingDirectory())
                        {
                            _configDrafter.DraftConfigFromTextures(CurrentConfig, new List<string>() { SelectedTextureFolder }, false, unmatchedTextures);
                        }
                        break;
                    default: throw new NotImplementedException();
                }

                CurrentConfig.GroupName = GeneratedModName.IsNullOrWhitespace() ? "New Asset Pack" : GeneratedModName;

                UnmatchedTextures = string.Join(Environment.NewLine, unmatchedTextures);
                HasUnmatchedTextures = unmatchedTextures.Any();
            });

        AddFileArchiveButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                SelectedFileArchives.Add(_archiveContainerFactory());
            });

        SelectDirectoryButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFolder("", out string path))
                {
                    SelectedTextureFolder = path;
                }
            });
    }

    public VM_AssetPack CurrentConfig { get; set; }

    public string GeneratedModName { get; set; }
    public bool LockGeneratedModName { get; set; }
    public string ModNameToolTip { get; set; }

    public DrafterTextureSource SelectedSource { get; set; } = DrafterTextureSource.Archives;
    public ObservableCollection<VM_DrafterArchiveContainer> SelectedFileArchives { get; set; } = new();
    public string SelectedTextureFolder { get; set; }

    public string UnmatchedTextures { get; set; }
    public bool HasUnmatchedTextures { get; set; } = false;

    public IReactiveCommand DraftConfigButton { get; }
    public RelayCommand AddFileArchiveButton { get; }
    public RelayCommand SelectDirectoryButton { get; }

    public void InitializeTo(VM_AssetPack config)
    {
        CurrentConfig = config;

        LockGeneratedModName = !(_patcherState.ModManagerSettings.ModManagerType == ModManager.ModOrganizer2 && !_patcherState.ModManagerSettings.MO2Settings.ModFolderPath.IsNullOrWhitespace() && Directory.Exists(_patcherState.ModManagerSettings.MO2Settings.ModFolderPath)) &&
            !(_patcherState.ModManagerSettings.ModManagerType == ModManager.Vortex && !_patcherState.ModManagerSettings.VortexSettings.StagingFolderPath.IsNullOrWhitespace() && Directory.Exists(_patcherState.ModManagerSettings.VortexSettings.StagingFolderPath));
        if (LockGeneratedModName)
        {
            GeneratedModName = "You can only select a mod name if you have filled out your mods folder in the Mod Manager Integration Settings";
            ModNameToolTip = "Textures will be extracted to your Data folder (or overwrite if using MO2)";
        }
        else
        {
            GeneratedModName = String.Empty;
            ModNameToolTip = "A new mod with this name will appear in your mod manager's mod list after drafting";
        }

        if (!SelectedFileArchives.Any())
        {
            SelectedFileArchives.Add(_archiveContainerFactory());
        }
        UnmatchedTextures = "";
        HasUnmatchedTextures = false;
    }

    public bool ValidateExistingDirectory()
    {
        if (!Directory.Exists(SelectedTextureFolder))
        {
            CustomMessageBox.DisplayNotificationOK("Drafter Error", "The selected mod directory does not exist");
            return false;
        }

        var texturesDir = Path.Combine(SelectedTextureFolder, "Textures");
        if (!Directory.Exists(texturesDir))
        {
            CustomMessageBox.DisplayNotificationOK("Drafter Error", "The selected mod directory must contain a Textures folder");
            return false;
        }
        if (Directory.GetDirectories(texturesDir).Length < 1)
        {
            CustomMessageBox.DisplayNotificationOK("Drafter Error", "The selected mod directory must contain a Textures\\* folder");
            return false;
        }
        return true;
    }

    public async Task<bool> ValidateContainers()
    {
        if (!SelectedFileArchives.Any())
        {
            CustomMessageBox.DisplayNotificationOK("Drafter Error", "No file archives were selected");
            return false;
        }

        if (GeneratedModName.IsNullOrWhitespace())
        {
            CustomMessageBox.DisplayNotificationOK("Drafter Error", "You must enter a name for the mod to which the files will be extracted. Recommend something like \"SynthEBD - This Texture Mod\"");
            return false;
        }

        List<string> fileNames = new();
        List<List<string>> archiveContents = new();

        for (int i = 0; i < SelectedFileArchives.Count; i++)
        {
            var container = SelectedFileArchives[i];

            if (fileNames.Contains(container.FilePath))
            {
                CustomMessageBox.DisplayNotificationOK("Drafter Error", "Cannot select the same file multiple times: " + Environment.NewLine + container.FilePath);
                return false;
            }
            fileNames.Add(container.FilePath);

            if (!File.Exists(container.FilePath))
            {
                CustomMessageBox.DisplayNotificationOK("Drafter Error", "The following file from Archive #" + (i + 1).ToString() + " does not exist: " + Environment.NewLine + container.FilePath);
                return false;
            }
            if (container.Prefix.IsNullOrWhitespace())
            {
                CustomMessageBox.DisplayNotificationOK("Drafter Error", "You need to assign a prefix to Archive #" + (i + 1).ToString());
                return false;
            }

            //var currentArchiveContents = await temp7z.GetArchiveContents(container.FilePath, true);
            var currentArchiveContents = await _7ZipInterfaceVM().GetArchiveContents(container.FilePath, true, _patcherState.GeneralSettings.Close7ZipWhenFinished, 1000);
            if (!currentArchiveContents.Any())
            {
                CustomMessageBox.DisplayNotificationOK("Drafter Error", "The following file has no contents upon extraction: " + Environment.NewLine + container.FilePath);
                return false;
            }
            foreach (var path in currentArchiveContents)
            {
                for (int j = 0; j < archiveContents.Count; j++)
                {
                    if (archiveContents[j].Contains(path) && container.Prefix == SelectedFileArchives[j].Prefix)
                    {
                        CustomMessageBox.DisplayNotificationOK("Drafter Error", "The following file in Archive #" + (i + 1).ToString() + " was also found in Archive # " + (j + 1).ToString() + ":" + Environment.NewLine + path + Environment.NewLine + "You must assign a different Prefix to one of these archives");
                        return false;
                    }
                }
            }
            archiveContents.Add(currentArchiveContents);
        }

        return true;
    }

    public async Task<List<string>> ExtractArchives()
    {
        List<string> destinationDirs = new();
        var destinationDir = "";
        foreach (var archiveFile in SelectedFileArchives)
        {
            if (_patcherState.ModManagerSettings.ModManagerType == ModManager.None)
            {
                destinationDir = Path.Combine(_environmentProvider.DataFolderPath, "Textures", archiveFile.Prefix);
            }
            else
            {
                destinationDir = Path.Combine(_patcherState.ModManagerSettings.CurrentInstallationFolder, GeneratedModName, "Textures", archiveFile.Prefix);
            }

            destinationDirs.Add(destinationDir);
            var succes = await _7ZipInterfaceVM().ExtractArchive(archiveFile.FilePath, destinationDir, true, _patcherState.GeneralSettings.Close7ZipWhenFinished, 1000);
        }
        return destinationDirs;
    }

    private void GetPrefixName()
    {

    }
}

public class VM_DrafterArchiveContainer : VM
{
    public delegate VM_DrafterArchiveContainer Factory();
    private readonly VM_ConfigDrafter _configDrafter;
    public VM_DrafterArchiveContainer(VM_ConfigDrafter drafter)
    {
        _configDrafter = drafter;

        SelectArchive = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFile("", "Archive Files (*.7z;*.zip;*.rar)|*.7z;*.zip;*.rar|" + "All files (*.*)|*.*", "Select a zipped archive", out string path))
                {
                    FilePath = path;
                }
            });

        DeleteMe = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                _configDrafter.SelectedFileArchives.Remove(this);
            });
    }
    public string FilePath { get; set; }
    public string Prefix { get; set; }
    public RelayCommand SelectArchive { get; }
    public RelayCommand DeleteMe { get; }
}

public enum DrafterTextureSource
{
    Archives,
    Directory
}