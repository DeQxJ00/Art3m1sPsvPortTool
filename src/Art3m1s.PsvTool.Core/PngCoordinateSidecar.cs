using System.Globalization;
using System.Text;

namespace Art3m1s.PsvTool.Core;

internal sealed class PngCoordinates
{
    private readonly record struct Row(string Key, string Value, int Start, int Count);
    private readonly List<Row> _rows = [];

    public static PngCoordinates? Read(PngDocument png, string path)
    {
        if (!Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)
            || png.IsAnimated || !png.Trailer.IsEmpty)
            return null;

        var coordinates = new PngCoordinates();
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in png.Chunks)
        {
            if (!PngDocument.RequiresTextureMetadataPreservation(chunk))
                continue;
            if (chunk.Type != "tEXt")
                return null;

            var data = chunk.Data.Span;
            int separator = data.IndexOf((byte)0);
            if (separator <= 0 || data.IndexOfAnyExceptInRange((byte)0, (byte)127) >= 0)
                return null;
            string key = Encoding.ASCII.GetString(data[..separator]);
            string value = Encoding.ASCII.GetString(data[(separator + 1)..]);
            if (!keys.Add(key) || ParseRow(key, value) is not { } row)
                return null;
            coordinates._rows.Add(row);
        }
        return coordinates._rows.Count > 0 && coordinates.Fits(png.Width, png.Height, 1) ? coordinates : null;
    }

    private static Row? ParseRow(string key, string value)
    {
        string[] fields = value.Split(',');
        string kind = key.ToLowerInvariant();
        int start = 0;
        int count;
        if (kind == "comment")
        {
            if (fields[0] != "pos" || fields.Length is not (3 or 5 or 6 or 7))
                return null;
            start = 1;
            count = fields.Length == 3 ? 2 : 4;
            if (fields.Length >= 6 && (!TryNumber(fields[5], out int frames) || frames <= 0))
                return null;
            if (fields.Length == 7 && fields[6].Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-')))
                return null;
        }
        else
        {
            count = kind switch
            {
                "position" or "offset" or "anchor" or "origin" => 2,
                "crop" or "clip" or "rect" => 4,
                "x" or "y" or "left" or "top" or "width" or "height" => 1,
                _ => 0
            };
            if (count == 0 || fields.Length != count)
                return null;
        }
        for (int i = start; i < start + count; i++)
            if (!TryNumber(fields[i], out _))
                return null;
        return new(key, value, start, count);
    }

    // The original Lua computes frame i's vertical origin as i * (frameHeight + 1).
    // Export only when its crops still match the resized sheet at the chosen ratio.
    internal bool Fits(double width, double height, double ratio)
    {
        foreach (var row in _rows)
        {
            if (row.Start != 1 || row.Count != 4)
                continue;
            string[] fields = row.Value.Split(',');
            int frameWidth = Number(fields[3]), frameHeight = Number(fields[4]);
            int frames = fields.Length >= 6 ? Number(fields[5]) : 1;
            int scaledWidth = (int)(frameWidth * ratio), scaledHeight = (int)(frameHeight * ratio);
            long lastSourceTop = (frames - 1L) * (frameHeight + 1L);
            long lastTargetTop = (frames - 1L) * (scaledHeight + 1L);
            if (scaledWidth <= 0 || scaledHeight <= 0 || scaledWidth > Math.Floor(width * ratio)
                || lastTargetTop + scaledHeight > Math.Floor(height * ratio)
                || lastTargetTop != Math.Floor(lastSourceTop * ratio))
                return false;
        }
        return true;
    }

    public string ToCsv(double ratio)
    {
        var csv = new StringBuilder("art3m1s_png_coordinates_v1\n");
        foreach (var row in _rows)
            csv.Append(row.Key).Append(',').Append(Scale(row, ratio)).Append('\n');
        return csv.ToString();
    }

    // Keep frame count and animation mode intact even if the output stays a PNG.
    internal static string? ScalePositionComment(string value, double ratio) =>
        ParseRow("comment", value) is { } row ? Scale(row, ratio) : null;

    private static string Scale(Row row, double ratio)
    {
        if (ratio == 1)
            return row.Value;
        string[] fields = row.Value.Split(',');
        for (int i = row.Start; i < row.Start + row.Count; i++)
            fields[i] = ((int)(Number(fields[i]) * ratio)).ToString(CultureInfo.InvariantCulture);
        return string.Join(',', fields);
    }

    private static bool TryNumber(string value, out int number) =>
        int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number)
        && value.All(c => char.IsAsciiDigit(c) || c is '+' or '-');

    private static int Number(string value) => int.Parse(value, CultureInfo.InvariantCulture);
}

