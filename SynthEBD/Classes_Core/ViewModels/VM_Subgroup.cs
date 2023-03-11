using System.Collections.ObjectModel;
using System.ComponentModel;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Specialized;
using ReactiveUI;
using GongSolutions.Wpf.DragDrop;
using DynamicData.Binding;
using System.Text.RegularExpressions;
using static SynthEBD.VM_NPCAttribute;
using System.Reactive.Linq;

namespace SynthEBD;

public class VM_Subgroup : VM, IDropTarget
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Logger _logger;
    private readonly VM_NPCAttributeCreator _attributeCreator;
    private readonly VM_FilePathReplacementMenu.Factory _filePathReplacementMenuFactory;
    private readonly VM_FilePathReplacement.Factory _filePathReplacementFactory;
    private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _descriptorSelectionFactory;

    public delegate VM_Subgroup Factory(
        VM_SubgroupPlaceHolder associatedPlaceHolder,
        VM_AssetPack parentAssetPack,
        bool setExplicitReferenceNPC);

    public VM_Subgroup(
        VM_SubgroupPlaceHolder associatedPlaceHolder, 
        IEnvironmentStateProvider environmentProvider,
        Logger logger,
        VM_AssetPack parentAssetPack,
        bool setExplicitReferenceNPC,
        VM_SettingsOBody oBody,
        VM_NPCAttributeCreator attributeCreator,
        VM_FilePathReplacementMenu.Factory filePathReplacementMenuFactory,
        VM_FilePathReplacement.Factory filePathReplacementFactory,
        VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory)
    {
        _environmentProvider = environmentProvider;
        _logger = logger;
        _attributeCreator = attributeCreator;
        _filePathReplacementMenuFactory = filePathReplacementMenuFactory;
        _filePathReplacementFactory = filePathReplacementFactory;
        _descriptorSelectionFactory = descriptorSelectionFactory;

        AssociatedPlaceHolder = associatedPlaceHolder;
        AssociatedPlaceHolder.AssociatedViewModel = this;

        SetExplicitReferenceNPC = setExplicitReferenceNPC;
        ParentAssetPack = parentAssetPack;
        SubscribedRaceGroupings = ParentAssetPack.RaceGroupingEditor.RaceGroupings;

        AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(SubscribedRaceGroupings);
        DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(SubscribedRaceGroupings);

        //RefreshBodyGenDescriptors();
        this.WhenAnyValue(x => x.ParentAssetPack.TrackedBodyGenConfig).Subscribe(_ => RefreshBodyGenDescriptors()).DisposeWith(this);
        AllowedBodySlideDescriptors = _descriptorSelectionFactory(oBody.DescriptorUI, SubscribedRaceGroupings, parentAssetPack, true, DescriptorMatchMode.All);
        DisallowedBodySlideDescriptors = _descriptorSelectionFactory(oBody.DescriptorUI, SubscribedRaceGroupings, parentAssetPack, true, DescriptorMatchMode.Any);

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => LinkCache = x)
            .DisposeWith(this);

        //UI-related
        RequiredSubgroups.ToObservableChangeSet().Subscribe(x => RefreshListBoxLabel(RequiredSubgroups, SubgroupListBox.Required)).DisposeWith(this);
        ExcludedSubgroups.ToObservableChangeSet().Subscribe(x => RefreshListBoxLabel(ExcludedSubgroups, SubgroupListBox.Excluded)).DisposeWith(this);

        // must be set after Parent Asset Pack
        if (SetExplicitReferenceNPC)
        {
            PathsMenu = _filePathReplacementMenuFactory(this, SetExplicitReferenceNPC, LinkCache);
        }
        else
        {
            PathsMenu = _filePathReplacementMenuFactory(this, SetExplicitReferenceNPC, parentAssetPack.RecordTemplateLinkCache);
            parentAssetPack.WhenAnyValue(x => x.RecordTemplateLinkCache).Subscribe(x => PathsMenu.ReferenceLinkCache = parentAssetPack.RecordTemplateLinkCache).DisposeWith(this);
        }

        AutoGenerateIDcommand = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (AssociatedPlaceHolder != null)
                {
                    AssociatedPlaceHolder.AutoGenerateID(false, 0);
                    ID = AssociatedPlaceHolder.ID;
                }
            });

        AutoGenerateID_Children_Command = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                if (AssociatedPlaceHolder != null)
                {
                    AssociatedPlaceHolder.AutoGenerateID(true, 1);
                    ID = AssociatedPlaceHolder.ID;
                }
            });

        AutoGenerateID_All_Command = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => ParentAssetPack.AutoGenerateSubgroupIDs()
        );

        AddAllowedAttribute = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => AllowedAttributes.Add(_attributeCreator.CreateNewFromUI(AllowedAttributes, true, null, parentAssetPack.AttributeGroupMenu.Groups))
        );

        AddDisallowedAttribute = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => DisallowedAttributes.Add(_attributeCreator.CreateNewFromUI(DisallowedAttributes, false, null, parentAssetPack.AttributeGroupMenu.Groups))
        );

        AddNPCKeyword = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => AddKeywords.Add(new VM_CollectionMemberString("", AddKeywords))
        );

        AddPath = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => PathsMenu.Paths.Add(filePathReplacementFactory(PathsMenu))
        );

        DeleteRequiredSubgroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute:  x => { RequiredSubgroups.Remove((VM_SubgroupPlaceHolder)x); RefreshListBoxLabel(RequiredSubgroups, SubgroupListBox.Required); }
        );

        DeleteExcludedSubgroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => { ExcludedSubgroups.Remove((VM_SubgroupPlaceHolder)x); RefreshListBoxLabel(ExcludedSubgroups, SubgroupListBox.Excluded); }
        );
    }

    public VM_SubgroupPlaceHolder AssociatedPlaceHolder { get; set; }
    public string ID { get; set; } = "";
    public string Name { get; set; } = "New";
    public bool Enabled { get; set; } = true;
    public bool DistributionEnabled { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
    public ObservableCollection<FormKey> AllowedRaces { get; set; } = new();
    public VM_RaceGroupingCheckboxList AllowedRaceGroupings { get; set; }
    public ObservableCollection<FormKey> DisallowedRaces { get; set; } = new();
    public VM_RaceGroupingCheckboxList DisallowedRaceGroupings { get; set; }
    public ObservableCollection<VM_NPCAttribute> AllowedAttributes { get; set; } = new();
    public ObservableCollection<VM_NPCAttribute> DisallowedAttributes { get; set; } = new();
    public bool AllowUnique { get; set; } = true;
    public bool AllowNonUnique { get; set; } = true;
    public ObservableCollection<VM_SubgroupPlaceHolder> RequiredSubgroups { get; set; } = new();
    public ObservableCollection<VM_SubgroupPlaceHolder> ExcludedSubgroups { get; set; } = new();
    public ObservableCollection<VM_CollectionMemberString> AddKeywords { get; set; } = new();
    public double ProbabilityWeighting { get; set; } = 1;
    public VM_FilePathReplacementMenu PathsMenu { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu AllowedBodyGenDescriptors { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu DisallowedBodyGenDescriptors { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu AllowedBodySlideDescriptors { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu DisallowedBodySlideDescriptors { get; set; }
    public NPCWeightRange WeightRange { get; set; } = new();

    //UI-related
    public ILinkCache LinkCache { get; private set; }
    public IEnumerable<Type> RacePickerFormKeys { get; set; } = typeof(IRaceGetter).AsEnumerable();

    public string TopLevelSubgroupID { get; set; }
    public RelayCommand AutoGenerateIDcommand { get; }
    public RelayCommand AutoGenerateID_Children_Command { get; }
    public RelayCommand AutoGenerateID_All_Command { get; }
    public RelayCommand AddAllowedAttribute { get; }
    public RelayCommand AddDisallowedAttribute { get; }
    public RelayCommand AddNPCKeyword { get; }
    public RelayCommand AddPath { get; }
    public RelayCommand DeleteRequiredSubgroup { get; }
    public RelayCommand DeleteExcludedSubgroup { get; }
    public bool SetExplicitReferenceNPC { get; set; }
    public HashSet<string> RequiredSubgroupIDs { get; set; } = new(); // temporary placeholder for RequiredSubgroups until all subgroups are loaded in
    public HashSet<string> ExcludedSubgroupIDs { get; set; } = new(); // temporary placeholder for ExcludedSubgroups until all subgroups are loaded in
    public string RequiredSubgroupsLabel { get; set; } = "Drag subgroups here from the tree view";
    public string ExcludedSubgroupsLabel { get; set; } = "Drag subgroups here from the tree view";
    public VM_AssetPack ParentAssetPack { get; set; }
    public VM_SubgroupPlaceHolder ParentSubgroup { get; set; }
    public ObservableCollection<VM_RaceGrouping> SubscribedRaceGroupings { get; set; }

    public void CopyInViewModelFromModel()
    {
        var model = AssociatedPlaceHolder.AssociatedModel;
        if (model == null)
        {
            return;
        }
        ID = model.ID;
        Name = model.Name;
        Enabled = model.Enabled;
        DistributionEnabled = model.DistributionEnabled;
        Notes = model.Notes;
        AllowedRaces.AddRange(model.AllowedRaces);
        DisallowedRaces.AddRange(model.DisallowedRaces);
        foreach (var x in DisallowedAttributes) { x.DisplayForceIfOption = false; }
        AllowUnique = model.AllowUnique;
        AllowNonUnique = model.AllowNonUnique;
        
        RequiredSubgroupIDs = model.RequiredSubgroups;
        foreach (var reqID in model.RequiredSubgroups)
        {
            if (ParentAssetPack.TryGetSubgroupByID(reqID, out var reqSubgroup))
            {
                RequiredSubgroups.Add(reqSubgroup);
            }
        }

        ExcludedSubgroupIDs = model.ExcludedSubgroups;
        foreach (var exID in model.ExcludedSubgroups)
        {
            if (ParentAssetPack.TryGetSubgroupByID(exID, out var exSubgroup))
            {
                RequiredSubgroups.Add(exSubgroup);
            }
        }

        VM_CollectionMemberString.CopyInObservableCollectionFromICollection(model.AddKeywords, AddKeywords);
        ProbabilityWeighting = model.ProbabilityWeighting;
        PathsMenu.CopyInFromModels(model.Paths, _filePathReplacementFactory);
        WeightRange = model.WeightRange;
        _attributeCreator.CopyInFromModels(model.AllowedAttributes, AllowedAttributes, ParentAssetPack.AttributeGroupMenu.Groups, true, null);
        _attributeCreator.CopyInFromModels(model.DisallowedAttributes, DisallowedAttributes, ParentAssetPack.AttributeGroupMenu.Groups, false, null);
        AllowedRaceGroupings.CopyInRaceGroupingsByLabel(model.AllowedRaceGroupings, ParentAssetPack.RaceGroupingEditor.RaceGroupings);
        DisallowedRaceGroupings.CopyInRaceGroupingsByLabel(model.DisallowedRaceGroupings, ParentAssetPack.RaceGroupingEditor.RaceGroupings);
        
        if (ParentAssetPack.TrackedBodyGenConfig != null)
        {
            AllowedBodyGenDescriptors.CopyInFromHashSet(model.AllowedBodyGenDescriptors);
            AllowedBodyGenDescriptors.MatchMode = model.AllowedBodyGenMatchMode;
            DisallowedBodyGenDescriptors.CopyInFromHashSet(model.DisallowedBodyGenDescriptors);
            DisallowedBodyGenDescriptors.MatchMode = model.DisallowedBodyGenMatchMode;
        }

        AllowedBodySlideDescriptors.CopyInFromHashSet(model.AllowedBodySlideDescriptors);
        AllowedBodySlideDescriptors.MatchMode = model.AllowedBodySlideMatchMode;
        DisallowedBodySlideDescriptors.CopyInFromHashSet(model.DisallowedBodySlideDescriptors);
        DisallowedBodySlideDescriptors.MatchMode = model.DisallowedBodySlideMatchMode;
    }
    public void RefreshListBoxLabel(ObservableCollection<VM_SubgroupPlaceHolder> listSource, SubgroupListBox whichBox)
    {
        string label = "";
        if (listSource.Any())
        {
            label = "";
        }
        else
        {
            label = "Drag subgroups here from the tree view";
        }

        switch(whichBox)
        {
            case SubgroupListBox.Required: RequiredSubgroupsLabel = label; break;
            case SubgroupListBox.Excluded: ExcludedSubgroupsLabel = label; break;
        }
    }
    public enum SubgroupListBox
    {
        Required,
        Excluded
    }

    public AssetPack.Subgroup DumpViewModelToModel()
    {
        var model = new AssetPack.Subgroup();

        model.ID = ID;
        model.Name = Name;
        model.Enabled = Enabled;
        model.DistributionEnabled = DistributionEnabled;
        model.Notes = Notes;
        model.AllowedRaces = AllowedRaces.ToHashSet();
        model.AllowedRaceGroupings = AllowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet();
        model.DisallowedRaces = DisallowedRaces.ToHashSet();
        model.DisallowedRaceGroupings = DisallowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet();
        model.AllowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(AllowedAttributes);
        model.DisallowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(DisallowedAttributes);
        model.AllowUnique = AllowUnique;
        model.AllowNonUnique = AllowNonUnique;
        model.RequiredSubgroups = RequiredSubgroups.Select(x => x.ID).ToHashSet();
        model.ExcludedSubgroups = ExcludedSubgroups.Select(x => x.ID).ToHashSet();
        model.AddKeywords = AddKeywords.Select(x => x.Content).ToHashSet();
        model.ProbabilityWeighting = ProbabilityWeighting;
        model.Paths = VM_FilePathReplacementMenu.DumpViewModelToModels(PathsMenu);
        model.WeightRange = WeightRange;

        model.AllowedBodyGenDescriptors = AllowedBodyGenDescriptors?.DumpToHashSet() ?? null;
        model.AllowedBodyGenMatchMode = AllowedBodyGenDescriptors?.MatchMode ?? DescriptorMatchMode.All;
        model.DisallowedBodyGenDescriptors = DisallowedBodyGenDescriptors?.DumpToHashSet() ?? null;
        model.DisallowedBodyGenMatchMode = DisallowedBodyGenDescriptors?.MatchMode ?? DescriptorMatchMode.Any;
        model.AllowedBodySlideDescriptors = AllowedBodySlideDescriptors.DumpToHashSet();
        model.AllowedBodySlideMatchMode = AllowedBodySlideDescriptors.MatchMode;
        model.DisallowedBodySlideDescriptors = DisallowedBodySlideDescriptors.DumpToHashSet();
        model.DisallowedBodySlideMatchMode = DisallowedBodySlideDescriptors.MatchMode;

        return model;
    }


    public void DragOver(IDropInfo dropInfo)
    {
        if (dropInfo.Data is VM_SubgroupPlaceHolder)
        {
            dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
            dropInfo.Effects = DragDropEffects.Move;
        }
    }

    public void Drop(IDropInfo dropInfo)
    {
        if (dropInfo.Data is VM_SubgroupPlaceHolder && dropInfo.VisualTarget is ListBox)
        {
            var listBox = (ListBox)dropInfo.VisualTarget;
            var parentContextSubgroup = listBox.DataContext as VM_Subgroup;
            if (parentContextSubgroup != null)
            {
                var draggedSubgroup = (VM_SubgroupPlaceHolder)dropInfo.Data;
                if (listBox.Name == "lbRequiredSubgroups")
                {
                    parentContextSubgroup.RequiredSubgroups.Add(draggedSubgroup);
                    parentContextSubgroup.RequiredSubgroupIDs.Add(draggedSubgroup.ID);
                    RefreshListBoxLabel(parentContextSubgroup.RequiredSubgroups, SubgroupListBox.Required);
                }
                else if (listBox.Name == "lbExcludedSubgroups")
                {
                    parentContextSubgroup.ExcludedSubgroups.Add(draggedSubgroup);
                    parentContextSubgroup.ExcludedSubgroupIDs.Add(draggedSubgroup.ID);
                    RefreshListBoxLabel(parentContextSubgroup.ExcludedSubgroups, SubgroupListBox.Excluded);
                }
            }
        }
    }  

    public void RefreshBodyGenDescriptors()
    {
        _logger.LogStartupEventStart("Refreshing BodyGen Descriptors for subgroup " + ID);
        DescriptorMatchMode allowedMode = DescriptorMatchMode.All;
        DescriptorMatchMode disallowedMode = DescriptorMatchMode.Any;

        // keep existing settings if any
        if (AllowedBodyGenDescriptors != null)
        {
            allowedMode = AllowedBodyGenDescriptors.MatchMode;
        }
        if (DisallowedBodyGenDescriptors != null)
        {
            disallowedMode = DisallowedBodyGenDescriptors.MatchMode;
        }

        if (ParentAssetPack.TrackedBodyGenConfig != null)
        {
            AllowedBodyGenDescriptors = _descriptorSelectionFactory(ParentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, ParentAssetPack, true, allowedMode);
            DisallowedBodyGenDescriptors = _descriptorSelectionFactory(ParentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, ParentAssetPack, true, disallowedMode);
        }
        _logger.LogStartupEventEnd("Refreshing BodyGen Descriptors for subgroup " + ID);
    }
}