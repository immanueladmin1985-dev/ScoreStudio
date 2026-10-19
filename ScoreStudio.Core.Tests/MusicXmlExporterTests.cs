using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ScoreStudio.Export;
using ScoreStudio.Import;
using ScoreStudio.Layout;
using ScoreStudio.Models;
using ScoreStudio.Services;

namespace ScoreStudio.Core.Tests;

/// <summary>Acceptance tests from docs/MUSICXML_EXPORT.md.</summary>
public sealed class MusicXmlExporterTests : IDisposable
{
    private static readonly string SampleDirectory = Path.Combine(AppContext.BaseDirectory, "samples", "musicxml");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ScoreStudioExportTests", Guid.NewGuid().ToString("N"));

    public MusicXmlExporterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    public static IEnumerable<object[]> Samples() =>
        Directory.GetFiles(SampleDirectory, "*.musicxml").Select(f => new object[] { Path.GetFileName(f) });

    /// <summary>
    /// The project as saved, without what differs between two imports of the same score (part ids, times, source path,
    /// the schema version of an older file).
    /// Decimals are rounded, since layout distances pass through tenths of a staff space.
    /// </summary>
    private static string Normalized(ScoreProject project)
    {
        var copy = ProjectFileService.FromJson(ProjectFileService.ToJson(project));
        copy.SchemaVersion = ScoreProject.CurrentSchemaVersion;
        copy.CreatedUtc = copy.ModifiedUtc = default;
        copy.ImportedFrom = null;
        foreach (var part in copy.Parts) part.Id = "";
        return Regex.Replace(ProjectFileService.ToJson(copy), @"-?\d+\.\d+",
            m => Math.Round(double.Parse(m.Value, CultureInfo.InvariantCulture), 3).ToString(CultureInfo.InvariantCulture));
    }

    private static ImportResult RoundTrip(ScoreProject project) => MusicXmlImporter.ImportXml(MusicXmlExporter.ToXml(project), "roundtrip");

