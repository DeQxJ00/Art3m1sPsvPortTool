using Art3m1s.PsvTool.Core;

namespace Art3m1s.PsvTool.App;

public sealed partial class MainViewModel
{
    private int _selectedPsbFormat;
    public IReadOnlyList<string> PsbFormatChoices { get; } =
        ["DXT5 (BC3)", "PVRTC2 4bpp", "PVRTC2 2bpp"];
    public string PsbFormatLabel => L("PsbFormat");
    public int SelectedPsbFormat
    {
        get => _selectedPsbFormat;
        set
        {
            if (value < 0 || value >= PsbFormatChoices.Count) return;
            if (Set(ref _selectedPsbFormat, value)) SaveSettings();
        }
    }
}
