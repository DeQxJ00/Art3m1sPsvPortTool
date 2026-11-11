using System.Text;
using Art3m1s.PsvTool.Core;
using Xunit;

namespace Art3m1s.PsvTool.Core.Tests;

public sealed class ScriptCompatibilityTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("script-compatibility-").FullName;

    [Fact]
    public async Task ButtonZoomAcceptsItsStringDefaultWithoutRemovingNumericZero()
    {
        const string script = """
            local zoom = options.rollzoom or '0'
            if (zoom == 'none') or (0 == zoom) or (zoom == 'off') then zoom = '0'
            elseif zoom == 'on' or zoom == '1' then zoom = '1'
            else engine:enqueueTag({'dialog'}) end
            """;
        string path = Path.Combine(root, "random-name.iet");
        await File.WriteAllBytesAsync(path, Asb(("lua", ["script", script])));
        var processor = new ArtemisTextProcessor();

        await processor.ProcessAsync(path, 1);

        byte[] first = await File.ReadAllBytesAsync(path);
        string changed = ReadLua(first);
        Assert.Contains("or zoom == '0' then", changed);
        Assert.Contains("0 == zoom", changed);
        Assert.Contains("else engine:enqueueTag", changed);
        await processor.ProcessAsync(path, 1);
        Assert.Equal(first, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task OptionalMessageGuardUsesTheFlagAndLayerFromTheScript()
    {
        const string callback = """
            function redrawOptional(engine, args)
                local locale = engine:var('display.language')
                engine:tag{ 'chgmsg', layered = 1, id = '81.4' }
                engine:tag{'print', data = args.text}
                engine:tag{'/chgmsg'}
            end
            """;
        string path = Path.Combine(root, "elsewhere.iet");
        await File.WriteAllBytesAsync(path, Asb(
            ("var", ["system", "file_exists", "name", "optional.layout", "file", "$locale + '_layout.csv'"]),
            ("chgmsg", ["id", "81.4", "layered", "1"]),
            ("fontinit", []), ("execcsv", ["exec", "font"]), ("/chgmsg", []),
            ("if", ["estimate", "$optional.layout"]),
            ("chgmsg", ["id", "81.4", "layered", "1"]),
            ("indentmodify", []), ("rp", ["backlog", "0"]), ("/chgmsg", []), ("lydel", ["id", "81"]),
            ("calllua", ["function", "redrawOptional"]),
            ("lua", ["script", callback])));
        var processor = new ArtemisTextProcessor();

        await processor.ProcessAsync(path, 1);

        byte[] first = await File.ReadAllBytesAsync(path);
        string changed = ReadLua(first);
        Assert.Contains("if engine:var(\"optional.layout\") == \"0\" then", changed);
        Assert.Contains("engine:tag({\"lydel\", id = \"81\"})", changed);
        Assert.True(changed.IndexOf("return", StringComparison.Ordinal) < changed.IndexOf("'chgmsg'", StringComparison.Ordinal));
        await processor.ProcessAsync(path, 1);
        Assert.Equal(first, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task LeavesUnrelatedAndIncompleteScriptsByteIdentical()
    {
        string path = Path.Combine(root, "another-name.iet");
        const string script = """
            -- local a = p.rollzoom or "0"; if (a == "off") or (a == "none") or (a == 0) then a = "0" end
            local a = p.rollzoom or "0"
            if a == 0 then return false end
            function draw(e, t) e:tag{"chgmsg", id="81.4", layered="1"} end
            """;
        var processor = new ArtemisTextProcessor();
        byte[] original = Asb(("lua", ["script", script]));
        foreach (byte[] data in new[] { original, original[..^1] })
        {
            await File.WriteAllBytesAsync(path, data);
            await processor.ProcessAsync(path, 1);
            Assert.Equal(data, await File.ReadAllBytesAsync(path));
        }
    }

    private static byte[] Asb(params (string Tag, string[] Fields)[] records)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("ASB\0"u8);
        writer.Write((byte)0);
        writer.Write(records.Length);
        foreach (var (tag, fields) in records)
        {
            writer.Write(0u);
            Write(tag);
            writer.Write(27u);
            writer.Write(fields.Length / 2);
            foreach (string field in fields) Write(field);
        }
        return stream.ToArray();

        void Write(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
            writer.Write((byte)0);
        }
    }

    private static string ReadLua(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));
        reader.BaseStream.Position = 5;
        int count = reader.ReadInt32();
        string? script = null;
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(0u, reader.ReadUInt32());
            string tag = Read();
            Assert.Equal(27u, reader.ReadUInt32());
            int fields = reader.ReadInt32();
            for (int j = 0; j < fields; j++)
            {
                string key = Read(), value = Read();
                if (tag == "lua" && key == "script") script = value;
            }
        }
        Assert.Equal(bytes.Length, reader.BaseStream.Position);
        return Assert.IsType<string>(script);

        string Read()
        {
            string value = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
            Assert.Equal(0, reader.ReadByte());
            return value;
        }
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
