using System.Buffers.Binary;
using System.Text;

namespace ScoreStudio.Export;

/// <summary>
/// Just enough of an OpenType or TrueType font (or the first font of a .ttc collection) to put it in a PDF
/// (docs/PDF_EXPORT.md): glyph numbers for characters, advance widths, metrics, whether embedding is allowed, and, for a
/// TrueType font, a copy that keeps only the glyphs used.
/// </summary>
public sealed class OpenTypeFont
{
    private readonly byte[] _data;
    private readonly Dictionary<string, (int Offset, int Length)> _tables = new(StringComparer.Ordinal);
    private readonly Dictionary<int, int> _cmap = new();
    private readonly ushort[] _advances;

    public string Name { get; }
    public int UnitsPerEm { get; }
    public int NumGlyphs { get; }
    public (int XMin, int YMin, int XMax, int YMax) BoundingBox { get; }
    public int Ascent { get; }
    public int Descent { get; }

    /// <summary>A CFF-outline font (.otf); otherwise TrueType outlines.</summary>
    public bool IsCff => _tables.ContainsKey("CFF ");

    /// <summary>False when the font's licence (OS/2 fsType) forbids embedding it.</summary>
    public bool Embeddable { get; }

    /// <summary>The whole font file as read (for a CFF font, which is embedded whole).</summary>
    public byte[] Data => _data;

    private OpenTypeFont(byte[] data, int offset, string fallbackName)
    {
        _data = data;
        var count = U16(offset + 4);
        for (var i = 0; i < count; i++)
        {
            var record = offset + 12 + i * 16;
            _tables[Encoding.ASCII.GetString(data, record, 4)] = ((int)U32(record + 8), (int)U32(record + 12));
        }

        var head = Table("head");
        UnitsPerEm = U16(head + 18);
        BoundingBox = (S16(head + 36), S16(head + 38), S16(head + 40), S16(head + 42));
        var hhea = Table("hhea");
        (Ascent, Descent) = (S16(hhea + 4), S16(hhea + 6));
        var metrics = U16(hhea + 34);
        NumGlyphs = U16(Table("maxp") + 4);

        var hmtx = Table("hmtx");
        _advances = new ushort[NumGlyphs];
        for (var g = 0; g < NumGlyphs; g++) _advances[g] = U16(hmtx + 4 * Math.Min(g, metrics - 1));

        Embeddable = !_tables.TryGetValue("OS/2", out var os2) || (U16(os2.Offset + 8) & 0x000F) != 0x0002;
        ReadCmap();
        Name = ReadName() ?? fallbackName;
    }

