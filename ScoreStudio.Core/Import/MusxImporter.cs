using System.Diagnostics;
using System.Text;
using ScoreStudio.Services;

namespace ScoreStudio.Import;

/// <summary>Where denigma, the program that turns Finale .musx files into MusicXML, may be (docs/MUSX_IMPORT.md).</summary>
public static class DenigmaLocator
{
    public const string EnvironmentVariable = "SCORESTUDIO_DENIGMA";

    /// <summary>The folder next to ScoreStudio.exe that the Windows build ships denigma in.</summary>
    public static readonly string BundledFolder = Path.Combine("tools", "denigma");

    /// <summary>The places to look, in order: the path the user chose, SCORESTUDIO_DENIGMA, the program's own folder, the PATH.</summary>
    public static IEnumerable<string> Candidates(string? chosen, string programFolder, Func<string, string?> environment)
    {
        if (!string.IsNullOrWhiteSpace(chosen)) yield return chosen;
        if (environment(EnvironmentVariable) is { Length: > 0 } variable) yield return variable;
        yield return Path.Combine(programFolder, BundledFolder, "denigma.exe");
        yield return Path.Combine(programFolder, BundledFolder, "denigma");
        foreach (var directory in (environment("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return Path.Combine(directory, "denigma.exe");
            yield return Path.Combine(directory, "denigma");
        }
    }

    /// <summary>The first candidate that exists, or null.</summary>
    public static string? Find(string? chosen, string programFolder, Func<string, string?> environment, Func<string, bool> exists) =>
        Candidates(chosen, programFolder, environment).FirstOrDefault(exists);

    public static string? Find(string? chosen) => Find(chosen, AppContext.BaseDirectory, Environment.GetEnvironmentVariable, File.Exists);

    public const string ProjectPage = "https://github.com/rpatters1/denigma";
}

/// <summary>
/// Opens a Finale .musx file: denigma turns a copy of it into MusicXML in a work folder, and <see cref="MusicXmlImporter"/>
/// reads that (docs/MUSX_IMPORT.md). The original file is only read.
/// </summary>
public static class MusxImporter
{
    public const long MaxSourceBytes = MusicXmlImporter.MaxSourceBytes;

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Shown first among the warnings of every .musx import.</summary>
    public const string ConverterNote = "Finale 파일을 denigma로 MusicXML로 바꿔 가져옴 (denigma의 MusicXML 변환은 아직 개발 중이라 빠진 기호가 있을 수 있음)";

    public static ImportResult ImportFile(string path, string denigma, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(denigma);
        CheckSource(path);
        if (!File.Exists(denigma)) throw new ProjectFileException($"Finale 파일을 바꾸는 프로그램(denigma)을 찾을 수 없습니다.\n{denigma}");

        var work = Path.Combine(Path.GetTempPath(), "ScoreStudioMusx", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            // ASCII names inside the work folder, so the converter never meets a path it cannot spell.
            var copy = Path.Combine(work, "source.musx");
            File.Copy(path, copy);
            var output = Path.Combine(work, "score.musicxml");
            var (exitCode, log) = Run(denigma, new[] { "export", copy, "--musicxml", output }, work, timeout ?? DefaultTimeout);
            if (!File.Exists(output))
            {
                var detail = string.Join("\n", log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(8));
                throw new ProjectFileException($"Finale 파일을 MusicXML로 바꾸지 못했습니다(denigma 종료 코드 {exitCode}).\n{Path.GetFileName(path)}"
                    + (detail.Length > 0 ? $"\n\n{detail}" : ""));
            }

            var result = MusicXmlImporter.ImportFile(output);
            var project = result.Project;
            project.ImportedFrom = Path.GetFullPath(path);
            if (project.Title == Path.GetFileNameWithoutExtension(output)) project.Title = Path.GetFileNameWithoutExtension(path);
            var warnings = new List<ImportWarning> { new(ConverterNote, 1, Path.GetFileName(path)) };
            warnings.AddRange(result.Warnings);
            return new ImportResult { Project = project, Warnings = warnings, Summary = result.Summary };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ProjectFileException($"Finale 파일을 읽을 수 없습니다.\n{path}\n\n{ex.Message}", ex);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>A .musx file is a ZIP archive; anything else is refused before the converter runs.</summary>
    private static void CheckSource(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new ProjectFileException($"파일을 찾을 수 없습니다.\n{path}");
        if (info.Length == 0) throw new ProjectFileException("파일이 비어 있습니다.");
        if (info.Length > MaxSourceBytes)
            throw new ProjectFileException($"파일이 너무 큽니다({info.Length / 1024 / 1024}MB). {MaxSourceBytes / 1024 / 1024}MB까지 가져올 수 있습니다.");

        Span<byte> header = stackalloc byte[4];
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Read(header) != 4 || header[0] != 'P' || header[1] != 'K' || header[2] != 3 || header[3] != 4)
                throw new ProjectFileException("Finale .musx 파일이 아닙니다. (Finale 2014 이전의 .mus 파일은 Finale에서 .musx나 MusicXML로 저장해야 합니다.)");
        }
    }

    /// <summary>Runs the converter and returns its exit code and output; it is stopped when it runs out of time.</summary>
    private static (int ExitCode, string Log) Run(string executable, IEnumerable<string> arguments, string folder, TimeSpan timeout)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            WorkingDirectory = folder, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        var log = new StringBuilder();
        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (log) log.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (log) log.AppendLine(e.Data); };
        try
        {
            if (!process.Start()) throw new ProjectFileException("Finale 파일을 바꾸는 프로그램(denigma)을 실행하지 못했습니다.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new ProjectFileException($"Finale 파일을 바꾸는 프로그램(denigma)을 실행하지 못했습니다.\n{executable}\n\n{ex.Message}", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new ProjectFileException($"Finale 파일 변환이 {timeout.TotalMinutes:0}분 안에 끝나지 않아 멈췄습니다.");
        }

        process.WaitForExit();
        lock (log) return (process.ExitCode, log.ToString());
    }
}
