using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ScoreStudio.Import;
using ScoreStudio.Layout;
using ScoreStudio.Models;
using ScoreStudio.Services;

namespace ScoreStudio.Export;

public sealed class ExportResult
{
    public required XDocument Document { get; init; }

    /// <summary>What could not be written exactly, grouped like import warnings.</summary>
    public required IReadOnlyList<ImportWarning> Warnings { get; init; }
}

/// <summary>
/// Writes a <see cref="ScoreProject"/> as partwise MusicXML 4.0 (.musicxml, .xml) or compressed MusicXML (.mxl).
/// Everything <see cref="MusicXmlImporter"/> reads is written back with the same meaning (docs/MUSICXML_EXPORT.md).
/// </summary>
public static partial class MusicXmlExporter
{
    private const string MxlMediaType = "application/vnd.recordare.musicxml";
    private const string MxlScorePath = "score.musicxml";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static ExportResult Export(ScoreProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var warnings = new ImportWarningCollector();

        var identification = new XElement("identification");
        if (!string.IsNullOrWhiteSpace(project.Composer))
            identification.Add(new XElement("creator", new XAttribute("type", "composer"), project.Composer.Trim()));
        identification.Add(new XElement("encoding",
            new XElement("software", "ScoreStudio"),
            new XElement("encoding-date", DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));

        var root = new XElement("score-partwise", new XAttribute("version", "4.0"),
            new XElement("work", new XElement("work-title", project.Title)),
            identification,
            Defaults(project),
            PartList(project));

        for (var i = 0; i < project.Parts.Count; i++)
            root.Add(new PartWriter(project.Parts[i], PartId(i), warnings).Write());

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", "no"),
            new XDocumentType("score-partwise", "-//Recordare//DTD MusicXML 4.0 Partwise//EN", "http://www.musicxml.org/dtds/partwise.dtd", null),
            root);
        return new ExportResult { Document = document, Warnings = warnings.ToList() };
    }

    /// <summary>The project as MusicXML text.</summary>
    public static string ToXml(ScoreProject project)
    {
        using var stream = new MemoryStream();
        WriteXml(stream, Export(project).Document);
        return Utf8NoBom.GetString(stream.ToArray());
    }

