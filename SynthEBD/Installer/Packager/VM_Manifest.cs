using DynamicData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SynthEBD
{
    public class VM_Manifest : VM
    {
        private readonly Logger _logger;
        private readonly IEnvironmentStateProvider _environmentStateProvider;
        public delegate VM_Manifest Factory(); 
        public VM_Manifest(Logger logger, IEnvironmentStateProvider environmentStateProvider)
        {
            _logger = logger;
            _environmentStateProvider = environmentStateProvider;

            VM_PackagerOption root = new(Options, this, true, _environmentStateProvider.LinkCache);
            Options.Add(root);
            SelectedNode = root;

            ImportCommand = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    if (IO_Aux.SelectFile("", "Config files (*.json)|*.json", "Select manifest.json to import", out string path))
                    {
                        var model = JSONhandler<Manifest>.LoadJSONFile(path, out bool success, out string exceptionStr);
                        if (success)
                        {
                            GetViewModelFromModel(model);
                        }
                        else
                        {
                            CustomMessageBox.DisplayNotificationOK("Import Failed", "Manifest.json import failed with the following error: " + Environment.NewLine + exceptionStr);
                        }
                    }
                });

            ExportCommand = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    if (IO_Aux.SelectFolder("", out string path))
                    {
                        string dest = System.IO.Path.Combine(path, "Manifest.json");
                        var model = DumpViewModelToModel();
                        JSONhandler<Manifest>.SaveJSONFile(model, dest, out bool success, out string exceptionStr);
                        if (success)
                        {
                            _logger.CallTimedNotifyStatusUpdateAsync("Saved to " + path, 3);
                        }
                        else
                        {
                            CustomMessageBox.DisplayNotificationOK("Export Failed", "Manifest.json export failed with the following error: " + Environment.NewLine + exceptionStr);
                        }
                    }
                });

            SelectedNodeChanged = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => this.SelectedNode = (VM_PackagerOption)x
                );

            AddRootNode = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    Options.Add(new VM_PackagerOption(Options, this, true, _environmentStateProvider.LinkCache));
                });

            SetRootDirectory = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    if (IO_Aux.SelectFolder("", out string path))
                    {
                        RootDirectory = path;
                    }
                });
        }
        public string ConfigName { get; set; } = "New Config";
        public string ConfigDescription { get; set; } = "";
        public string ConfigPrefix { get; set; } = "Prefix";
        public ObservableCollection<VM_PackagerOption> Options { get; set; } = new();
        public string InstallationMessage { get; set; } = string.Empty;
        public string RootDirectory { get; set; } = string.Empty;
        public RelayCommand ExportCommand { get; set; }
        public RelayCommand ImportCommand { get; set; }
        public RelayCommand SelectedNodeChanged { get; set; }
        public RelayCommand SetRootDirectory { get; set; }
        public VM_PackagerOption SelectedNode { get; set; }
        public RelayCommand AddRootNode { get; set; }

        public void GetViewModelFromModel(Manifest model)
        {
            ConfigName = model.ConfigName;
            ConfigDescription = model.ConfigDescription;
            ConfigPrefix = model.ConfigPrefix;
            InstallationMessage = model.InstallationMessage;

            Options.Clear();

            if (model.Version == 0) // compatibility for legacy installer that only had one root node within the manifest itself
            {
                var root = new VM_PackagerOption(Options, this, true, _environmentStateProvider.LinkCache);
                root.Name = "Root";
                root.DownloadInfo = new(model.DownloadInfo.Select(x => VM_DownloadInfoContainer.GetViewModelFromModel(x, root)));
                root.OptionsDescription = model.OptionsDescription;
                root.AssetPackPaths = VM_CollectionMemberStringDecorated.InitializeObservableCollectionFromICollection(model.AssetPackPaths);
                root.BodyGenConfigPaths = VM_CollectionMemberStringDecorated.InitializeObservableCollectionFromICollection(model.BodyGenConfigPaths);
                root.RecordTemplatePaths = VM_CollectionMemberStringDecorated.InitializeObservableCollectionFromICollection(model.RecordTemplatePaths);
                root.DestinationModFolder = model.DestinationModFolder;
                if (model.FileExtensionMap.Any())
                {
                    root.FileExtensionMap = GetFileExtensionMapFromModel(model.FileExtensionMap);
                }
                else
                {
                    root.FileExtensionMap.Add(new string[] { "dds", "textures" });
                    root.FileExtensionMap.Add(new string[] { "nif", "meshes" });
                    root.FileExtensionMap.Add(new string[] { "tri", "meshes" });
                }
                root.Options.AddRange(model.Options.Select(x => VM_PackagerOption.GetViewModelFromModel(x, root.Options, this, _environmentStateProvider.LinkCache)));
                Options.Add(root);
            }
            else
            {
                Options.AddRange(model.Options.Select(x => VM_PackagerOption.GetViewModelFromModel(x, Options, this, _environmentStateProvider.LinkCache)));
            }

            if (Options.Any())
            {
                SelectedNode = Options.First();
            }
        }

        public static ObservableCollection<string[]> GetFileExtensionMapFromModel(Dictionary<string, string> model)
        {
            ObservableCollection<string[]> map = new();
            foreach (var entry in model)
            {
                string[] vm = new string[2] { entry.Key, entry.Value };
                map.Add(vm);
            }
            return map;
        }

        public Manifest DumpViewModelToModel()
        {
            Manifest model = new();
            model.ConfigName = ConfigName;
            model.ConfigDescription = ConfigDescription;
            model.ConfigPrefix = ConfigPrefix;
            model.InstallationMessage = InstallationMessage;
            model.Options.Clear();
            foreach (var option in Options)
            {
                model.Options.Add(option.DumpViewModelToModel());
            }
            model.Version = 1;
            return model;
        }
    }
}