    /// <summary>Reads a .ttf or .otf file, or the first font of a .ttc collection.</summary>
    public static OpenTypeFont Load(string path)
    {
        var data = File.ReadAllBytes(path);
        var offset = Encoding.ASCII.GetString(data, 0, 4) == "ttcf" ? (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12)) : 0;
        return new OpenTypeFont(data, offset, Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>The glyph for a character (a Unicode code point), or 0 when the font has none.</summary>
    public int GlyphOf(int codePoint) => _cmap.TryGetValue(codePoint, out var glyph) ? glyph : 0;

    /// <summary>Advance width of a glyph in font units.</summary>
    public int Advance(int glyph) => glyph >= 0 && glyph < _advances.Length ? _advances[glyph] : 0;

    private int Table(string tag) => _tables.TryGetValue(tag, out var t) ? t.Offset : throw new InvalidDataException($"글꼴에 {tag} 표가 없습니다.");

    private ushort U16(int at) => BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(at));
    private short S16(int at) => BinaryPrimitives.ReadInt16BigEndian(_data.AsSpan(at));
    private uint U32(int at) => BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(at));

    /// <summary>The Unicode map: format 12 (full Unicode) when there is one, else format 4 (the basic plane).</summary>
    private void ReadCmap()
    {
        if (!_tables.TryGetValue("cmap", out var table)) return; // a subset made for a PDF has none
        var cmap = table.Offset;
        var count = U16(cmap + 2);
        int? format4 = null, format12 = null;
        for (var i = 0; i < count; i++)
        {
            var (platform, encoding) = (U16(cmap + 4 + i * 8), U16(cmap + 6 + i * 8));
            var sub = cmap + (int)U32(cmap + 8 + i * 8);
            var format = U16(sub);
            var unicode = platform == 0 || (platform == 3 && encoding is 1 or 10);
            if (!unicode) continue;
            if (format == 12) format12 ??= sub;
            if (format == 4) format4 ??= sub;
        }

        if (format12 is { } f12)
        {
            var groups = U32(f12 + 12);
            for (var g = 0; g < groups; g++)
            {
                var at = f12 + 16 + g * 12;
                var (start, end, glyph) = (U32(at), U32(at + 4), U32(at + 8));
                for (var c = start; c <= end && c - start < 0x10000; c++) _cmap.TryAdd((int)c, (int)(glyph + c - start));
            }
        }
        else if (format4 is { } f4)
        {
            var segments = U16(f4 + 6) / 2;
            var ends = f4 + 14;
            var starts = ends + segments * 2 + 2;
            var deltas = starts + segments * 2;
            var offsets = deltas + segments * 2;
            for (var s = 0; s < segments; s++)
            {
                var (start, end, delta, rangeOffset) = (U16(starts + s * 2), U16(ends + s * 2), S16(deltas + s * 2), U16(offsets + s * 2));
                for (var c = start; c <= end && c != 0xFFFF; c++)
                {
                    int glyph;
                    if (rangeOffset == 0) glyph = (c + delta) & 0xFFFF;
                    else
                    {
                        var at = offsets + s * 2 + rangeOffset + (c - start) * 2;
                        glyph = U16(at);
                        if (glyph != 0) glyph = (glyph + delta) & 0xFFFF;
                    }

                    if (glyph != 0) _cmap.TryAdd(c, glyph);
                }
            }
        }
    }

    /// <summary>The PostScript name (name ID 6), or null.</summary>
    private string? ReadName()
    {
        if (!_tables.TryGetValue("name", out var table)) return null;
        var (count, strings) = (U16(table.Offset + 2), table.Offset + U16(table.Offset + 4));
        for (var i = 0; i < count; i++)
        {
            var record = table.Offset + 6 + i * 12;
            if (U16(record + 6) != 6) continue;
            var (platform, length, offset) = (U16(record), U16(record + 8), U16(record + 10));
            var bytes = _data.AsSpan(strings + offset, length);
            var name = platform is 0 or 3 ? Encoding.BigEndianUnicode.GetString(bytes) : Encoding.Latin1.GetString(bytes);
            return new string(name.Where(c => c > ' ' && c < 127 && c is not ('[' or ']' or '(' or ')' or '{' or '}' or '<' or '>' or '/' or '%')).ToArray());
        }

        return null;
    }

    /// <summary>
    /// A TrueType font file with only the given glyphs' outlines (and the glyphs they are built from); every other
    /// glyph is left empty, so glyph numbers stay the same. Only the tables a PDF needs are kept.
    /// </summary>
    public byte[] SubsetTrueType(IEnumerable<int> glyphs)
    {
        if (IsCff) throw new InvalidOperationException("CFF 글꼴은 줄이지 않고 통째로 넣습니다.");
        var head = Table("head");
        var longLoca = S16(head + 50) == 1;
        var loca = Table("loca");
        var glyf = Table("glyf");
        int Location(int g) => longLoca ? (int)U32(loca + g * 4) : U16(loca + g * 2) * 2;

        // The glyphs used and the components of composite glyphs, recursively; glyph 0 (.notdef) always.
        var keep = new HashSet<int> { 0 };
        var queue = new Queue<int>(glyphs.Where(g => g > 0 && g < NumGlyphs));
        while (queue.Count > 0)
        {
            var g = queue.Dequeue();
            if (!keep.Add(g)) continue;
            var (start, end) = (Location(g), Location(g + 1));
            if (end - start < 10 || S16(glyf + start) >= 0) continue;
            var at = glyf + start + 10;
            while (true)
            {
                var flags = U16(at);
                queue.Enqueue(U16(at + 2));
                at += 4 + ((flags & 0x0001) != 0 ? 4 : 2);
                at += (flags & 0x0008) != 0 ? 2 : (flags & 0x0040) != 0 ? 4 : (flags & 0x0080) != 0 ? 8 : 0;
                if ((flags & 0x0020) == 0) break;
            }
        }

        var newGlyf = new MemoryStream();
        var newLoca = new byte[(NumGlyphs + 1) * 4];
        for (var g = 0; g < NumGlyphs; g++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(g * 4), (uint)newGlyf.Length);
            if (!keep.Contains(g)) continue;
            var (start, end) = (Location(g), Location(g + 1));
            newGlyf.Write(_data, glyf + start, end - start);
            while (newGlyf.Length % 4 != 0) newGlyf.WriteByte(0);
        }

        BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(NumGlyphs * 4), (uint)newGlyf.Length);

        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var tag in new[] { "head", "hhea", "hmtx", "maxp", "cvt ", "fpgm", "prep" })
        {
            if (_tables.TryGetValue(tag, out var t)) tables[tag] = _data.AsSpan(t.Offset, t.Length).ToArray();
        }

        BinaryPrimitives.WriteInt16BigEndian(tables["head"].AsSpan(50), 1); // long loca offsets
        BinaryPrimitives.WriteUInt32BigEndian(tables["head"].AsSpan(8), 0);  // checksum adjustment, set below
        tables["loca"] = newLoca;
        tables["glyf"] = newGlyf.ToArray();
        return WriteFont(tables);
    }

    /// <summary>An sfnt file of the given tables, with their checksums and the head checksum adjustment.</summary>
    private static byte[] WriteFont(SortedDictionary<string, byte[]> tables)
    {
        static uint Checksum(byte[] bytes)
        {
            uint sum = 0;
            for (var i = 0; i < bytes.Length; i += 4)
            {
                uint word = 0;
                for (var b = 0; b < 4; b++) word = (word << 8) | (i + b < bytes.Length ? bytes[i + b] : 0u);
                sum += word;
            }

            return sum;
        }

        var count = tables.Count;
        var power = 1;
        while (power * 2 <= count) power *= 2;
        var output = new MemoryStream();
        var header = new byte[12 + count * 16];
        BinaryPrimitives.WriteUInt32BigEndian(header, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), (ushort)(power * 16));
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8), (ushort)Math.Log2(power));
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(10), (ushort)(count * 16 - power * 16));
        output.Write(header);

        var offset = header.Length;
        var i = 0;
        var headOffset = 0;
        foreach (var (tag, bytes) in tables)
        {
            var record = 12 + i++ * 16;
            Encoding.ASCII.GetBytes(tag, header.AsSpan(record, 4));
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(record + 4), Checksum(bytes));
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(record + 8), (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(record + 12), (uint)bytes.Length);
            if (tag == "head") headOffset = offset;
            output.Write(bytes);
            var padding = (4 - bytes.Length % 4) % 4;
            output.Write(new byte[padding]);
            offset += bytes.Length + padding;
        }

        var file = output.ToArray();
        header.CopyTo(file, 0);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(headOffset + 8), 0xB1B0AFBA - Checksum(file));
        return file;
    }
}
