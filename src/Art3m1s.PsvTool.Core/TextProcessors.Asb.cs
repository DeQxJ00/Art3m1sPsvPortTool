using System.Text;

namespace Art3m1s.PsvTool.Core;

public sealed partial class ArtemisTextProcessor
{
    private sealed record AsbField(string Value, int Start, int End);
    private sealed record AsbRecord(string Tag, IReadOnlyList<(string Key, AsbField Field)> Fields)
    {
        public string? Value(string key) => Fields.FirstOrDefault(pair => pair.Key == key).Field?.Value;
    }

    private static IReadOnlyList<AsbRecord>? ReadAsbRecords(byte[] bytes)
    {
        if (bytes.Length < 9 || !bytes.AsSpan().StartsWith("ASB\0"u8)) return null;
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        stream.Position = 5;
        try
        {
            uint count = reader.ReadUInt32();
            if (count > (stream.Length - stream.Position) / 9) return null;
            List<AsbRecord> records = [];
            for (uint i = 0; i < count; i++)
            {
                uint kind = reader.ReadUInt32();
                string tag = ReadField().Value;
                if (kind == 1)
                {
                    records.Add(new("", [])); // Labels interrupt adjacent command patterns.
                    continue;
                }
                if (kind != 0) return null;
                reader.ReadUInt32(); // Original source line.
                uint fields = reader.ReadUInt32();
                if (fields > (stream.Length - stream.Position) / 10) return null;
                List<(string, AsbField)> parameters = [];
                for (uint j = 0; j < fields; j++)
                    parameters.Add((ReadField().Value, ReadField()));
                records.Add(new(tag, parameters));
            }
            return records;
        }
        catch (Exception error) when (error is EndOfStreamException or InvalidDataException)
        {
            return null;
        }

        AsbField ReadField()
        {
            int start = (int)stream.Position;
            uint size = reader.ReadUInt32();
            if (size >= stream.Length - stream.Position) throw new InvalidDataException();
            stream.Position += size;
            if (reader.ReadByte() != 0) throw new InvalidDataException();
            // Latin-1 gives a one-to-one mapping for existing script bytes.
            return new(Encoding.Latin1.GetString(bytes, start + sizeof(uint), (int)size), start, (int)stream.Position);
        }
    }

    private static byte[] RewriteAsb(byte[] original, List<(AsbField Field, string Value)> edits)
    {
        if (edits.Count == 0) return original;
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        int position = 0;
        foreach (var (field, value) in edits)
        {
            writer.Write(original.AsSpan(position, field.Start - position));
            byte[] changed = Encoding.Latin1.GetBytes(value);
            writer.Write(changed.Length);
            writer.Write(changed);
            writer.Write((byte)0);
            position = field.End;
        }
        writer.Write(original.AsSpan(position));
        return output.ToArray();
    }
}