    /// <summary>
    /// Writes the project to <paramref name="path"/>: compressed MusicXML for ".mxl", plain MusicXML otherwise. The file the
    /// score was imported from is never overwritten, and a failed export leaves an existing file as it was.
    /// </summary>
    public static IReadOnlyList<ImportWarning> ExportFile(ScoreProject project, string path)
    {
        ArgumentNullException.ThrowIfNull(project);
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

        if (IsSourceFile(project, fullPath))
            throw new ProjectFileException($"악보를 가져온 원본 파일에는 내보낼 수 없습니다. 다른 이름을 고르세요.\n{fullPath}");

        var result = Export(project);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ProjectFileException("내보낼 폴더를 확인할 수 없습니다.");
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write))
            {
                if (string.Equals(Path.GetExtension(fullPath), ".mxl", StringComparison.OrdinalIgnoreCase))
                    WriteMxl(stream, result.Document);
                else
                    WriteXml(stream, result.Document);
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(tempPath); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            throw new ProjectFileException($"파일을 내보낼 수 없습니다.\n{fullPath}\n\n{ex.Message}", ex);
        }

        return result.Warnings;
    }

    private static bool IsSourceFile(ScoreProject project, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(project.ImportedFrom) || !Path.IsPathRooted(project.ImportedFrom)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(project.ImportedFrom), fullPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static void WriteXml(Stream stream, XDocument document)
    {
        var settings = new XmlWriterSettings { Encoding = Utf8NoBom, Indent = true, IndentChars = "  " };
        using var writer = XmlWriter.Create(stream, settings);
        document.Save(writer);
    }

    /// <summary>Compressed MusicXML: an uncompressed "mimetype" entry first, then the container and the score.</summary>
    private static void WriteMxl(Stream stream, XDocument document)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        using (var mimetype = new StreamWriter(archive.CreateEntry("mimetype", CompressionLevel.NoCompression).Open(), Utf8NoBom))
            mimetype.Write(MxlMediaType);

        var container = new XDocument(new XDeclaration("1.0", "UTF-8", "no"),
            new XElement("container",
                new XElement("rootfiles",
                    new XElement("rootfile", new XAttribute("full-path", MxlScorePath), new XAttribute("media-type", MxlMediaType + "+xml")))));
        using (var entry = archive.CreateEntry("META-INF/container.xml").Open())
            WriteXml(entry, container);
        using (var entry = archive.CreateEntry(MxlScorePath).Open())
            WriteXml(entry, document);
    }

    private static string PartId(int index) => $"P{index + 1}";

    internal static string Number(double value) => Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>Page and staff layout, written only for scores that have the source's layout (40 tenths = one staff height).</summary>
    private static XElement? Defaults(ScoreProject project)
    {
        if (project.Engraving is not { } engraving) return null;

        string Tenths(double millimetres) => Number(millimetres * 10 / engraving.StaffSpace);
        var (width, height) = PageSizes.Of(project);
        var defaults = new XElement("defaults",
            new XElement("scaling", new XElement("millimeters", Number(engraving.StaffSpace * 4)), new XElement("tenths", "40")),
            new XElement("page-layout",
                new XElement("page-height", Tenths(height)),
                new XElement("page-width", Tenths(width)),
                new XElement("page-margins", new XAttribute("type", "both"),
                    new XElement("left-margin", Tenths(engraving.MarginLeft)),
                    new XElement("right-margin", Tenths(engraving.MarginRight)),
                    new XElement("top-margin", Tenths(engraving.MarginTop)),
                    new XElement("bottom-margin", Tenths(engraving.MarginBottom)))),
            new XElement("system-layout",
                new XElement("system-distance", Number(engraving.SystemDistance * 10)),
                new XElement("top-system-distance", Number(engraving.TopSystemDistance * 10))),
            new XElement("staff-layout", new XElement("staff-distance", Number(engraving.StaffDistance * 10))));

        if (engraving.LyricFontSize is not null || engraving.LyricFontFamily is not null)
        {
            var font = new XElement("lyric-font");
            if (engraving.LyricFontFamily is { } family) font.Add(new XAttribute("font-family", family));
            if (engraving.LyricFontSize is { } size) font.Add(new XAttribute("font-size", Number(size * 72 / 25.4)));
            defaults.Add(font);
        }

        return defaults;
    }

    /// <summary>
    /// Score parts with their groups around them. Each group has its own number, so groups may overlap; stops are written
    /// in the project's group order, which is the order the importer reads them back in.
    /// </summary>
    private static XElement PartList(ScoreProject project)
    {
        var list = new XElement("part-list");
        var groups = project.PartGroups.Select((g, i) => (Group: g, Number: (i + 1).ToString(CultureInfo.InvariantCulture))).ToList();
        for (var i = 0; i < project.Parts.Count; i++)
        {
            foreach (var (group, number) in groups.Where(g => g.Group.FirstPart == i).OrderByDescending(g => g.Group.LastPart))
            {
                list.Add(new XElement("part-group", new XAttribute("type", "start"), new XAttribute("number", number),
                    new XElement("group-symbol", group.Symbol switch
                    {
                        PartGroupSymbol.Brace => "brace",
                        PartGroupSymbol.Bracket => "bracket",
                        PartGroupSymbol.Line => "line",
                        PartGroupSymbol.Square => "square",
                        _ => "none",
                    }),
                    new XElement("group-barline", group.JoinBarlines ? "yes" : "no")));
            }

            var part = project.Parts[i];
            var name = new XElement("part-name", part.Name);
            if (!part.ShowName) name.Add(new XAttribute("print-object", "no"));
            var scorePart = new XElement("score-part", new XAttribute("id", PartId(i)), name);
            if (!string.IsNullOrWhiteSpace(part.Instrument))
                scorePart.Add(new XElement("score-instrument", new XAttribute("id", $"{PartId(i)}-I1"), new XElement("instrument-name", part.Instrument)));
            list.Add(scorePart);

            foreach (var (_, number) in groups.Where(g => g.Group.LastPart == i))
                list.Add(new XElement("part-group", new XAttribute("type", "stop"), new XAttribute("number", number)));
        }

        return list;
    }
}
