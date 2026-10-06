using System.Buffers.Binary;
using System.Text;
using Art3m1s.PsvTool.Core;
using Xunit;

namespace Art3m1s.PsvTool.Core.Tests;

public sealed class PsbTextureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "psb-textures-" + Guid.NewGuid().ToString("N"));
    public PsbTextureTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ScanReadsRectangularAtlasesFromLooseAndEncryptedPfsWithoutChangingSources()
    {
        byte[] original = PsbFixture.Create((128, 64), (32, 32));
        string source = Path.Combine(_root, "loose.PSB");
        await File.WriteAllBytesAsync(source, original);
        await new PfsCodec().PackPf8Async(new('8',
            [new(Encoding.UTF8.GetBytes("image/fg/hero.psb"), "image/fg/hero.psb", 0, (uint)original.Length, source)]),
            Path.Combine(_root, "root.pfs.010"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "broken.psb"), [1, 2, 3]);

        var scanned = await new PsbTextureScanner().ScanAsync(_root);

        Assert.Equal(3, scanned.Count);
        var archived = Assert.Single(scanned, x => x.Archive is not null);
        Assert.Equal("root.pfs.010", archived.Archive);
        Assert.Equal("image/fg/hero.psb", archived.Path);
        Assert.Equal(128, archived.Width); Assert.Equal(64, archived.Height);
        Assert.Equal(new[] { (128, 64), (32, 32) }, archived.Inspection!.Atlases.Select(x => (x.Width, x.Height)));
        Assert.NotNull(Assert.Single(scanned, x => x.Path == "broken.psb").Error);
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
    }

    [Fact]
    public async Task ConversionUsesExactWidthHeightRuleForWholePsbAndDefaultHalfForOtherSizes()
    {
        string input = Path.Combine(_root, "input"), output = Path.Combine(_root, "output");
        Directory.CreateDirectory(input);
        byte[] multi = PsbFixture.Create((128, 64), (32, 32));
        string source = Path.Combine(_root, "multi.psb");
        await File.WriteAllBytesAsync(source, multi);
        var codec = new PfsCodec();
        await codec.PackPf8Async(new('8', [new(Encoding.UTF8.GetBytes("multi.psb"), "multi.psb", 0, (uint)multi.Length, source)]),
            Path.Combine(input, "root.pfs"));
        await File.WriteAllBytesAsync(Path.Combine(input, "other.psb"), PsbFixture.Create((64, 128)));
        await new ConversionService().ConvertAsync(new(input, output, Ratio: .75,
            Categories: AssetCategories.Animation, ConvertEmotePsbTexturesToDxt5: false,
            PsbTextures: new([new(128, 64, .25)], Enabled: true)));

        var unpacked = await codec.ExtractAsync(Path.Combine(output, "root.pfs"), Path.Combine(_root, "unpack"));
        var converted = await PsbTextureScanner.InspectFileAsync(Assert.Single(unpacked.Entries).ExtractedPath);
        Assert.Equal(new[] { (32, 16), (8, 8) }, converted.Atlases.Select(x => (x.Width, x.Height)));
        var other = await PsbTextureScanner.InspectFileAsync(Path.Combine(output, "other.psb"));
        Assert.Equal(32, other.Width); Assert.Equal(64, other.Height);
        Assert.Equal(multi, await File.ReadAllBytesAsync(source));
    }

    [Fact]
    public async Task AnimationUncheckedPreservesDimensionsWhileBc3StillConverts()
    {
        string input = Path.Combine(_root, "input"); Directory.CreateDirectory(input);
        await File.WriteAllBytesAsync(Path.Combine(input, "hero.psb"), PsbFixture.Create((64, 32)));
        await new PfsCodec().PackPf8Async(new('8', []), Path.Combine(input, "root.pfs"));
        string output = Path.Combine(_root, "output");
        await new ConversionService().ConvertAsync(new(input, output, Categories: AssetCategories.None,
            PsbTextures: new([new(64, 32, .25)], Enabled: true)));
        var result = await PsbTextureScanner.InspectFileAsync(Path.Combine(output, "hero.psb"));
        Assert.Equal(64, result.Width); Assert.Equal(32, result.Height);
        Assert.Equal("DXT5", Assert.Single(result.Atlases).Format);
    }

    [Fact]
    public void InvalidRulesAreRejectedAndUnconfiguredRectanglesDefaultToHalf()
    {
        Assert.Equal(.5, new PsbTextureOptions().RatioFor(4096, 2048));
        Assert.Equal(.25, new PsbTextureOptions([new(4096, 2048, .25)]).RatioFor(4096, 2048));
        Assert.Equal(.5, new PsbTextureOptions([new(4096, 2048, .25)]).RatioFor(2048, 4096));
        foreach (double ratio in new[] { double.NaN, double.PositiveInfinity, 0, -1, 1.1 })
            Assert.Throws<ArgumentException>(() => new PsbTextureOptions([new(4096, 2048, ratio)]).Validate());
        Assert.Throws<ArgumentException>(() => new PsbTextureOptions([new(64, 32), new(64, 32)]).Validate());
    }

    public void Dispose() => Directory.Delete(_root, true);
}

