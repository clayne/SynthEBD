using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using System.ComponentModel;
using System.Collections.ObjectModel;
using Noggog;
using ReactiveUI;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins.Order;

namespace SynthEBD;

public class VM_BlockedPlugin : VM
{
    private IEnvironmentStateProvider _stateProvider;
    public delegate VM_BlockedPlugin Factory();
    public VM_BlockedPlugin(IEnvironmentStateProvider stateProvider)
    {
        _stateProvider = stateProvider;
        this.WhenAnyValue(x => x.ModKey).Subscribe(x =>
        {
            if (!ModKey.IsNull)
            {
                DispName = ModKey.FileName;
            }
        });

        _stateProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        _stateProvider.WhenAnyValue(x => x.LoadOrder)
            .Subscribe(x => LoadOrder = x)
            .DisposeWith(this);

        this.WhenAnyValue(x => x.HeadParts).Subscribe(x =>
        {
            for (int i = 0; i < HeadPartTypes.Count; i++) { HeadPartTypes[i].Block = HeadParts; }
        });
    }

    // Caption
    public string DispName { get; set; } = "New Plugin";
    public ModKey ModKey { get; set; } = new();
    public bool Assets { get; set; } = true;
    public bool Height { get; set; } = false;
    public bool BodyShape { get; set; } = false;
    public bool HeadParts { get; set; } = false;
    public ObservableCollection<VM_HeadPartBlock> HeadPartTypes { get; set; } = new()
    {
        new(HeadPart.TypeEnum.Eyebrows, false),
        new(HeadPart.TypeEnum.Eyes, false),
        new(HeadPart.TypeEnum.Face, false),
        new(HeadPart.TypeEnum.FacialHair, false),
        new(HeadPart.TypeEnum.Hair, false),
        new(HeadPart.TypeEnum.Misc, false),
        new(HeadPart.TypeEnum.Scars, false)
    };

    public ILinkCache lk { get; private set; }
    public ILoadOrderGetter LoadOrder { get; private set; }

    public static VM_BlockedPlugin GetViewModelFromModel(BlockedPlugin model, VM_BlockedPlugin.Factory factory)
    {
        VM_BlockedPlugin viewModel = factory();
        viewModel.DispName = model.ModKey.FileName;
        viewModel.ModKey = model.ModKey;
        viewModel.Assets = model.Assets;
        viewModel.Height = model.Height;
        viewModel.BodyShape = model.BodyShape;
        viewModel.HeadParts = model.HeadParts;
        foreach (var type in model.HeadPartTypes.Keys) 
        { 
            viewModel.HeadPartTypes.Where(x => x.Type == type).First().Block = model.HeadPartTypes[type]; 
        }
        return viewModel;
    }

    public static BlockedPlugin DumpViewModelToModel(VM_BlockedPlugin viewModel)
    {
        BlockedPlugin model = new BlockedPlugin();
        model.ModKey = viewModel.ModKey;
        model.Assets = viewModel.Assets;
        model.Height = viewModel.Height;
        model.BodyShape = viewModel.BodyShape;
        model.HeadParts = viewModel.HeadParts;
        foreach (var type in model.HeadPartTypes.Keys) 
        { 
            model.HeadPartTypes[type] = viewModel.HeadPartTypes.Where(x => x.Type == type).First().Block; 
        }
        return model;
    }
}