public sealed partial class ArtemisTextProcessor
{
    private readonly record struct PngReaderEdit(int Start, int End, string Replacement);

    internal static bool HasCompatiblePngCoordinateReaders(string script, out bool found)
    {
        var edits = FindPngCoordinateReaders(script);
        found = edits is { Count: > 0 };
        return edits != null;
    }

    // A null result means the script uses a calling convention we cannot adapt.
    private static List<PngReaderEdit>? FindPngCoordinateReaders(string script)
    {
        List<PngReaderEdit> edits = [];
        if (!script.Contains("loadPngComments", StringComparison.Ordinal))
            return edits;
        var tokens = LexScript(script);
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'i' || At(i) != "loadPngComments")
                continue;
            if (At(i - 2) != "e" || At(i - 1) != ":" || At(i + 1) != "("
                || At(i - 3) is "." or ":")
                return null;
            edits.Add(new(tokens[i - 2].Start, tokens[i + 1].End, "art3m1s_png_comments(e, "));

            // Expand the common original existence guard on the same argument.
            if (At(i - 3) != "=" || At(i - 5) != "local" || At(i - 6) != "then"
                || At(i - 7) != ")" || At(i - 9) != "(" || At(i - 8) != At(i + 2)
                || At(i + 3) != ")")
                continue;
            int guardStart = At(i - 10) switch
            {
                "isFile" when At(i - 11) == "if" => i - 10,
                "isFileExists" when At(i - 11) == ":" && At(i - 12) == "e" && At(i - 13) == "if" => i - 12,
                _ => -1
            };
            if (guardStart >= 0 && tokens[i + 2].Kind == 'i')
            {
                int guardEnd = tokens[i - 7].End;
                edits.Add(new(guardEnd, guardEnd, $" or art3m1s_png_csv_comments(e, {At(i + 2)})"));
            }
        }
        return edits;

        string? At(int index) => index >= 0 && index < tokens.Count ? tokens[index].Text : null;
    }

    internal static async Task AddPngCoordinateReadersAsync(string path, CancellationToken token)
    {
        var file = EncodedText.Decode(await File.ReadAllBytesAsync(path, token));
        var edits = FindPngCoordinateReaders(file.Text);
        if (edits is not { Count: > 0 })
            return;
        const string reader = """
            local function art3m1s_png_csv_path(engine, path)
                local stem = type(path) == "string" and (path:match("^(.*)%.[pP][nN][gG]$") or path:match("^(.*)%.[dD][dD][sS]$"))
                if stem and engine:isFileExists(stem .. ".dds") then return stem .. ".csv" end
            end
            local function art3m1s_png_csv_comments(engine, path)
                local csv = art3m1s_png_csv_path(engine, path)
                local data = csv and engine:isFileExists(csv) and engine:file(csv)
                local rows = type(data) == "string" and data:match("^art3m1s_png_coordinates_v1\n(.+\n)$")
                if rows then
                    local comments = {}
                    for line in rows:gmatch("([^\n]*)\n") do
                        local key, value = line:match("^([%a_][%w_]*),(.+)$")
                        if not key or comments[key] then return nil end
                        comments[key] = value
                    end
                    return comments
                end
            end
            local function art3m1s_png_comments(engine, path)
                if type(path) == "string" and path:lower():sub(-4) == ".png" and engine:isFileExists(path) then
                    return engine:loadPngComments(path)
                end
                return art3m1s_png_csv_comments(engine, path) or engine:loadPngComments(path)
            end
            """;
        var output = new StringBuilder(file.Text);
        foreach (var edit in edits.OrderByDescending(edit => edit.Start))
            output.Remove(edit.Start, edit.End - edit.Start).Insert(edit.Start, edit.Replacement);
        string newline = file.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        output.Insert(0, reader.ReplaceLineEndings(newline) + newline);
        await File.WriteAllBytesAsync(path, file.Encode(output.ToString()), token);
    }
}
