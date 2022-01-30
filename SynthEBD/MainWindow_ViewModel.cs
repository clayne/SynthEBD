﻿using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SynthEBD
{
    public class MainWindow_ViewModel : INotifyPropertyChanged
    {
        //public GameEnvironmentProvider GameEnvironmentProvider { get; }
        public VM_Settings_General SGVM { get; }
        public VM_SettingsTexMesh TMVM { get; }
        public VM_SettingsBodyGen BGVM { get; }
        public VM_SettingsOBody OBVM { get; }
        public VM_SettingsHeight HVM { get; } = new();
        public VM_SpecificNPCAssignmentsUI SAUIVM { get; }
        public VM_ConsistencyUI CUIVM { get; }
        public VM_BlockListUI BUIVM { get; } = new();
        public VM_SettingsModManager MMVM { get; } = new();
        public VM_NavPanel NavPanel { get; }

        public VM_RunButton RunButton { get; }
        public object DisplayedViewModel { get; set; }
        public object NavViewModel { get; set; }

        public VM_StatusBar StatusBarVM { get; set; }

        public VM_LogDisplay LogDisplayVM { get; set; } = new();
        public List<AssetPack> AssetPacks { get; set; }
        public List<HeightConfig> HeightConfigs { get; set; }
        public BodyGenConfigs BodyGenConfigs { get; set; }
        public Dictionary<string, NPCAssignment> Consistency { get; set; }
        public HashSet<NPCAssignment> SpecificNPCAssignments { get; set; }
        public BlockList BlockList { get; set; }
        public HashSet<string> LinkedNPCNameExclusions { get; set; }
        public HashSet<LinkedNPCGroup> LinkedNPCGroups { get; set; }

        public List<SkyrimMod> RecordTemplatePlugins { get; set; }
        public ILinkCache<ISkyrimMod, ISkyrimModGetter> RecordTemplateLinkCache { get; set; }

        public MainWindow_ViewModel()
        {
            SGVM = new VM_Settings_General(this);
            BGVM = new VM_SettingsBodyGen(SGVM);
            OBVM = new VM_SettingsOBody(SGVM.RaceGroupings, SGVM);
            TMVM = new VM_SettingsTexMesh(this);
            SAUIVM = new VM_SpecificNPCAssignmentsUI(TMVM, BGVM, OBVM, SGVM);
            CUIVM = new VM_ConsistencyUI();

            NavPanel = new SynthEBD.VM_NavPanel(this, SGVM, TMVM, BGVM, OBVM, HVM, SAUIVM, CUIVM, BUIVM, LogDisplayVM, MMVM);

            StatusBarVM = new VM_StatusBar();

            RunButton = new VM_RunButton(this);

            // Load general settings
            SettingsIO_General.loadGeneralSettings();
            VM_Settings_General.GetViewModelFromModel(SGVM);

            // get paths
            PatcherSettings.Paths = new Paths();

            // Initialize patchable races from general settings (required by some UI elements)
            Patcher.MainLinkCache = PatcherEnvironmentProvider.Environment.LinkCache;
            Patcher.ResolvePatchableRaces();

            LoadInitialSettingsViewModels();
            LoadPluginViewModels();
            LoadFinalSettingsViewModels();

            // Start on the settings VM
            DisplayedViewModel = SGVM;
            NavViewModel = NavPanel;
            Logger.Instance.RunButton = RunButton;
            Logger.Instance.MainVM = this;

            Application.Current.MainWindow.Closing += new CancelEventHandler(MainWindow_Closing);
        }

        public void SaveAndRefreshPlugins()
        {
            SavePluginViewModels();
            LoadPluginViewModels();
        }

        public void LoadInitialSettingsViewModels() // view models that should be loaded before plugin VMs
        {
            // Load texture and mesh settings
            PatcherSettings.TexMesh = SettingsIO_AssetPack.LoadTexMeshSettings();
            VM_SettingsTexMesh.GetViewModelFromModel(TMVM, PatcherSettings.TexMesh);

            PatcherSettings.BodyGen = SettingsIO_BodyGen.LoadBodyGenSettings();

            // load OBody settings before asset packs - asset packs depend on BodyGen but not vice versa
            PatcherSettings.OBody = SettingsIO_OBody.LoadOBodySettings();
            PatcherSettings.OBody.ImportBodySlides();

            // load heights
            PatcherSettings.Height = SettingsIO_Height.LoadHeightSettings();

            // load BlockList
            BlockList = SettingsIO_BlockList.LoadBlockList();
            VM_BlockListUI.GetViewModelFromModel(BlockList, BUIVM);

            // load Mod Manager Integration
            PatcherSettings.ModManagerIntegration = SettingsIO_ModManager.LoadModManagerSettings();
            VM_SettingsModManager.GetViewModelFromModel(PatcherSettings.ModManagerIntegration, MMVM);

            // load Misc settings
            LinkedNPCNameExclusions = SettingsIO_Misc.LoadNPCNameExclusions();
            SGVM.LinkedNameExclusions = VM_CollectionMemberString.InitializeCollectionFromHashSet(LinkedNPCNameExclusions);
            LinkedNPCGroups = SettingsIO_Misc.LoadLinkedNPCGroups();
            SGVM.LinkedNPCGroups = VM_LinkedNPCGroup.GetViewModelsFromModels(LinkedNPCGroups);
        }

        public void LoadPluginViewModels()
        {
            // load bodygen configs before asset packs - asset packs depend on BodyGen but not vice versa
            BodyGenConfigs = SettingsIO_BodyGen.loadBodyGenConfigs(PatcherSettings.General.RaceGroupings);
            VM_SettingsBodyGen.GetViewModelFromModel(BodyGenConfigs, PatcherSettings.BodyGen, BGVM, SGVM);

            VM_SettingsOBody.GetViewModelFromModel(PatcherSettings.OBody, OBVM, SGVM.RaceGroupings);

            RecordTemplatePlugins = SettingsIO_AssetPack.LoadRecordTemplates();
            RecordTemplateLinkCache = RecordTemplatePlugins.ToImmutableLinkCache();

            // load asset packs
            AssetPacks = SettingsIO_AssetPack.LoadAssetPacks(PatcherSettings.General.RaceGroupings, RecordTemplatePlugins, BodyGenConfigs); // load asset pack models from json
            VM_AssetPack.GetViewModelsFromModels(TMVM.AssetPacks, AssetPacks, SGVM, PatcherSettings.TexMesh, BGVM, OBVM.DescriptorUI, RecordTemplateLinkCache, this); // add asset pack view models to TexMesh shell view model here

            // load heights
            HeightConfigs = SettingsIO_Height.loadHeightConfigs();
            VM_HeightConfig.GetViewModelsFromModels(HVM.AvailableHeightConfigs, HeightConfigs);
            VM_SettingsHeight.GetViewModelFromModel(HVM, PatcherSettings.Height); /// must do after populating configs
        }

        public void LoadFinalSettingsViewModels() // view models that should be loaded after plugin VMs because they depend on the loaded plugins
        {
            // load specific assignments (must load after plugin view models)
            SpecificNPCAssignments = SettingsIO_SpecificNPCAssignments.LoadAssignments();
            VM_SpecificNPCAssignmentsUI.GetViewModelFromModels(SAUIVM, SpecificNPCAssignments, OBVM, SGVM);

            // Load Consistency (must load after plugin view models)
            Consistency = SettingsIO_Misc.LoadConsistency();
            VM_ConsistencyUI.GetViewModelsFromModels(Consistency, CUIVM.Assignments, TMVM.AssetPacks);
        }

        public void SavePluginViewModels()
        {
            VM_AssetPack.DumpViewModelsToModels(TMVM.AssetPacks, AssetPacks);
            VM_HeightConfig.DumpViewModelsToModels(HVM.AvailableHeightConfigs, HeightConfigs);
            VM_SettingsBodyGen.DumpViewModelToModel(BGVM, PatcherSettings.BodyGen, BodyGenConfigs);
        }

        public void DumpViewModelsToModels()
        {
            VM_Settings_General.DumpViewModelToModel(SGVM, PatcherSettings.General);
            VM_SettingsTexMesh.DumpViewModelToModel(TMVM, PatcherSettings.TexMesh);
            VM_AssetPack.DumpViewModelsToModels(TMVM.AssetPacks, AssetPacks);
            VM_SettingsHeight.DumpViewModelToModel(HVM, PatcherSettings.Height);
            VM_HeightConfig.DumpViewModelsToModels(HVM.AvailableHeightConfigs, HeightConfigs);
            VM_SettingsBodyGen.DumpViewModelToModel(BGVM, PatcherSettings.BodyGen, BodyGenConfigs);
            VM_SettingsOBody.DumpViewModelToModel(PatcherSettings.OBody, OBVM);
            VM_SpecificNPCAssignmentsUI.DumpViewModelToModels(SAUIVM, SpecificNPCAssignments);
            VM_BlockListUI.DumpViewModelToModel(BUIVM, BlockList);
            VM_ConsistencyUI.DumpViewModelsToModels(CUIVM.Assignments, Consistency);
            VM_LinkedNPCGroup.DumpViewModelsToModels(LinkedNPCGroups, SGVM.LinkedNPCGroups);
            VM_SettingsModManager.DumpViewModelToModel(PatcherSettings.ModManagerIntegration, MMVM);
        }

        void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            DumpViewModelsToModels();

            JSONhandler<Settings_General>.SaveJSONFile(PatcherSettings.General, Paths.GeneralSettingsPath);

            JSONhandler<Settings_TexMesh>.SaveJSONFile(PatcherSettings.TexMesh, PatcherSettings.Paths.TexMeshSettingsPath);
            SettingsIO_AssetPack.SaveAssetPacks(AssetPacks);

            JSONhandler<Settings_Height>.SaveJSONFile(PatcherSettings.Height, PatcherSettings.Paths.HeightSettingsPath);
            SettingsIO_Height.SaveHeightConfigs(HeightConfigs);

            JSONhandler<Settings_BodyGen>.SaveJSONFile(PatcherSettings.BodyGen, PatcherSettings.Paths.BodyGenSettingsPath);
            SettingsIO_BodyGen.SaveBodyGenConfigs(BodyGenConfigs.Female);
            SettingsIO_BodyGen.SaveBodyGenConfigs(BodyGenConfigs.Male);

            JSONhandler<Settings_OBody>.SaveJSONFile(PatcherSettings.OBody, PatcherSettings.Paths.OBodySettingsPath);

            SettingsIO_Misc.SaveConsistency(Consistency);

            JSONhandler<HashSet<NPCAssignment>>.SaveJSONFile(SpecificNPCAssignments, PatcherSettings.Paths.SpecificNPCAssignmentsPath);
            JSONhandler<BlockList>.SaveJSONFile(BlockList, PatcherSettings.Paths.BlockListPath);

            SettingsIO_Misc.SaveLinkedNPCGroups(LinkedNPCGroups);

            SettingsIO_Misc.SaveNPCNameExclusions(SGVM.LinkedNameExclusions.Select(cms => cms.Content).ToHashSet());

            SettingsIO_Misc.SaveTrimPaths(TMVM.TrimPaths.ToHashSet());

            JSONhandler<Settings_ModManager>.SaveJSONFile(PatcherSettings.ModManagerIntegration, PatcherSettings.Paths.ModManagerSettingsPath);
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

}
