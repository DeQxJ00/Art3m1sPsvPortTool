using Art3m1s.PsvTool.Core;

namespace Art3m1s.PsvTool.App;

internal sealed record AutomaticResolution(IReadOnlyList<IniResolution> Choices, int SelectedIndex, int? Width, int? Height)
{
    public static AutomaticResolution FromScan(ScanResult scan)
    {
        IniResolution[] choices = scan.Resolutions.Where(item => item.Width > 0 && item.Height > 0).ToArray();
        int selected = choices.Length == 0 ? -1 : Array.FindIndex(choices, item =>
            item.Source == choices[0].Source && item.Section.Equals("WINDOWS", StringComparison.OrdinalIgnoreCase));
        if (selected < 0 && choices.Length > 0) selected = 0;
        return new(choices, selected, selected < 0 ? scan.Width : choices[selected].Width,
            selected < 0 ? scan.Height : choices[selected].Height);
    }

    public static double FitRatio(int width, int height,
        int fitWidth = ConversionDefaults.FitWidth, int fitHeight = ConversionDefaults.FitHeight) =>
        Math.Min(1d, Math.Min((double)fitWidth / width, (double)fitHeight / height));
}