    [Theory]
    [MemberData(nameof(Samples))]
    public void EverySample_ReadsBackTheSameAfterExport(string sample)
    {
        var first = MusicXmlImporter.ImportFile(Path.Combine(SampleDirectory, sample));
        var second = RoundTrip(first.Project);

        Assert.Equal(Normalized(first.Project), Normalized(second.Project));
        Assert.Empty(second.Warnings.Select(w => w.Message).Except(first.Warnings.Select(w => w.Message)));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void EverySample_WritesNoteChildrenInSchemaOrder(string sample)
    {
        string[] order =
        {
            "grace", "chord", "pitch", "unpitched", "rest", "duration", "tie", "instrument", "voice", "type", "dot",
            "accidental", "time-modification", "stem", "notehead", "staff", "beam", "notations", "lyric",
        };
        var project = MusicXmlImporter.ImportFile(Path.Combine(SampleDirectory, sample)).Project;
        var document = XDocument.Parse(MusicXmlExporter.ToXml(project));

        foreach (var note in document.Descendants("note"))
        {
            var indices = note.Elements().Select(e => Array.IndexOf(order, e.Name.LocalName)).ToList();
            Assert.DoesNotContain(-1, indices);
            Assert.Equal(indices.OrderBy(i => i), indices);
        }
    }

    [Fact]
    public void ChoirSample_KeepsItsMusicAndSpellsNotesForTheKey()
    {
        var project = ProjectFileService.Load(Path.Combine(AppContext.BaseDirectory, "samples", "sample-choir.scoreproj"));
        var result = RoundTrip(project);
        Assert.Empty(result.Warnings);

        // Notes entered without a spelling come back spelled the way the score shows them.
        foreach (var measure in project.Parts.SelectMany(p => p.Measures))
        {
            foreach (var e in measure.Events.Where(e => e.Kind == "Note"))
                e.NotationSpelling ??= SpellingOf(Notation.Spell(e, measure.KeyFifths));
        }

        project.Title = result.Project.Title;
        Assert.Equal(Normalized(project), Normalized(result.Project));
    }

    private static string SpellingOf(SpelledPitch pitch) =>
        "CDEFGAB"[pitch.Step] + (pitch.Alter > 0 ? new string('#', pitch.Alter) : new string('b', -pitch.Alter)) + pitch.Octave.ToString(CultureInfo.InvariantCulture);

    private static ScoreProject Score(int keyFifths, params ScoreEvent[][] measures) => new()
    {
        Title = "테스트",
        Parts =
        {
            new ScorePart
            {
                Name = "노래",
                Measures = measures.Select((events, i) => new ScoreMeasure { Number = i + 1, KeyFifths = keyFifths, Events = events.ToList() }).ToList(),
            },
        },
    };

    private static ScoreEvent Note(string spelling, int midi, int position, int duration, int positionDenominator = 1, int durationDenominator = 1) => new()
    {
        MidiPitch = midi, NotationSpelling = spelling,
        PositionNumerator = position, PositionDenominator = positionDenominator,
        DurationNumerator = duration, DurationDenominator = durationDenominator,
    };

    [Fact]
    public void Ties_HaveAStartAndAStopAcrossTheBarline()
    {
        var tied = Note("C5", 72, 0, 4);
        tied.TieToNext = true;
        var project = Score(0, new[] { tied }, new[] { Note("C5", 72, 0, 4) });

        var notes = XDocument.Parse(MusicXmlExporter.ToXml(project)).Descendants("note").ToList();
        Assert.Equal(new[] { "start" }, notes[0].Elements("tie").Select(t => (string?)t.Attribute("type")));
        Assert.Equal(new[] { "start" }, notes[0].Descendants("tied").Select(t => (string?)t.Attribute("type")));
        Assert.Equal(new[] { "stop" }, notes[1].Elements("tie").Select(t => (string?)t.Attribute("type")));
        Assert.Equal(new[] { "stop" }, notes[1].Descendants("tied").Select(t => (string?)t.Attribute("type")));
        Assert.True(RoundTrip(project).Project.Parts[0].Measures[0].Events[0].TieToNext);
    }

    [Fact]
    public void Accidentals_AreWrittenOnlyWhereTheKeyOrAnEarlierNoteDiffers()
    {
        // G major: F♯ is in the key. F♮ needs a natural, the next F does not, F♯ then needs its sharp back.
        var project = Score(1, new[]
        {
            Note("F#4", 66, 0, 1), Note("F4", 65, 1, 1), Note("F4", 65, 2, 1), Note("F#4", 66, 3, 1),
        }, new[] { Note("Bb4", 70, 0, 2), Note("F5", 77, 2, 2) });

        var accidentals = XDocument.Parse(MusicXmlExporter.ToXml(project)).Descendants("note")
            .Select(n => (string?)n.Element("accidental")).ToList();
        Assert.Equal(new string?[] { null, "natural", null, "sharp", "flat", "natural" }, accidentals);
    }

    [Fact]
    public void NestedTuplets_GetTheirOwnNumbersAndReadBackAsTwoSpans()
    {
        // A quarter-note triplet over two beats whose middle quarter holds an eighth-note triplet.
        var measure = new ScoreMeasure
        {
            Numerator = 2, Denominator = 4,
            Events =
            {
                Note("C5", 72, 0, 2, 1, 3),
                Note("D5", 74, 2, 2, 3, 9), Note("E5", 76, 8, 2, 9, 9), Note("F5", 77, 10, 2, 9, 9),
                Note("G5", 79, 4, 2, 3, 3),
            },
            Tuplets =
            {
                new ScoreTuplet { PositionNumerator = 0, DurationNumerator = 2, Actual = 3, Normal = 2 },
                new ScoreTuplet { PositionNumerator = 2, PositionDenominator = 3, DurationNumerator = 2, DurationDenominator = 3, Actual = 3, Normal = 2 },
            },
        };
        var project = new ScoreProject { Parts = { new ScorePart { Measures = { measure } } } };

        var document = XDocument.Parse(MusicXmlExporter.ToXml(project));
        var starts = document.Descendants("tuplet").Where(t => (string?)t.Attribute("type") == "start").Select(t => (string?)t.Attribute("number")).ToList();
        Assert.Equal(new[] { "1", "2" }, starts);
        Assert.Equal("9", document.Descendants("note").ElementAt(1).Element("time-modification")?.Element("actual-notes")?.Value);

        // The importer lists a tuplet when it reads its end, so the inner one comes first.
        var result = RoundTrip(project);
        Assert.Empty(result.Warnings);
        Assert.Equal(
            measure.Tuplets.AsEnumerable().Reverse().Select(t => (t.PositionNumerator, t.PositionDenominator, t.DurationNumerator, t.DurationDenominator)),
            result.Project.Parts[0].Measures[0].Tuplets.Select(t => (t.PositionNumerator, t.PositionDenominator, t.DurationNumerator, t.DurationDenominator)));
    }

    [Fact]
    public void Mxl_HasAContainerPointingAtTheScoreAndImportsAgain()
    {
        var first = MusicXmlImporter.ImportFile(Path.Combine(SampleDirectory, "hymn-repeats-chords.musicxml"));
        var path = Path.Combine(_dir, "찬송.mxl");
        MusicXmlExporter.ExportFile(first.Project, path);

        using (var archive = ZipFile.OpenRead(path))
        {
            Assert.Equal("mimetype", archive.Entries[0].FullName);
            using var container = archive.GetEntry("META-INF/container.xml")!.Open();
            Assert.Equal("score.musicxml", (string?)XDocument.Load(container).Descendants("rootfile").Single().Attribute("full-path"));
        }

        Assert.Equal(Normalized(first.Project), Normalized(MusicXmlImporter.ImportFile(path).Project));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Export_RefusesToOverwriteTheFileTheScoreCameFrom()
    {
        var source = Path.Combine(_dir, "원본.musicxml");
        File.Copy(Path.Combine(SampleDirectory, "simple-melody.musicxml"), source);
        var before = File.ReadAllBytes(source);
        var project = MusicXmlImporter.ImportFile(source).Project;

        var error = Assert.Throws<ProjectFileException>(() => MusicXmlExporter.ExportFile(project, source));
        Assert.Contains("원본 파일", error.Message);
        Assert.Equal(before, File.ReadAllBytes(source));

        var other = Path.Combine(_dir, "사본.musicxml");
        Assert.Empty(MusicXmlExporter.ExportFile(project, other));
        Assert.True(File.Exists(other));
    }
}
