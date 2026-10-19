using System.IO.Compression;
using ScoreStudio.Import;
using ScoreStudio.Services;

namespace ScoreStudio.Core.Tests;

/// <summary>Acceptance tests from docs/MUSX_IMPORT.md.</summary>
public sealed class MusxImporterTests : IDisposable
{
    private static readonly string MusxSamples = Path.Combine(AppContext.BaseDirectory, "samples", "musx");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ScoreStudioMusxTests", Guid.NewGuid().ToString("N"));

    public MusxImporterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Locator_LooksAtTheChosenPath_TheVariable_TheProgramFolder_ThenThePath()
    {
        var environment = new Dictionary<string, string?> { ["SCORESTUDIO_DENIGMA"] = "/env/denigma", ["PATH"] = "/usr/bin" };
        var candidates = DenigmaLocator.Candidates("/chosen/denigma", "/app", n => environment.GetValueOrDefault(n)).ToList();

        Assert.Equal("/chosen/denigma", candidates[0]);
        Assert.Equal("/env/denigma", candidates[1]);
        Assert.Equal(Path.Combine("/app", "tools", "denigma", "denigma.exe"), candidates[2]);
        Assert.Contains(Path.Combine("/usr/bin", "denigma"), candidates);
        Assert.Equal(Path.Combine("/app", "tools", "denigma", "denigma.exe"),
            DenigmaLocator.Find(null, "/app", _ => null, p => p.EndsWith("denigma.exe", StringComparison.Ordinal)));
    }

    /// <summary>A stand-in for denigma (Linux and macOS only): it copies a MusicXML sample to the output path it is given.</summary>
    private string FakeConverter(string body)
    {
        var path = Path.Combine(_dir, "fake-denigma");
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private string MusxFile(string name = "악보.musx")
    {
        var path = Path.Combine(_dir, name);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            zip.CreateEntry("score.dat");
        return path;
    }

    [Fact]
    public void Import_ConvertsACopy_LeavesTheOriginalAndNoWorkFolder_AndNotesTheConverter()
    {
        if (OperatingSystem.IsWindows()) return;
        var sample = Path.Combine(AppContext.BaseDirectory, "samples", "musicxml", "finale-marks.musicxml");
        var converter = FakeConverter($"[ \"$1\" = export ] && [ \"$3\" = --musicxml ] && cp '{sample}' \"$4\"");
        var source = MusxFile();
        var before = (File.ReadAllBytes(source), File.GetLastWriteTimeUtc(source));
        var workFolders = Directory.Exists(Path.Combine(Path.GetTempPath(), "ScoreStudioMusx"))
            ? Directory.GetDirectories(Path.Combine(Path.GetTempPath(), "ScoreStudioMusx")).Length
            : 0;

        var result = MusxImporter.ImportFile(source, converter);

        Assert.Equal(before.Item1, File.ReadAllBytes(source));
        Assert.Equal(before.Item2, File.GetLastWriteTimeUtc(source));
        Assert.Equal(workFolders, Directory.GetDirectories(Path.Combine(Path.GetTempPath(), "ScoreStudioMusx")).Length);
        Assert.Equal(MusxImporter.ConverterNote, result.Warnings[0].Message);
        Assert.Equal(Path.GetFullPath(source), result.Project.ImportedFrom);
        Assert.Equal("셈여림과 아티큘레이션", result.Project.Title);
        Assert.NotEmpty(result.Project.Parts[0].Measures[0].Directions);
    }

    [Fact]
    public void Import_ExplainsAFailedConversion()
    {
        if (OperatingSystem.IsWindows()) return;
        var converter = FakeConverter("echo 'unsupported musx version' >&2; exit 3");

        var error = Assert.Throws<ProjectFileException>(() => MusxImporter.ImportFile(MusxFile(), converter));
        Assert.Contains("종료 코드 3", error.Message);
        Assert.Contains("unsupported musx version", error.Message);
    }

    [Fact]
    public void Import_StopsAConverterThatRunsTooLong()
    {
        if (OperatingSystem.IsWindows()) return;
        var converter = FakeConverter("sleep 30");

        var error = Assert.Throws<ProjectFileException>(() => MusxImporter.ImportFile(MusxFile(), converter, TimeSpan.FromSeconds(1)));
        Assert.Contains("끝나지 않아", error.Message);
    }

    [Fact]
    public void Import_RefusesFilesThatAreNotMusx_BeforeRunningAnything()
    {
        var missing = Path.Combine(_dir, "없음.musx");
        var empty = Path.Combine(_dir, "빈.musx");
        File.WriteAllBytes(empty, Array.Empty<byte>());
        var text = Path.Combine(_dir, "글.musx");
        File.WriteAllText(text, "not a zip");
        var converter = Path.Combine(_dir, "no-such-program");

        Assert.Contains("찾을 수 없습니다", Assert.Throws<ProjectFileException>(() => MusxImporter.ImportFile(missing, converter)).Message);
        Assert.Contains("비어 있습니다", Assert.Throws<ProjectFileException>(() => MusxImporter.ImportFile(empty, converter)).Message);
        Assert.Contains(".musx 파일이 아닙니다", Assert.Throws<ProjectFileException>(() => MusxImporter.ImportFile(text, converter)).Message);
        Assert.Contains("denigma", Assert.Throws<ProjectFileException>(() => MusxImporter.ImportFile(MusxFile(), converter)).Message);
    }

    public static IEnumerable<object[]> Samples() =>
        Directory.Exists(MusxSamples)
            ? Directory.GetFiles(MusxSamples, "*.musx").Select(f => new object[] { Path.GetFileName(f) })
            : Array.Empty<object[]>();

    /// <summary>
    /// The real denigma named by SCORESTUDIO_DENIGMA (skipped without it; the Windows CI requires it): every sample .musx
    /// opens and has notes.
    /// </summary>
    [Theory]
    [MemberData(nameof(Samples))]
    public void RealDenigma_OpensTheSampleFinaleFiles(string sample)
    {
        var denigma = Environment.GetEnvironmentVariable(DenigmaLocator.EnvironmentVariable);
        if (string.IsNullOrEmpty(denigma) || !File.Exists(denigma))
        {
            Assert.True(Environment.GetEnvironmentVariable("SCORESTUDIO_REQUIRE_DENIGMA") != "1", $"denigma was required but not found at '{denigma}'");
            return;
        }

        var result = MusxImporter.ImportFile(Path.Combine(MusxSamples, sample), denigma);
        Assert.NotEmpty(result.Project.Parts);
        Assert.True(result.Summary.Notes > 0, $"{sample}: no notes");
    }
}
