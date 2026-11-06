using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using ScoreStudio.Export;
using ScoreStudio.Import;
using ScoreStudio.Layout;
using ScoreStudio.Models;
using ScoreStudio.Services;

namespace ScoreStudio.Core.Tests;

/// <summary>Writing a laid-out score as a PDF (docs/PDF_EXPORT.md).</summary>
public sealed class PdfExporterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ScoreStudioPdf_" + Guid.NewGuid().ToString("N"));

    public PdfExporterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ScoreStudio.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("ScoreStudio.sln을 찾을 수 없습니다.");
    }

    private static string Bravura => Path.Combine(RepoRoot(), "ScoreStudio", "Fonts", "Bravura.otf");

    /// <summary>The Windows fonts on Windows; elsewhere DejaVu and a Korean font when they are installed.</summary>
    private static PdfFonts Fonts()
    {
        if (OperatingSystem.IsWindows()) return PdfFonts.Windows(Bravura);
        var d = "/usr/share/fonts/truetype/";
        string[] Chain(string first) => new[] { d + "dejavu/" + first, d + "wqy/wqy-zenhei.ttc", "/usr/share/fonts/truetype/nanum/NanumGothic.ttf" };
        return new PdfFonts(Bravura, Chain("DejaVuSans.ttf"), Chain("DejaVuSans-Bold.ttf"), Chain("DejaVuSans-Oblique.ttf"),
            Chain("DejaVuSans-BoldOblique.ttf"), Chain("DejaVuSerif.ttf"));
    }

    private static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "samples", "musicxml", name);

    /// <summary>The PDF's text with its streams inflated, so page content and font maps can be searched.</summary>
    private static string Readable(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var result = new StringBuilder();
        var at = 0;
        foreach (Match match in Regex.Matches(text, "/FlateDecode[^>]*>>\nstream\n"))
        {
            var start = match.Index + match.Length;
            var end = text.IndexOf("\nendstream", start, StringComparison.Ordinal);
            result.Append(text, at, start - at);
            try
            {
                using var zlib = new ZLibStream(new MemoryStream(pdf, start, end - start), CompressionMode.Decompress);
                using var reader = new StreamReader(zlib, Encoding.Latin1);
                result.Append(reader.ReadToEnd());
            }
            catch (InvalidDataException) { }

            at = end;
        }

        return result.Append(text, at, text.Length - at).ToString();
    }

    [Fact]
    public void Score_IsWrittenPageByPage_WithItsGlyphsAndText()
    {
        var project = MusicXmlImporter.ImportFile(Sample("pedal-segno-coda.musicxml")).Project;
        var layout = ScoreLayoutEngine.Layout(project);
        using var stream = new MemoryStream();
        PdfExporter.Write(layout, Fonts(), stream, project.Title);
        var bytes = stream.ToArray();
        var text = Readable(bytes);

        Assert.StartsWith("%PDF-1.7", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Equal(layout.Pages.Count, Regex.Matches(text, "/Type /Page /Parent").Count);
        // A4 in points: 210 × 297 mm.
        Assert.Contains("/MediaBox [0 0 595.276 841.89]", text);

        // The music font, whole, as OpenType; every glyph drawn is in its widths.
        Assert.Contains("/Subtype /OpenType", text);
        Assert.Contains("/CIDFontType0", text);
        var musicGlyphs = layout.Pages.SelectMany(p => p.Items).OfType<GlyphItem>().Select(g => (int)g.Glyph).Distinct().ToList();
        var bravura = OpenTypeFont.Load(Bravura);
        Assert.All(musicGlyphs, g => Assert.Contains($"{bravura.GlyphOf(g)} [", text));

        // The title can be copied back out: its characters are in a ToUnicode map.
        if (OperatingSystem.IsWindows() || File.Exists("/usr/share/fonts/truetype/wqy/wqy-zenhei.ttc"))
        {
            var 페 = string.Concat(Encoding.BigEndianUnicode.GetBytes("페").Select(b => b.ToString("X2")));
            Assert.Contains($"> <{페}>", text);
        }

        Assert.Contains("/Title <FEFF", text);
    }

    [Fact]
    public void ExportFile_RefusesTheSourceFile_AndLeavesNoTemporaryFile()
    {
        var source = Path.Combine(_dir, "원본.pdf");
        File.WriteAllText(source, "원본");
        var project = ScoreProject.CreateNew();
        project.ImportedFrom = source;
        var layout = ScoreLayoutEngine.Layout(project);

        Assert.Throws<ProjectFileException>(() => PdfExporter.ExportFile(project, layout, Fonts(), source));
        Assert.Equal("원본", File.ReadAllText(source));

        var target = Path.Combine(_dir, "악보.pdf");
        PdfExporter.ExportFile(project, layout, Fonts(), target);
        Assert.StartsWith("%PDF", File.ReadAllText(target, Encoding.Latin1));
        Assert.Equal(new[] { source, target }.Order(), Directory.GetFiles(_dir).Order());
    }

    [Fact]
    public void Fonts_AreRead_AndTrueTypeFontsKeepOnlyTheGlyphsUsed()
    {
        var bravura = OpenTypeFont.Load(Bravura);
        Assert.True(bravura.IsCff);
        Assert.True(bravura.Embeddable);
        Assert.NotEqual(0, bravura.GlyphOf(0xE050)); // gClef
        Assert.True(bravura.Advance(bravura.GlyphOf(0xE050)) > 0);

        var trueType = Fonts().Text.FirstOrDefault(File.Exists);
        if (trueType is null) return; // no TrueType font installed here
        var font = OpenTypeFont.Load(trueType);
        var a = font.GlyphOf('A');
        Assert.NotEqual(0, a);
        var subset = font.SubsetTrueType(new[] { a });
        Assert.True(subset.Length < font.Data.Length);
        var path = Path.Combine(_dir, "subset.ttf");
        File.WriteAllBytes(path, subset);
        var reread = OpenTypeFont.Load(path);
        Assert.Equal((font.NumGlyphs, font.UnitsPerEm, font.Advance(a)), (reread.NumGlyphs, reread.UnitsPerEm, reread.Advance(a)));
    }

    /// <summary>On the CI, a PDF of a real score goes with the previews, made with this machine's fonts.</summary>
    [Fact]
    public void PreviewPdf_IsWrittenForTheRelease()
    {
        var directory = Environment.GetEnvironmentVariable("SCORESTUDIO_PREVIEW_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var project = MusxSampleOrMusicXml();
        PdfExporter.ExportFile(project, ScoreLayoutEngine.Layout(project), Fonts(), Path.Combine(directory, "preview-zwei-gesange.pdf"));
    }

    /// <summary>Brahms's Zwei Gesänge through denigma when it is here, else a MusicXML sample.</summary>
    private static ScoreProject MusxSampleOrMusicXml()
    {
        var musx = Path.Combine(AppContext.BaseDirectory, "samples", "musx", "zwei_gesange.musx");
        if (File.Exists(musx) && DenigmaLocator.Find(null, AppContext.BaseDirectory, Environment.GetEnvironmentVariable, File.Exists) is { } denigma)
            return MusxImporter.ImportFile(musx, denigma).Project;
        return MusicXmlImporter.ImportFile(Sample("finale-marks.musicxml")).Project;
    }
}
