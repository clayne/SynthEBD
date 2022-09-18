using System.Collections.ObjectModel;
using System.Windows.Documents;
using ReactiveUI;

namespace SynthEBD;

public class VM_ConfigSelector : VM
{
    public VM_ConfigSelector(Manifest manifest, Window_ConfigInstaller window, VM_ConfigInstaller parentVM)
    {
        Manifest = manifest;
        AssociatedWindow = window;
        Name = manifest.ConfigName;
        Description = manifest.ConfigDescription;
        AssetPackPaths = new ObservableCollection<string>(manifest.AssetPackPaths);
        RecordTemplatePaths = new ObservableCollection<string>(manifest.RecordTemplatePaths);
        BodyGenConfigPaths = new ObservableCollection<string>(manifest.BodyGenConfigPaths);
        DownloadInfo = manifest.DownloadInfo;
        LastSelectionChainIndex = manifest.Options.Count - 1;

        InitializeOptions(manifest);

        Installer = this;

        OKvisibility = this.Options == null || !this.Options.Any();

        parentVM.InstallationMessage = manifest.InstallationMessage;

        this.WhenAnyValue(x => x.SelectedOption).Subscribe(_ =>
        {
            SelectionMade();
        });

        Back = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                BackTrack();
            }
        );

        Cancel = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                parentVM.Cancelled = true;
                AssociatedWindow.Close();
            }
        );

        OK = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                Finalize(parentVM);
            }
        );
    }

    public Manifest Manifest { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string DestinationModFolder { get; set; }
    public ObservableCollection<string> AssetPackPaths { get; set; }
    public ObservableCollection<string> RecordTemplatePaths { get; set; }
    public ObservableCollection<string> BodyGenConfigPaths { get; set; }
    public HashSet<Manifest.DownloadInfoContainer> DownloadInfo { get; set; }
    public ObservableCollection<VM_Option> Options { get; set; } = new();
    public string DisplayedOptionsDescription { get; set; }
    public ObservableCollection<VM_Option> DisplayedOptions { get; set; }
    public VM_Option SelectedOption { get; set; } = null;
    public VM_ConfigSelector Installer { get; set; }
    public RelayCommand Back { get; }
    public bool BackVisibility { get; set; } = false;
    public RelayCommand OK { get; }
    public bool OKvisibility { get; set; }

    public List<VM_Option> CurrentSelectionChain { get; set; } = new();
    public List<List<VM_Option>> SelectionChains { get; set; } = new();
    public int CurrentSelectionChainIndex { get; set; } = 0;
    private int CurrentSelectionChainDepth { get; set; } = 0;
    private int LastSelectionChainIndex { get; set; } = 0;
    public RelayCommand Cancel { get; }

    private bool BackFlag { get; set; } = false;

    public HashSet<string> SelectedAssetPackPaths { get; set; } = new();
    public HashSet<string> SelectedRecordTemplatePaths { get; set; } = new();
    public HashSet<string> SelectedBodyGenConfigPaths { get; set; } = new();

    public Window_ConfigInstaller AssociatedWindow { get; set; }

    private void InitializeOptions(Manifest manifest)
    {
        if (manifest.Options.Any())
        {
            foreach (var option in manifest.Options) { SelectionChains.Add(new List<VM_Option>()); }
            CurrentSelectionChain = SelectionChains.First();
            SelectedOption = new VM_Option(manifest.Options.First(), null, this);
        }
    }
    private void SelectionMade()
    {
        if (SelectedOption is not null && BackFlag == false)
        {
            CurrentSelectionChain.Add(SelectedOption);
            CurrentSelectionChainDepth++;
            DisplayedOptionsDescription = SelectedOption.OptionsDescription;
            DisplayedOptions = SelectedOption.Options;

            bool hasDisplayedOptions = DisplayedOptions != null && DisplayedOptions.Any();

            if (CurrentSelectionChainIndex == LastSelectionChainIndex && !hasDisplayedOptions)
            {
                OKvisibility = true;
            }
            else
            {
                OKvisibility = false;

                if (!hasDisplayedOptions)
                {
                    CurrentSelectionChainIndex++;
                    CurrentSelectionChain = SelectionChains[CurrentSelectionChainIndex];
                    SelectedOption = new VM_Option(this.Manifest.Options[CurrentSelectionChainIndex], null, this);
                }
            }
        }

        if (CurrentSelectionChain.Count > 1 || CurrentSelectionChainIndex > 0)
        {
            BackVisibility = true;
        }
        else
        {
            BackVisibility = false;
        }
    }
    
    private void BackTrack()
    {
        BackFlag = true;
        CurrentSelectionChain.Remove(CurrentSelectionChain.Last());
        foreach (var option in CurrentSelectionChain.Last().Options)
        {
            option.IsSelected = false;
        }
        if (CurrentSelectionChainDepth > 0)
        {
            CurrentSelectionChainDepth--;
        }
        else if (CurrentSelectionChainIndex > 0)
        {
            CurrentSelectionChainIndex--;
            CurrentSelectionChain = SelectionChains[CurrentSelectionChainIndex];
            CurrentSelectionChainDepth = CurrentSelectionChain.Count - 1;
            SelectedOption = CurrentSelectionChain.Last();
        }
        else
        {
            return; // should never be reached because the back button isn't visible in this case
        }
        SelectedOption = CurrentSelectionChain.Last(); // Triggers subscription immediately; subscription happens before moving on to next line
        DisplayedOptionsDescription = SelectedOption.OptionsDescription;
        DisplayedOptions = SelectedOption.Options;

        if (DisplayedOptions == null || !DisplayedOptions.Any())
        {
            OKvisibility = true;
        }
        else
        {
            OKvisibility = false;
        }

        BackFlag = false;
    }

    private void Finalize(VM_ConfigInstaller parentVM)
    {
        foreach (var selection in CurrentSelectionChain)
        {
            SelectedAssetPackPaths.UnionWith(selection.AssetPackPaths);
            SelectedRecordTemplatePaths.UnionWith(selection.RecordTemplatePaths);
            SelectedBodyGenConfigPaths.UnionWith(selection.BodyGenConfigPaths);
            DownloadInfo.UnionWith(selection.DownloadInfo);
            if (!string.IsNullOrWhiteSpace(selection.DestinationModFolder))
            {
                Manifest.DestinationModFolder = selection.DestinationModFolder;
            }
        }
        parentVM.DownloadMenu = new VM_DownloadCoordinator(DownloadInfo, AssociatedWindow, parentVM);
        parentVM.DisplayedViewModel = parentVM.DownloadMenu;
    }
}
public class VM_Option : VM
{
    public VM_Option(Manifest.Option option, VM_Option parent, VM_ConfigSelector installer)
    {
        Name = option.Name;
        Description = option.Description;
        AssetPackPaths = new ObservableCollection<string>(option.AssetPackPaths);
        RecordTemplatePaths = new ObservableCollection<string>(option.RecordTemplatePaths);
        BodyGenConfigPaths = new ObservableCollection<string>(option.BodyGenConfigPaths);
        DownloadInfo = option.DownloadInfo;
        OptionsDescription = option.OptionsDescription;
        foreach (var subOption in option.Options)
        {
            Options.Add(new VM_Option(subOption, this, installer));
        }

        Parent = parent;
        Installer = installer;

        DestinationModFolder = option.DestinationModFolder;
    }

    public string Name { get; set; }
    public string Description { get; set; }
    public ObservableCollection<string> AssetPackPaths { get; set; }
    public ObservableCollection<string> RecordTemplatePaths { get; set; }
    public ObservableCollection<string> BodyGenConfigPaths { get; set; }
    public HashSet<Manifest.DownloadInfoContainer> DownloadInfo { get; set; }
    public ObservableCollection<VM_Option> Options { get; set; } = new ObservableCollection<VM_Option>() ?? new ObservableCollection<VM_Option>();
    public string OptionsDescription { get; set; }
    public VM_Option SelectedOption { get; set; } = null;
    public VM_Option Parent { get; set; }
    public bool IsSelected { get; set; }
    public VM_ConfigSelector Installer { get; set; }
    public string DestinationModFolder { get; set; }
}