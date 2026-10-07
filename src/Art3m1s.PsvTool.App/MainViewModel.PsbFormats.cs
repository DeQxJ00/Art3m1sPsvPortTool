using Art3m1s.PsvTool.Core;
using System.ComponentModel;

namespace Art3m1s.PsvTool.App;

public sealed partial class MainViewModel
{
    private int _selectedPsbFormat;
    private int _selectedPsbDxt5Layout;
    public IReadOnlyList<string> PsbFormatChoices { get; } =
        ["DXT5 (BC3)", "PVRTC2 4bpp", "PVRTC2 2bpp"];
    public string PsbFormatLabel => L("PsbFormat");
    public string PsbDxt5LayoutLabel => L("PsbDxt5Layout");
    public string PsbDxt5LayoutHelp => L("PsbDxt5LayoutHelp");
    private IReadOnlyList<PsbLayoutChoice>? _psbLayoutChoices;
    public IReadOnlyList<PsbLayoutChoice> PsbDxt5LayoutChoices => _psbLayoutChoices ??=
        [new(_localizer, "PsbDxt5Swizzled"), new(_localizer, "PsbDxt5Linear")];
    public bool CanSelectPsbDxt5Layout => ConvertEmotePsbTexturesToDxt5 && SelectedPsbFormat == 0;
    public int SelectedPsbDxt5Layout
    {
        get => _selectedPsbDxt5Layout;
        set
        {
            if (value is < 0 or > 1) return;
            if (Set(ref _selectedPsbDxt5Layout, value)) SaveSettings();
        }
    }
    public int SelectedPsbFormat
    {
        get => _selectedPsbFormat;
        set
        {
            if (value < 0 || value >= PsbFormatChoices.Count) return;
            if (Set(ref _selectedPsbFormat, value))
            {
                OnPropertyChanged(nameof(CanSelectPsbDxt5Layout)); SaveSettings();
            }
        }
    }
}

// Stable item identities keep the selection when labels change language.
public sealed class PsbLayoutChoice(ILocalizer localizer, string key) : INotifyPropertyChanged
{
    public string Label => localizer[key];
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new(nameof(Label)));
}
