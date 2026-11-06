using System.Globalization;
using System.IO.Compression;
using System.Text;
using ScoreStudio.Layout;
using ScoreStudio.Models;
using ScoreStudio.Services;

namespace ScoreStudio.Export;

/// <summary>
/// Font files for a PDF (docs/PDF_EXPORT.md): the music font, and for each text style the fonts to try in order (a
/// character takes the first font that has it). Missing files are skipped.
/// </summary>
public sealed record PdfFonts(string MusicFont, IReadOnlyList<string> Text, IReadOnlyList<string> Bold, IReadOnlyList<string> Italic,
    IReadOnlyList<string> BoldItalic, IReadOnlyList<string> Serif)
{
    /// <summary>The fonts the screen uses (Segoe UI, Malgun Gothic, Times New Roman), from the Windows fonts folder.</summary>
    public static PdfFonts Windows(string musicFont)
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        string[] In(params string[] names) => names.Select(n => Path.Combine(folder, n)).ToArray();
        return new PdfFonts(musicFont,
            In("segoeui.ttf", "malgun.ttf", "seguisym.ttf"),
            In("segoeuib.ttf", "malgunbd.ttf", "malgun.ttf", "seguisym.ttf"),
            In("segoeuii.ttf", "segoeui.ttf", "malgun.ttf", "seguisym.ttf"),
            In("segoeuiz.ttf", "segoeuib.ttf", "malgunbd.ttf", "malgun.ttf", "seguisym.ttf"),
            In("times.ttf", "batang.ttc", "malgun.ttf", "seguisym.ttf"));
    }
}

/// <summary>Writes a laid-out score as a vector PDF with its fonts embedded (docs/PDF_EXPORT.md).</summary>
public static class PdfExporter
{
    private const double PointsPerMm = 72 / 25.4;

    /// <summary>Writes the PDF to a file through a temporary file, refusing the file the score was imported from.</summary>
    public static void ExportFile(ScoreProject project, ScoreLayout layout, PdfFonts fonts, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ProjectFileException($"내보낼 파일 경로가 올바르지 않습니다.\n{path}", ex);
        }

        if (MusicXmlExporter.IsSourceFile(project, fullPath))
            throw new ProjectFileException($"악보를 가져온 원본 파일에는 내보낼 수 없습니다. 다른 이름을 고르세요.\n{fullPath}");