// Small original motion PSBs with real resource tables, for scanner/conversion integration tests.
internal static class PsbFixture
{
    private sealed record Resource(int Index);
    public static byte[] Create(params (int Width, int Height)[] sizes)
    {
        Dictionary<string, object> sources = [];
        List<byte[]> pixels = [];
        for (int i = 0; i < sizes.Length; i++)
        {
            var (width, height) = sizes[i];
            byte[] bgra = new byte[width * height * 4];
            for (int p = 0; p < bgra.Length; p += 4) { bgra[p] = 40; bgra[p + 1] = 90; bgra[p + 2] = 180; bgra[p + 3] = 255; }
            pixels.Add(bgra);
            sources["atlas" + i] = new Dictionary<string, object>
            {
                ["texture"] = new Dictionary<string, object>
                {
                    ["width"] = width,
                    ["height"] = height,
                    ["truncated_width"] = width,
                    ["truncated_height"] = height,
                    ["type"] = "RGBA8",
                    ["pixel"] = new Resource(i)
                },
                ["icon"] = new Dictionary<string, object>
                {
                    ["piece" + i] = new Dictionary<string, object>
                    {
                        ["left"] = 0,
                        ["top"] = 0,
                        ["width"] = width / 2,
                        ["height"] = height / 2,
                        ["originX"] = width / 4,
                        ["originY"] = height / 4,
                        ["attr"] = 0
                    }
                }
            };
        }
        Dictionary<string, object> root = new()
        {
            ["id"] = "motion",
            ["source"] = sources,
            ["metadata"] = new Dictionary<string, object> { ["base"] = new Dictionary<string, object> { ["chara"] = "hero", ["motion"] = "idle" } },
            ["object"] = new Dictionary<string, object>
            {
                ["hero"] = new Dictionary<string, object>
                {
                    ["motion"] = new Dictionary<string, object>
                    {
                        ["idle"] = new Dictionary<string, object>
                        {
                            ["lastTime"] = 60,
                            ["loopTime"] = 0,
                            ["layer"] = new object[] {
              new Dictionary<string, object> { ["label"] = "part", ["type"] = 0, ["frameList"] = new object[] {
                new Dictionary<string, object> { ["time"] = 0, ["type"] = 1, ["content"] = new Dictionary<string, object>
                  { ["src"] = "atlas0", ["icon"] = "piece0", ["coord"] = new object[] { 100, 40, 0 }, ["opa"] = 255,
                    ["bounds"] = new Dictionary<string, object> { ["left"] = -10, ["top"] = -20, ["right"] = 50, ["bottom"] = 80 } } } } } }
                        }
                    }
                }
            },
            ["screenSize"] = new Dictionary<string, object> { ["width"] = 1280, ["height"] = 720 }
        };
        List<string> names = [], strings = [];
        void Collect(object value)
        {
            if (value is Dictionary<string, object> map)
                foreach (var (key, child) in map) { if (!names.Contains(key)) names.Add(key); Collect(child); }
            else if (value is string s && !strings.Contains(s)) strings.Add(s);
            else if (value is object[] list) foreach (object child in list) Collect(child);
        }
        Collect(root);
        Dictionary<int, uint> charset = new() { [0] = 256 }, tree = [];
        Dictionary<(int Parent, byte Char), int> edges = [];
        List<uint> nameIndexes = [];
        int nextBase = 512;
        foreach (string name in names)
        {
            int parent = 0;
            foreach (byte b in Encoding.UTF8.GetBytes(name).Append((byte)0))
            {
                if (!edges.TryGetValue((parent, b), out int node))
                {
                    node = (int)charset[parent] + b; edges[(parent, b)] = node;
                    tree[node] = (uint)parent; charset[node] = (uint)nextBase; nextBase += 256;
                }
                parent = node;
            }
            nameIndexes.Add((uint)parent);
        }
        uint[] charArray = new uint[charset.Keys.Max() + 1], treeArray = new uint[tree.Keys.Max() + 1];
        foreach (var (key, value) in charset) charArray[key] = value;
        foreach (var (key, value) in tree) treeArray[key] = value;
        byte[] Value(object value)
        {
            using MemoryStream stream = new();
            switch (value)
            {
                case Dictionary<string, object> map:
                    stream.WriteByte(0x21); stream.Write(Array(map.Keys.Select(x => (uint)names.IndexOf(x))));
                    byte[][] children = map.Values.Select(Value).ToArray();
                    uint relative = 0; List<uint> offsets = [];
                    foreach (byte[] child in children) { offsets.Add(relative); relative += (uint)child.Length; }
                    stream.Write(Array(offsets)); foreach (byte[] child in children) stream.Write(child); break;
                case string s: stream.WriteByte(0x18); Write32(stream, (uint)strings.IndexOf(s)); break;
                case Resource r: stream.WriteByte(0x1c); Write32(stream, (uint)r.Index); break;
                case object[] list:
                    stream.WriteByte(0x20);
                    byte[][] items = list.Select(Value).ToArray();
                    uint itemOffset = 0; List<uint> itemOffsets = [];
                    foreach (byte[] item in items) { itemOffsets.Add(itemOffset); itemOffset += (uint)item.Length; }
                    stream.Write(Array(itemOffsets)); foreach (byte[] item in items) stream.Write(item); break;
                case int n: stream.WriteByte(0x08); Write32(stream, (uint)n); break;
                default: throw new InvalidOperationException();
            }
            return stream.ToArray();
        }
        using MemoryStream output = new(); output.Write(new byte[44]);
        byte[] header = new byte[44]; "PSB\0"u8.CopyTo(header); BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), 3);
        void Address(int field) => BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(field), (uint)output.Position);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), 44);
        Address(12); output.Write(Array(charArray)); output.Write(Array(treeArray)); output.Write(Array(nameIndexes));
        Address(16); uint stringOffset = 0; List<uint> stringOffsets = [];
        foreach (string s in strings) { stringOffsets.Add(stringOffset); stringOffset += (uint)Encoding.UTF8.GetByteCount(s) + 1; }
        output.Write(Array(stringOffsets)); Address(20);
        foreach (string s in strings) { output.Write(Encoding.UTF8.GetBytes(s)); output.WriteByte(0); }
        Address(36); output.Write(Value(root));
        uint pixelOffset = 0; List<uint> pixelOffsets = [];
        foreach (byte[] p in pixels) { pixelOffsets.Add(pixelOffset); pixelOffset += (uint)p.Length; }
        Address(24); output.Write(Array(pixelOffsets)); Address(28); output.Write(Array(pixels.Select(p => (uint)p.Length)));
        Address(32); foreach (byte[] p in pixels) output.Write(p);
        uint a = 1, bsum = 0; foreach (byte b in header.AsSpan(8, 32)) { a = (a + b) % 65521; bsum = (bsum + a) % 65521; }
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40), (bsum << 16) | a);
        byte[] result = output.ToArray(); header.CopyTo(result, 0); return result;
    }
    private static byte[] Array(IEnumerable<uint> source)
    {
        uint[] values = source.ToArray(); using MemoryStream stream = new();
        stream.WriteByte(0x10); Write32(stream, (uint)values.Length); stream.WriteByte(0x10);
        foreach (uint value in values) Write32(stream, value);
        return stream.ToArray();
    }
    private static void Write32(Stream stream, uint value)
    { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); stream.Write(bytes); }
}
