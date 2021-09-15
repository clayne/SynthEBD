﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_SettingsTexMesh : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public VM_SettingsTexMesh()
        {
            this.bChangeNPCTextures = true;
            this.bChangeNPCMeshes = true;
            this.bApplyToNPCsWithCustomSkins = true;
            this.bApplyToNPCsWithCustomFaces = true;
            this.bForwardArmatureFromExistingWNAMs = true;
            this.bDisplayPopupAlerts = true;
            this.bGenerateAssignmentLog = true;
            this.AssetPacks = new ObservableCollection<VM_AssetPack>();
        }

        public bool bChangeNPCTextures { get; set; }
        public bool bChangeNPCMeshes { get; set; }
        public bool bApplyToNPCsWithCustomSkins { get; set; }
        public bool bApplyToNPCsWithCustomFaces { get; set; }
        public bool bForwardArmatureFromExistingWNAMs { get; set; }
        public bool bDisplayPopupAlerts { get; set; }
        public bool bGenerateAssignmentLog { get; set; }
        public ObservableCollection<VM_AssetPack> AssetPacks { get; set; }

        public static void GetViewModelFromModel(VM_SettingsTexMesh viewModel, Settings_TexMesh model)
        {
            viewModel.bChangeNPCTextures = model.bChangeNPCTextures;
            viewModel.bChangeNPCMeshes = model.bChangeNPCMeshes;
            viewModel.bApplyToNPCsWithCustomSkins = model.bApplyToNPCsWithCustomSkins;
            viewModel.bApplyToNPCsWithCustomFaces = model.bApplyToNPCsWithCustomFaces;
            viewModel.bForwardArmatureFromExistingWNAMs = model.bForwardArmatureFromExistingWNAMs;
            viewModel.bDisplayPopupAlerts = model.bDisplayPopupAlerts;
            viewModel.bGenerateAssignmentLog = model.bGenerateAssignmentLog;
        }

        public static void DumpViewModelToModel(VM_SettingsTexMesh viewModel, Settings_TexMesh model)
        {
            model.bChangeNPCTextures = viewModel.bChangeNPCTextures;
            model.bChangeNPCMeshes = viewModel.bChangeNPCMeshes;
            model.bApplyToNPCsWithCustomSkins = viewModel.bApplyToNPCsWithCustomSkins;
            model.bApplyToNPCsWithCustomFaces = viewModel.bApplyToNPCsWithCustomFaces;
            model.bForwardArmatureFromExistingWNAMs = viewModel.bForwardArmatureFromExistingWNAMs;
            model.bDisplayPopupAlerts = viewModel.bDisplayPopupAlerts;
            model.bGenerateAssignmentLog = viewModel.bGenerateAssignmentLog;
        }
    }
}