        var directory = Path.GetDirectoryName(fullPath) ?? throw new ProjectFileException("내보낼 폴더를 확인할 수 없습니다.");
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write))
                Write(layout, fonts, stream, project.Title);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(tempPath); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            throw new ProjectFileException($"PDF를 내보낼 수 없습니다.\n{fullPath}\n\n{ex.Message}", ex);
        }
    }

    /// <summary>Writes every page of a layout to a PDF stream.</summary>
    public static void Write(ScoreLayout layout, PdfFonts fonts, Stream output, string title = "")
    {
        var music = File.Exists(fonts.MusicFont) ? new EmbeddedFont(OpenTypeFont.Load(fonts.MusicFont)) : null;
        var cache = new Dictionary<string, EmbeddedFont?>(StringComparer.OrdinalIgnoreCase);
        List<EmbeddedFont> Chain(IEnumerable<string> paths) => paths.Select(p =>
        {
            if (!cache.TryGetValue(p, out var font))
            {
                try { font = File.Exists(p) && OpenTypeFont.Load(p) is { Embeddable: true } f ? new EmbeddedFont(f) : null; }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentOutOfRangeException or IndexOutOfRangeException) { font = null; }
                cache[p] = font;
            }

            return font;
        }).OfType<EmbeddedFont>().ToList();
        var chains = new Dictionary<(bool Bold, bool Italic, bool Serif), List<EmbeddedFont>>
        {
            [(false, false, false)] = Chain(fonts.Text),
            [(true, false, false)] = Chain(fonts.Bold),
            [(false, true, false)] = Chain(fonts.Italic),
            [(true, true, false)] = Chain(fonts.BoldItalic),
        };
        var serif = Chain(fonts.Serif);

        var pdf = new PdfDocument();
        var pageIds = new List<int>();
        var pagesId = pdf.Reserve();
        foreach (var page in layout.Pages)
        {
            var content = new StringBuilder();
            var height = page.Height * PointsPerMm;
            foreach (var item in page.Items)
                WriteItem(content, item, height, music, chains, serif);
            var contentId = pdf.AddStream(Encoding.Latin1.GetBytes(content.ToString()), compress: true);
            pageIds.Add(pdf.Reserve());
            pdf.Set(pageIds[^1], $"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 {N(page.Width * PointsPerMm)} {N(height)}] /Resources {pdf.FontResourcesPlaceholder} /Contents {contentId} 0 R >>");
        }

        // Fonts last, once every glyph they need is known.
        var all = new[] { music }.Concat(cache.Values).OfType<EmbeddedFont>().Where(f => f.Used.Count > 0).Distinct().ToList();
        var resources = new StringBuilder("<< /Font << ");
        foreach (var font in all) resources.Append($"/{font.ResourceName} {font.WriteTo(pdf)} 0 R ");
        resources.Append(">> >>");
        pdf.ReplaceInAll(pdf.FontResourcesPlaceholder, resources.ToString());

        pdf.Set(pagesId, $"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(id => $"{id} 0 R"))}] /Count {pageIds.Count} >>");
        var info = pdf.Add($"<< /Title {PdfDocument.TextString(title)} /Producer (ScoreStudio) >>");
        var catalog = pdf.Add($"<< /Type /Catalog /Pages {pagesId} 0 R >>");
        pdf.WriteTo(output, catalog, info);
    }

    private static string N(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);

    private static (double X, double Y) P(double x, double y, double height) => (x * PointsPerMm, height - y * PointsPerMm);

    private static void WriteItem(StringBuilder content, DrawItem item, double height, EmbeddedFont? music,
        Dictionary<(bool, bool, bool), List<EmbeddedFont>> chains, List<EmbeddedFont> serif)
    {
        switch (item)
        {
            case LineItem line:
            {
                var (x1, y1) = P(line.X1, line.Y1, height);
                var (x2, y2) = P(line.X2, line.Y2, height);
                content.Append($"{N(line.Thickness * PointsPerMm)} w {N(x1)} {N(y1)} m {N(x2)} {N(y2)} l S\n");
                break;
            }
            case BeamItem beam:
            {
                var a = P(beam.X1, beam.Y1, height);
                var b = P(beam.X2, beam.Y2, height);
                var c = P(beam.X2, beam.Y2 + beam.Thickness, height);
                var d = P(beam.X1, beam.Y1 + beam.Thickness, height);
                content.Append($"{N(a.X)} {N(a.Y)} m {N(b.X)} {N(b.Y)} l {N(c.X)} {N(c.Y)} l {N(d.X)} {N(d.Y)} l h f\n");
                break;
            }
            case CurveItem curve:
            {
                var (ix1, iy1, ix2, iy2) = curve.InnerControls();
                var s = P(curve.X1, curve.Y1, height);
                var c1 = P(curve.ControlX1, curve.ControlY1, height);
                var c2 = P(curve.ControlX2, curve.ControlY2, height);
                var e = P(curve.X2, curve.Y2, height);
                var i2 = P(ix2, iy2, height);
                var i1 = P(ix1, iy1, height);
                content.Append($"{N(s.X)} {N(s.Y)} m {N(c1.X)} {N(c1.Y)} {N(c2.X)} {N(c2.Y)} {N(e.X)} {N(e.Y)} c {N(i2.X)} {N(i2.Y)} {N(i1.X)} {N(i1.Y)} {N(s.X)} {N(s.Y)} c h f\n");
                break;
            }
            case GlyphItem glyph when music is not null:
            {
                var (x, y) = P(glyph.X, glyph.Y, height);
                var id = music.Use(glyph.Glyph);
                content.Append($"BT /{music.ResourceName} {N(glyph.Size * PointsPerMm)} Tf {N(x)} {N(y)} Td <{id:X4}> Tj ET\n");
                break;
            }
            case TextItem text when text.Text.Length > 0:
            {
                var chain = text.Serif ? serif : chains[(text.Bold, text.Italic, false)];
                if (chain.Count == 0) break;
                var size = text.Size * PointsPerMm;
                var runs = Runs(text.Text, chain);
                var width = runs.Sum(r => r.Font.Width(r.Glyphs) * size);
                var (x, y) = P(text.X, text.Y, height);
                // Along the baseline, turned clockwise on the page (counter-clockwise in PDF, whose y points up).
                var angle = -text.Angle * Math.PI / 180;
                var (cos, sin) = (Math.Cos(angle), Math.Sin(angle));
                var shift = -(text.Anchor switch { TextAnchor.Middle => width / 2, TextAnchor.End => width, _ => 0 });
                foreach (var run in runs)
                {
                    content.Append($"BT /{run.Font.ResourceName} {N(size)} Tf {N(cos)} {N(sin)} {N(-sin)} {N(cos)} {N(x + shift * cos)} {N(y + shift * sin)} Tm <{string.Concat(run.Glyphs.Select(g => g.ToString("X4")))}> Tj ET\n");
                    shift += run.Font.Width(run.Glyphs) * size;
                }

                break;
            }
        }
    }

    /// <summary>The text split into runs of one font each: every character in the first font of the chain that has it.</summary>
    private static List<(EmbeddedFont Font, List<int> Glyphs)> Runs(string text, List<EmbeddedFont> chain)
    {
        var runs = new List<(EmbeddedFont Font, List<int> Glyphs)>();
        for (var i = 0; i < text.Length; i++)
        {
            var codePoint = char.ConvertToUtf32(text, i);
            if (char.IsHighSurrogate(text[i])) i++;
            var font = chain.FirstOrDefault(f => f.Font.GlyphOf(codePoint) != 0) ?? chain[0];
            var glyph = font.Use(codePoint);
            if (runs.Count == 0 || runs[^1].Font != font) runs.Add((font, new List<int>()));
            runs[^1].Glyphs.Add(glyph);
        }

        return runs;
    }

    /// <summary>A font in the PDF: the glyphs used, and the characters they stand for (for copying text).</summary>
    private sealed class EmbeddedFont
    {
        private static int _next;

        public EmbeddedFont(OpenTypeFont font)
        {
            Font = font;
            ResourceName = "F" + Interlocked.Increment(ref _next).ToString(CultureInfo.InvariantCulture);
        }

        public OpenTypeFont Font { get; }
        public string ResourceName { get; }
        public SortedDictionary<int, int> Used { get; } = new(); // glyph → character

        public int Use(int codePoint)
        {
            var glyph = Font.GlyphOf(codePoint);
            Used.TryAdd(glyph, codePoint);
            return glyph;
        }

        /// <summary>Width of glyphs at a size of 1.</summary>
        public double Width(IEnumerable<int> glyphs) => glyphs.Sum(g => Font.Advance(g)) / (double)Font.UnitsPerEm;

        /// <summary>Writes the font (Type 0, Identity-H) with its program, widths and ToUnicode map; returns its object number.</summary>
        public int WriteTo(PdfDocument pdf)
        {
            var scale = 1000.0 / Font.UnitsPerEm;
            var tag = new string(ResourceName.PadLeft(6, 'A').Select(c => (char)('A' + c % 26)).ToArray());
            var name = $"{tag}+{Font.Name}";
            var program = Font.IsCff ? Font.Data : Font.SubsetTrueType(Used.Keys);
            var fileId = pdf.AddStream(program, compress: true, extra: Font.IsCff ? "/Subtype /OpenType" : $"/Length1 {program.Length}");
            var (xMin, yMin, xMax, yMax) = Font.BoundingBox;
            var descriptor = pdf.Add($"<< /Type /FontDescriptor /FontName /{name} /Flags 4 /FontBBox [{N(xMin * scale)} {N(yMin * scale)} {N(xMax * scale)} {N(yMax * scale)}] " +
                $"/ItalicAngle 0 /Ascent {N(Font.Ascent * scale)} /Descent {N(Font.Descent * scale)} /CapHeight {N(Font.Ascent * scale)} /StemV 80 " +
                $"/{(Font.IsCff ? "FontFile3" : "FontFile2")} {fileId} 0 R >>");
            var widths = string.Join(" ", Used.Keys.Select(g => $"{g} [{N(Font.Advance(g) * scale)}]"));
            var cid = pdf.Add($"<< /Type /Font /Subtype /{(Font.IsCff ? "CIDFontType0" : "CIDFontType2")} /BaseFont /{name} " +
                $"/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor {descriptor} 0 R /W [{widths}]" +
                (Font.IsCff ? "" : " /CIDToGIDMap /Identity") + " >>");
            var toUnicode = pdf.AddStream(Encoding.ASCII.GetBytes(ToUnicode()), compress: true);
            return pdf.Add($"<< /Type /Font /Subtype /Type0 /BaseFont /{name} /Encoding /Identity-H /DescendantFonts [{cid} 0 R] /ToUnicode {toUnicode} 0 R >>");
        }

        private string ToUnicode()
        {
            var map = new StringBuilder("/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def " +
                "/CMapName /Adobe-Identity-UCS def /CMapType 2 def 1 begincodespacerange <0000> <FFFF> endcodespacerange\n");
            foreach (var chunk in Used.Where(u => u.Key != 0).Chunk(100))
            {
                map.Append(chunk.Length).Append(" beginbfchar\n");
                foreach (var (glyph, codePoint) in chunk)
                    map.Append($"<{glyph:X4}> <{string.Concat(Encoding.BigEndianUnicode.GetBytes(char.ConvertFromUtf32(codePoint)).Select(b => b.ToString("X2")))}>\n");
                map.Append("endbfchar\n");
            }

            return map.Append("endcmap CMapName currentdict /CMap defineresource pop end end").ToString();
        }
    }

    /// <summary>The objects of a PDF file and how to write them with their cross-reference table.</summary>
    private sealed class PdfDocument
    {
        private readonly List<byte[]?> _objects = new();

        /// <summary>Stands for the shared font resources in page dictionaries until the fonts are written.</summary>
        public string FontResourcesPlaceholder => "%%FONTS%%";

        public int Reserve()
        {
            _objects.Add(null);
            return _objects.Count;
        }

        public void Set(int id, string body) => _objects[id - 1] = Encoding.Latin1.GetBytes(body);

        public int Add(string body)
        {
            var id = Reserve();
            Set(id, body);
            return id;
        }

        public int AddStream(byte[] data, bool compress, string extra = "")
        {
            var bytes = data;
            if (compress)
            {
                using var buffer = new MemoryStream();
                using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(data);
                bytes = buffer.ToArray();
            }

            var head = Encoding.Latin1.GetBytes($"<< /Length {bytes.Length}{(compress ? " /Filter /FlateDecode" : "")} {extra} >>\nstream\n");
            var tail = Encoding.Latin1.GetBytes("\nendstream");
            var id = Reserve();
            _objects[id - 1] = head.Concat(bytes).Concat(tail).ToArray();
            return id;
        }

        public void ReplaceInAll(string placeholder, string value)
        {
            for (var i = 0; i < _objects.Count; i++)
            {
                if (_objects[i] is not { } bytes) continue;
                var text = Encoding.Latin1.GetString(bytes);
                if (!text.StartsWith("<< /Type /Page ", StringComparison.Ordinal)) continue;
                _objects[i] = Encoding.Latin1.GetBytes(text.Replace(placeholder, value, StringComparison.Ordinal));
            }
        }

        /// <summary>A PDF text string in UTF-16 with a byte order mark, so Korean titles survive.</summary>
        public static string TextString(string text) =>
            "<FEFF" + string.Concat(Encoding.BigEndianUnicode.GetBytes(text).Select(b => b.ToString("X2"))) + ">";

        public void WriteTo(Stream output, int catalog, int info)
        {
            var offsets = new long[_objects.Count];
            void Raw(string s) => output.Write(Encoding.Latin1.GetBytes(s));
            Raw("%PDF-1.7\n");
            output.Write(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' });
            for (var i = 0; i < _objects.Count; i++)
            {
                offsets[i] = output.Position;
                Raw($"{i + 1} 0 obj\n");
                output.Write(_objects[i] ?? Encoding.Latin1.GetBytes("null"));
                Raw("\nendobj\n");
            }

            var xref = output.Position;
            Raw($"xref\n0 {_objects.Count + 1}\n0000000000 65535 f \n");
            foreach (var offset in offsets) Raw($"{offset:D10} 00000 n \n");
            Raw($"trailer\n<< /Size {_objects.Count + 1} /Root {catalog} 0 R /Info {info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        }
    }
}
