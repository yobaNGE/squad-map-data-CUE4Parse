using Squad_pipeline_map_data_CUE4Parse.Application;

namespace Squad_pipeline_map_data_CUE4Parse.Presentation;

public sealed class LayerRowViewModel : ObservableObject
{
    private readonly Action _selectionChanged;
    private bool _isSelected;

    public LayerRowViewModel(LayerDescriptor descriptor, Action selectionChanged)
    {
        Descriptor = descriptor;
        _selectionChanged = selectionChanged;
    }

    public LayerDescriptor Descriptor { get; }
    public string Name => Descriptor.Name;
    public string MapId => Descriptor.MapId;
    public string GameMode => Descriptor.GameMode;
    public string Version => Descriptor.Version;
    public string SourceName => Descriptor.Source.DisplayName;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value)) return;
            _selectionChanged();
        }
    }
}
