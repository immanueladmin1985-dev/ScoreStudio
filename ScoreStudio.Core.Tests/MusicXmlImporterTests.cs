using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using ScoreStudio.Import;
using ScoreStudio.Models;
using ScoreStudio.Services;

namespace ScoreStudio.Core.Tests;

public sealed class MusicXmlImporterTests : IDisposable
{
    private static readonly string SampleDirectory = Path.Combine(AppContext.BaseDirectory, "samples", "musicxml");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ScoreStudioImportTests", Guid.NewGuid().ToString("N"));

    public MusicXmlImporterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string Sample(string name) => Path.Combine(SampleDirectory, name);

    private static string Fraction(int numerator, int denominator) => $"{numerator}/{denominator}";

    private static string Describe(ScoreEvent e) =>
        $"{e.Kind} {(e.Kind == "Note" ? e.NotationSpelling + "(" + e.MidiPitch + ")" : "")} @{Fraction(e.PositionNumerator, e.PositionDenominator)} " +
        $"len {Fraction(e.DurationNumerator, e.DurationDenominator)} staff {e.Staff} voice {e.Voice}{(Verse1(e) is { } lyric ? " '" + lyric + "'" : "")}";

    private static string? Verse1(ScoreEvent e) => e.Lyrics.FirstOrDefault(l => l.Verse == 1)?.Text;

    [Fact]
    public void FinaleMarks_DynamicsWordsTempoHairpinsSlursAndArticulationsMatchTheSource()
    {
        var result = MusicXmlImporter.ImportFile(Sample("finale-marks.musicxml"));
        Assert.Empty(result.Warnings);
        var (m1, m2) = (result.Project.Parts[0].Measures[0], result.Project.Parts[0].Measures[1]);

        string Describe(ScoreDirection d) =>
            $"{d.Kind} staff {d.Staff} @{d.PositionNumerator}/{d.PositionDenominator} '{d.Text}' above {d.Above?.ToString() ?? "-"} y {d.Y?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}"
            + (d.Italic ? " italic" : "") + (d.Kind == DirectionKind.Tempo ? $" {d.BeatUnit}{new string('.', d.BeatUnitDots)}={d.PerMinute}" : "");
        Assert.Equal(new[]
        {
            "Words staff 0 @0/1 'dolce' above True y 3.5 italic",
            "Tempo staff 0 @0/1 '' above True y - quarter.=60",
            "Dynamic staff 0 @0/1 'mf' above False y -8",
        }, m1.Directions.Select(Describe));
        Assert.Equal(new[]
        {
            "Dynamic staff 0 @5/2 'sfz' above False y -",
            "Dynamic staff 1 @0/1 'sempre p' above False y -",
        }, m2.Directions.Select(Describe));

        string Span(ScoreHairpin h) =>
            $"{(h.Crescendo ? "cresc" : "dim")} staff {h.Staff} @{h.PositionNumerator}/{h.PositionDenominator} +{h.EndMeasureOffset} @{h.EndPositionNumerator}/{h.EndPositionDenominator} y {h.Y?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}";
        Assert.Equal("cresc staff 0 @2/1 +1 @0/1 y -7.5", Span(Assert.Single(m1.Hairpins)));
        Assert.Equal("dim staff 1 @0/1 +0 @4/1 y -", Span(Assert.Single(m2.Hairpins)));

        string Slur(ScoreSlur s) =>
            $"{s.Staff}/{s.Voice} @{s.PositionNumerator} → +{s.EndMeasureOffset} {s.EndStaff}/{s.EndVoice} @{s.EndPositionNumerator} above {s.Above?.ToString() ?? "-"}";
        Assert.Equal("0/1 @0 → +1 0/1 @0 above True", Slur(Assert.Single(m1.Slurs)));
        // The stop on the upper staff comes before the start on the lower staff in the file.
        Assert.Equal("1/5 @0 → +0 0/1 @2 above False", Slur(Assert.Single(m2.Slurs)));

        Assert.Equal(new[] { "Staccato", "Accent", "Marcato,Tenuto", "Staccato,Tenuto", "" },
            m1.Events.Select(e => string.Join(",", e.Articulations)));
        Assert.Equal(new[] { Articulation.Staccatissimo, Articulation.Fermata }, m2.Events[0].Articulations);
    }

    private static string OneMeasure(string attributes, string content) => $"""
        <score-partwise version="4.0">
          <part-list><score-part id="P1"><part-name>클라리넷</part-name></score-part></part-list>
          <part id="P1"><measure number="1">
            <attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time>{attributes}</attributes>
            {content}
            <note><pitch><step>D</step><octave>5</octave></pitch><duration>4</duration><voice>1</voice><type>whole</type></note>
          </measure></part>
        </score-partwise>
        """;

    [Fact]
    public void TransposingInstrument_IsKept_AndWrittenBack()
    {
        var xml = OneMeasure("<transpose><diatonic>-1</diatonic><chromatic>-2</chromatic></transpose>", "");
        var result = MusicXmlImporter.ImportXml(xml, "이조");
        Assert.Empty(result.Warnings);
        var transpose = Assert.IsType<ScoreTranspose>(result.Project.Parts[0].Transpose);
        Assert.Equal((-1, -2, 0), (transpose.Diatonic, transpose.Chromatic, transpose.OctaveChange));
        Assert.Equal("D5", result.Project.Parts[0].Measures[0].Events[0].NotationSpelling); // the written pitch

        var back = MusicXmlImporter.ImportXml(ScoreStudio.Export.MusicXmlExporter.ToXml(result.Project), "다시").Project.Parts[0].Transpose;
        Assert.Equal((-1, -2, 0), (back!.Diatonic, back.Chromatic, back.OctaveChange));
    }

    [Fact]
    public void GlissandoToAGraceNote_IsLeftOutWithOneWarning()
    {
        // As in denigma's glissando.musx: a slide from a main note to a grace note, and another starting on a grace note.
        var xml = OneMeasure("", """
            <note><pitch><step>C</step><octave>5</octave></pitch><duration>1</duration><voice>1</voice><type>quarter</type>
              <notations><slide type="start"/></notations></note>
            <note><grace/><pitch><step>E</step><octave>5</octave></pitch><voice>1</voice><type>eighth</type>
              <notations><slide type="stop"/></notations></note>
            <note><grace slash="yes"/><pitch><step>F</step><octave>5</octave></pitch><voice>1</voice><type>eighth</type>
              <notations><glissando type="start"/></notations></note>
            <note><pitch><step>G</step><octave>4</octave></pitch><duration>1</duration><voice>1</voice><type>quarter</type>
              <notations><glissando type="stop"/></notations></note>
            """).Replace("<duration>4</duration>", "<duration>2</duration>").Replace("<type>whole</type>", "<type>half</type>");
        var result = MusicXmlImporter.ImportXml(xml, "글리산도");

        Assert.Empty(result.Project.Parts[0].Measures[0].Glissandos);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(("꾸밈음에서 시작하거나 꾸밈음에서 끝나는 글리산도는 가져오지 않음", 2), (warning.Message, warning.Count));
    }

    [Fact]
    public void TempoTextWithAMetronomeNoteSymbol_BecomesOneText()
    {
        var xml = OneMeasure("", """
            <direction placement="above"><direction-type>
              <words font-weight="bold">Tempo (</words><symbol>metNoteQuarterUp</symbol><words>=120)</words>
            </direction-type></direction>
            """);
        var result = MusicXmlImporter.ImportXml(xml, "빠르기");

        Assert.Empty(result.Warnings);
        var direction = Assert.Single(result.Project.Parts[0].Measures[0].Directions);
        Assert.Equal(("Tempo (♩=120)", true), (direction.Text, direction.Bold));
    }

    [Fact]
    public void GraceNotes_GoToTheNoteAfterThem_OrAfterTheLastNoteOfTheMeasure()
    {
        var result = MusicXmlImporter.ImportFile(Sample("grace-notes.musicxml"));
        Assert.Empty(result.Warnings); // the slur from the grace note to its note is not warned about
        var (m1, m2) = (result.Project.Parts[0].Measures[0], result.Project.Parts[0].Measures[1]);

        string Graces(ScoreEvent e) => string.Join(" ", e.GraceNotes.Select(g =>
            $"{(g.Chord ? "+" : "")}{g.NotationSpelling}/{g.Type}{(g.Slash ? "/slash" : "")}{(g.After ? "/after" : "")}"));
        Assert.Equal(new[] { "D5/eighth/slash", "E5/16th F5/16th" }, m1.Events.Select(Graces));
        Assert.Equal("F#4/eighth +A4/eighth A4/16th/after B4/16th/after", Graces(Assert.Single(m2.Events)));

        // Grace notes take no time: the measures hold exactly their notes.
        Assert.Equal(new[] { (0, 1), (1, 1) }, m1.Events.Select(e => (e.PositionNumerator, e.DurationNumerator)));
        Assert.Empty(m1.Slurs);
    }

    [Fact]
    public void Ornaments_Tremolos_AndLetRingTies_AreKept()
    {
        var result = MusicXmlImporter.ImportFile(Sample("ornaments.musicxml"));
        Assert.Empty(result.Warnings);
        var measures = result.Project.Parts[0].Measures;

        // A two-note tremolo keeps its notes' real lengths (a quarter each, written as halves).
        Assert.Equal(new[] { (3, 0, 1), (0, 0, 1), (0, 0, 2) }, measures[0].Events.Select(e => (e.TremoloToNext, e.Tremolo, e.DurationNumerator)));
        Assert.Equal(new[] { Articulation.Trill }, measures[0].Events[2].Articulations);
        Assert.Equal(3, measures[1].Events[0].Tremolo);
        Assert.Equal(new[] { "", "Turn", "InvertedMordent", "InvertedTurn,Mordent" }, measures[1].Events.Select(e => string.Join(",", e.Articulations)));
        Assert.Equal(new[] { true, false }, measures[2].Events.Select(e => e.LetRing));
    }

    [Fact]
    public void Arpeggios_Techniques_RehearsalMarks_Glissandos_AndLines_AreKept()
    {
        var result = MusicXmlImporter.ImportFile(Sample("finale-marks-2.musicxml"));
        Assert.Empty(result.Warnings);
        var measures = result.Project.Parts[0].Measures;

        Assert.Equal(new[] { ArpeggioKind.Wavy, ArpeggioKind.Up, ArpeggioKind.NonArpeggio, ArpeggioKind.None },
            measures[0].Events.Where(e => e.Kind == "Note").GroupBy(e => e.PositionNumerator).Select(g => g.Max(e => e.Arpeggio)));
        Assert.Equal(new[] { Articulation.Harmonic }, measures[0].Events[^1].Articulations);
        Assert.Equal(new[] { "DownBow", "UpBow", "", "" }, measures[1].Events.Select(e => string.Join(",", e.Articulations)));

        Assert.Equal(new[] { ("A", true), ("Coda", false) },
            measures.SelectMany(m => m.Directions).Where(d => d.Kind == DirectionKind.Rehearsal).Select(d => (d.Text, d.Boxed)));

        var gliss = Assert.Single(measures[1].Glissandos);
        Assert.Equal((true, false, "gliss.", 2, 0, 3), (gliss.Wavy, gliss.Slide, gliss.Text, gliss.PositionNumerator, gliss.EndMeasureOffset, gliss.EndPositionNumerator));
        var slide = Assert.Single(measures[2].Glissandos);
        Assert.Equal((false, true, 0, 1), (slide.Wavy, slide.Slide, slide.PositionNumerator, slide.EndPositionNumerator));

        var bracket = Assert.Single(measures[2].Lines);
        Assert.Equal((LineKind.Bracket, true, -1, -1, 0, 3, 1), (bracket.Kind, bracket.Above, bracket.StartHook, bracket.EndHook, bracket.PositionNumerator, bracket.EndPositionNumerator, bracket.EndPositionDenominator));
        Assert.Equal(new[] { (LineKind.Dashes, (bool?)false, 0, 3), (LineKind.TrillLine, null, 0, 2) },
            measures[3].Lines.OrderBy(l => l.Kind).Select(l => (l.Kind, l.Above, l.PositionNumerator, l.EndPositionNumerator)));
        Assert.Contains(Articulation.Trill, measures[3].Events[0].Articulations);
    }

    [Fact]
    public void PercussionStaves_UnpitchedNotes_Noteheads_AndInstruments_AreKept()
    {
        var result = MusicXmlImporter.ImportFile(Sample("percussion.musicxml"));
        Assert.Empty(result.Warnings);
        var (drums, triangle) = (result.Project.Parts[0], result.Project.Parts[1]);

        Assert.Equal((ClefType.Percussion, 5), (drums.Staves[0].Clef, drums.Staves[0].Lines));
        Assert.Equal((ClefType.Percussion, 1), (triangle.Staves[0].Clef, triangle.Staves[0].Lines));

        // Instruments are numbered in order; a note sounds its instrument's MIDI key (MusicXML counts from 1).
        Assert.Equal(new[] { ("I1", "Bass Drum 1", 36), ("I2", "Acoustic Snare", 39), ("I3", "Closed Hi-Hat", 43), ("I4", "Crash Cymbal 1", 50), ("I5", "High Floor Tom", 44) },
            drums.Instruments.Select(i => (i.Id, i.Name, i.MidiUnpitched ?? 0)));
        Assert.All(drums.Instruments, i => Assert.Equal(10, i.MidiChannel));

        var notes = drums.Measures[0].Events;
        Assert.All(notes, e => Assert.True(e.Unpitched));
        Assert.Equal(new[] { "G5 I3 X 42", "F4 I1 Normal 35", "G5 I3 X 42", "G5 I3 X 42", "C5 I2 Normal 38", "G5 I3 X 42", "A5 I4 CircleX 49", "F4 I1 Normal 35", "A4 I5 Normal 43" },
            notes.Select(e => $"{e.NotationSpelling} {e.Instrument} {e.Notehead} {e.MidiPitch}"));
        Assert.Equal(NoteheadShape.Diamond, drums.Measures[1].Events[0].Notehead);

        // On the one-line staff a note without a place sits on the middle line.
        Assert.Equal(new[] { "B4 I1 Triangle", "B4 I2 Normal" },
            triangle.Measures[0].Events.Where(e => e.Kind == "Note").Select(e => $"{e.NotationSpelling} {e.Instrument} {e.Notehead}"));
    }

    [Fact]
    public void PitchedNotesInAPercussionClef_AreKeptAsUnpitched_WithAWarning()
    {
        // As denigma writes a Finale drum staff: pitches that are not the places the source shows.
        var xml = OneMeasure("<clef><sign>percussion</sign><line>3</line></clef>", "").Replace("<step>D</step>", "<step>D</step><alter>1</alter>")
            .Replace("<type>whole</type>", "<type>whole</type><notehead smufl=\"noteheadTriangleUpBlack\">other</notehead>");
        var result = MusicXmlImporter.ImportXml(xml, "드럼");

        var note = Assert.Single(result.Project.Parts[0].Measures[0].Events);
        Assert.Equal((true, "D5", NoteheadShape.Triangle), (note.Unpitched, note.NotationSpelling, note.Notehead));
        Assert.Equal("타악기 음자리표 보표의 음표가 음높이로 적혀 있어 위치가 원본과 다를 수 있음", Assert.Single(result.Warnings).Message);
    }

    [Fact]
    public void UnknownNoteheadShapes_AndUnsupportedStaffLines_AreWarned()
    {
        var xml = OneMeasure("<staff-details><staff-lines>6</staff-lines></staff-details>", "")
            .Replace("<type>whole</type>", "<type>whole</type><notehead>do</notehead>");
        var result = MusicXmlImporter.ImportXml(xml, "모양");

        Assert.Equal(5, result.Project.Parts[0].Staves[0].Lines);
        Assert.Equal(NoteheadShape.Normal, result.Project.Parts[0].Measures[0].Events[0].Notehead);
        Assert.Equal(new[] { "음표머리 모양 <do>은(는) 보통 머리로 표시함", "지원하지 않는 보표 줄 수(6줄 이상 등)는 5줄로 표시함" },
            result.Warnings.Select(w => w.Message).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Pedals_Segno_AndCoda_AreKept()
    {
        var result = MusicXmlImporter.ImportFile(Sample("pedal-segno-coda.musicxml"));
        Assert.Empty(result.Warnings);
        var measures = result.Project.Parts[0].Measures;

        var signs = measures.SelectMany(m => m.Directions).Where(d => d.Kind is DirectionKind.Segno or DirectionKind.Coda)
            .Select(d => (d.Kind, d.Staff, d.PositionNumerator)).ToList();
        Assert.Equal(new[] { (DirectionKind.Segno, 0, 0), (DirectionKind.Coda, 0, 4) }, signs); // the coda from the right barline
        Assert.Contains(measures[2].Directions, d => d.Kind == DirectionKind.Words && d.Text == "D.S. al Coda");

        var pedals = measures.Select((m, i) => m.Lines.Select(l => (Measure: i, Line: l))).SelectMany(x => x).ToList();
        Assert.All(pedals, p => Assert.Equal((LineKind.Pedal, 1, (bool?)false), (p.Line.Kind, p.Line.Staff, p.Line.Above)));
        Assert.Equal(new[] { "0: 0→0/3 sign", "1: 0→1/2 line changes 0/2", "2: 2→1/3 sign sost" },
            pedals.Select(p => $"{p.Measure}: {p.Line.PositionNumerator}→{p.Line.EndMeasureOffset}/{p.Line.EndPositionNumerator}"
                + (p.Line.PedalSign ? " sign" : "") + (p.Line.PedalLine ? " line" : "") + (p.Line.Sostenuto ? " sost" : "")
                + (p.Line.PedalChanges.Count > 0 ? " changes " + string.Join(",", p.Line.PedalChanges.Select(c => $"{c.MeasureOffset}/{c.PositionNumerator}")) : "")));
    }

    [Fact]
    public void UnendedPedals_AndPedalSignsWithoutLineOrSign_FollowMusicXmlRules()
    {
        // Without a sign attribute a pedal shows signs unless it is a line; a pedal that never comes up is dropped.
        var xml = OneMeasure("", """
            <direction><direction-type><pedal type="start"/></direction-type></direction>
            <direction><direction-type><pedal type="stop"/></direction-type><offset>2</offset></direction>
            <direction><direction-type><pedal type="start" line="yes" number="2"/></direction-type></direction>
            <direction><direction-type><pedal type="discontinue"/></direction-type></direction>
            """);
        var result = MusicXmlImporter.ImportXml(xml, "페달");

        var pedal = Assert.Single(result.Project.Parts[0].Measures[0].Lines);
        Assert.Equal((true, false, 2), (pedal.PedalSign, pedal.PedalLine, pedal.EndPositionNumerator));
        Assert.Equal(new[] { "끝나지 않은 페달은 가져오지 않음", "페달 중단·재개(discontinue·resume) 표시는 가져오지 않음" },
            result.Warnings.Select(w => w.Message).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Fingerings_MoreMarks_AccidentalMarks_BuzzRolls_AndBarlineFermatas_AreKept()
    {
        var result = MusicXmlImporter.ImportFile(Sample("finale-marks-3.musicxml"));
        Assert.Empty(result.Warnings);
        var m = result.Project.Parts[0].Measures;

        Assert.Equal(new[] { "1 above", "1 ", "2 ", "5 " }, m[0].Events.Take(4).Select(e => $"{string.Join("-", e.Fingerings)} {(e.FingeringAbove == true ? "above" : "")}"));
        Assert.Equal(new[] { "4", "1" }, m[4].Events[0].Fingerings);
        Assert.False(m[4].Events[0].FingeringAbove);

        Assert.Equal(new[] { Articulation.Stress, Articulation.Unstress, Articulation.SoftAccent, Articulation.BreathMark, Articulation.Caesura, Articulation.Doit,
                Articulation.Falloff, Articulation.Plop, Articulation.Scoop, Articulation.Schleifer, Articulation.Haydn, Articulation.Shake, Articulation.Trill },
            m.SelectMany(x => x.Events).SelectMany(e => e.Articulations));
        Assert.Equal(1, m[3].Events[2].AccidentalMark);   // the trill's sharp
        Assert.Equal(-1, m[4].Events[1].AccidentalMark);  // an editorial flat
        Assert.True(m[3].Events[3].BuzzRoll);
        Assert.Equal(new[] { false, false, false, true, false }, m.Select(x => x.BarlineFermata));
    }

    [Fact]
    public void FermataOnALeftBarline_BelongsToTheMeasureBefore()
    {
        var xml = OneMeasure("", "").Replace("</measure></part>", """
            </measure><measure number="2"><barline location="left"><fermata/></barline>
            <note><rest/><duration>4</duration><voice>1</voice><type>whole</type></note></measure></part>
            """);
        var result = MusicXmlImporter.ImportXml(xml, "페르마타");
        Assert.Empty(result.Warnings);
        Assert.Equal(new[] { true, false }, result.Project.Parts[0].Measures.Select(x => x.BarlineFermata));
    }

    [Fact]
    public void CueNotes_AndFiguredBass_AreKept()
    {
        var result = MusicXmlImporter.ImportFile(Sample("cue-figured-bass.musicxml"));
        Assert.Empty(result.Warnings);
        var (solo, bass) = (result.Project.Parts[0].Measures, result.Project.Parts[1].Measures);

        Assert.Equal(new[] { "cue", "cue", "cue", "" }, solo[0].Events.Select(e => e.Cue ? "cue" : e.CueSize ? "size" : ""));
        Assert.Equal(new[] { "", "size", "" }, solo[1].Events.Select(e => e.Cue ? "cue" : e.CueSize ? "size" : ""));

        string Describe(ScoreFiguredBass f) => $"{f.PositionNumerator}/{f.PositionDenominator} {f.DurationNumerator}{(f.Parentheses ? " ()" : "")}: "
            + string.Join(" ", f.Figures.Select(x => $"[{x.Prefix}|{x.Number}|{x.Suffix}{(x.Extend ? "|_" : "")}]"));
        Assert.Equal(new[] { "1/1 0: [|6|]", "2/1 1: [|6||_] [|4||_]" }, bass[0].FiguredBass.Select(Describe));
        Assert.Equal(new[] { "0/1 0: [|7|backslash] [sharp||]", "1/1 0 (): [natural|3|]" }, bass[1].FiguredBass.Select(Describe));
        Assert.All(bass.SelectMany(m => m.FiguredBass), f => Assert.Equal(0, f.Staff));
    }

    [Fact]
    public void TimeSymbols_OtherKeys_SenzaMisura_AndTranspositionChanges_AreKept()
    {
        var result = MusicXmlImporter.ImportFile(Sample("time-key-transpose.musicxml"));
        Assert.Empty(result.Warnings);
        var part = result.Project.Parts[0];
        var m = part.Measures;

        Assert.Equal(new[] { "4/4 Common", "2/2 Cut", "5/8 Normal 3+2", "5/8 Normal 3+2 senza", "3/4 SingleNumber" },
            m.Select(x => $"{x.Numerator}/{x.Denominator} {x.TimeSymbol}{(x.BeatsText.Length > 0 ? " " + x.BeatsText : "")}{(x.SenzaMisura ? " senza" : "")}"));
        Assert.Equal(new[] { "F1,B-1", "", "", "", "" }, m.Select(x => string.Join(",", x.KeyAccidentals.Select(k => k.Step + k.Alter))));
        Assert.Equal(new[] { 0, 0, 0, 0, 1 }, m.Select(x => x.KeyFifths));

        // The B flat clarinet changes to A in measure 5.
        Assert.Equal((-1, -2), (part.Transpose!.Diatonic, part.Transpose.Chromatic));
        Assert.Equal(new[] { false, false, false, false, true }, m.Select(x => x.Transpose is not null));
        Assert.Equal((-2, -3), (m[4].Transpose!.Diatonic, m[4].Transpose!.Chromatic));
    }

    [Theory]
    [InlineData(9, "F2,C2,G1,D1,A1,E1,B1")]
    [InlineData(-10, "B-2,E-2,A-2,D-1,G-1,C-1,F-1")]
    public void KeysOfMoreThanSevenSharpsOrFlats_BecomeOtherKeysWithDoubles(int fifths, string expected)
    {
        var result = MusicXmlImporter.ImportXml(OneMeasure($"<key><fifths>{fifths}</fifths></key>", ""), "조표");
        Assert.Empty(result.Warnings);
        Assert.Equal(expected, string.Join(",", result.Project.Parts[0].Measures[0].KeyAccidentals.Select(k => k.Step + k.Alter)));
    }

    [Fact]
    public void ChordDiagrams_AndSymbols_AreKept()
    {
        var result = MusicXmlImporter.ImportFile(Sample("chord-diagrams-symbols.musicxml"));
        Assert.Empty(result.Warnings);
        var m = result.Project.Parts[0].Measures;

        var c = m[0].ChordSymbols.Single().Frame!;
        Assert.Equal((6, 4, (int?)null), (c.Strings, c.Frets, c.FirstFret));
        Assert.Equal(new[] { "5:3:3:", "4:2:2:", "3:0::", "2:1:1:", "1:0::" }, c.Notes.Select(n => $"{n.String}:{n.Fret}:{n.Fingering}:{n.Barre}"));
        var f = m[1].ChordSymbols.Single().Frame!;
        Assert.Equal(5, f.FirstFret);
        Assert.Equal(new[] { "start", "", "", "", "", "stop" }, f.Notes.Select(n => n.Barre));

        Assert.Equal(new[] { "keyboardPedalPed", "keyboardPedalUp", "repeat1Bar", "pictTriangle" },
            m.SelectMany(x => x.Directions).Where(d => d.Kind == DirectionKind.Symbol).Select(d => d.Text));
    }

    [Fact]
    public void SymbolsWithoutAKnownGlyph_AreKept_AndSaidNotToBeDrawn()
    {
        var xml = OneMeasure("", "<direction><direction-type><symbol>handbellsMalletBellOnTable</symbol></direction-type></direction>");
        var result = MusicXmlImporter.ImportXml(xml, "기호");
        Assert.Equal("handbellsMalletBellOnTable", Assert.Single(result.Project.Parts[0].Measures[0].Directions).Text);
        Assert.Equal("지시 기호 <handbellsMalletBellOnTable>은(는) 저장만 하고 그리지 않음", Assert.Single(result.Warnings).Message);
    }

    [Theory]
    [InlineData("G", 1, ClefType.Treble8va, '\uE053', 37)]
    [InlineData("F", -1, ClefType.Bass8vb, '\uE064', 11)]
    [InlineData("F", 1, ClefType.Bass8va, '\uE065', 25)]
    public void OctaveClefs_AreKept_WrittenBack_AndDrawn(string sign, int octave, ClefType expected, char glyph, int bottomLine)
    {
        var line = sign == "G" ? 2 : 4;
        var xml = OneMeasure($"<clef><sign>{sign}</sign><line>{line}</line><clef-octave-change>{octave}</clef-octave-change></clef>", "");
        var result = MusicXmlImporter.ImportXml(xml, "음자리표");
        Assert.Empty(result.Warnings);
        Assert.Equal(expected, result.Project.Parts[0].Staves[0].Clef);
        Assert.Equal(bottomLine, ScoreStudio.Layout.Notation.BottomLineDiatonic(expected)); // E5, G1, G3 counted in steps from C0

        var back = MusicXmlImporter.ImportXml(ScoreStudio.Export.MusicXmlExporter.ToXml(result.Project), "다시").Project;
        Assert.Equal(expected, back.Parts[0].Staves[0].Clef);
        Assert.Contains(ScoreStudio.Layout.ScoreLayoutEngine.Layout(result.Project).Pages[0].Items.OfType<ScoreStudio.Layout.GlyphItem>(), g => g.Glyph == glyph);
    }

    [Fact]
    public void HiddenNotesAndRests_AreKept_AndWrittenBackHidden()
    {
        // Finale fills a second layer with rests it does not print.
        var xml = OneMeasure("", """<note print-object="no"><rest/><duration>2</duration><voice>2</voice><type>half</type></note><backup><duration>2</duration></backup>""");
        var result = MusicXmlImporter.ImportXml(xml, "숨김");
        Assert.Empty(result.Warnings);
        Assert.Equal(new[] { false, true }, result.Project.Parts[0].Measures[0].Events.OrderBy(e => e.Voice).Select(e => e.Hidden)); // voice 2 is the hidden rest

        var written = ScoreStudio.Export.MusicXmlExporter.ToXml(result.Project);
        Assert.Contains("print-object=\"no\"", written);
        Assert.Equal(new[] { false, true }, MusicXmlImporter.ImportXml(written, "다시").Project.Parts[0].Measures[0].Events.OrderBy(e => e.Voice).Select(e => e.Hidden));
    }

    [Fact]
    public void SimpleMelody_TitlesKeyTimePitchesRhythmAndLyricsMatchTheSource()
    {
        var result = MusicXmlImporter.ImportFile(Sample("simple-melody.musicxml"));
        var project = result.Project;
        var part = Assert.Single(project.Parts);

        Assert.Equal(("도레미 노래", "ScoreStudio 샘플"), (project.Title, project.Composer));
        Assert.Equal(("노래", "Voice"), (part.Name, part.Instrument));
        Assert.Equal(ClefType.Treble, Assert.Single(part.Staves).Clef);
        Assert.Equal(new[] { 1, 2, 3, 4 }, part.Measures.Select(m => m.Number));
        Assert.All(part.Measures, m => Assert.Equal((3, 4, -1), (m.Numerator, m.Denominator, m.KeyFifths)));

        Assert.Equal(
            new[]
            {
                "Note F4(65) @0/1 len 1/1 staff 0 voice 1 '도'",
                "Note G4(67) @1/1 len 1/1 staff 0 voice 1 '레'",
                "Note A4(69) @2/1 len 1/1 staff 0 voice 1 '미'",
                "Note Bb4(70) @0/1 len 3/2 staff 0 voice 1 '파'",
                "Note A4(69) @3/2 len 1/2 staff 0 voice 1 '솔'",
                "Note G4(67) @2/1 len 1/1 staff 0 voice 1 '라'",
                "Rest  @0/1 len 1/1 staff 0 voice 1",
                "Note F4(65) @1/1 len 2/1 staff 0 voice 1 '시'",
                "Rest  @0/1 len 3/1 staff 0 voice 1",
            },
            part.Measures.SelectMany(m => m.Events).Select(Describe));

        Assert.Empty(result.Warnings);
        Assert.Equal((1, 4, 7, 2, 7), (result.Summary.Parts, result.Summary.Measures, result.Summary.Notes, result.Summary.Rests, result.Summary.Lyrics));
    }

    [Fact]
    public void PianoGrandStaff_StavesChordsBackupTripletsAndVoicesMatchTheSource()
    {
        var result = MusicXmlImporter.ImportFile(Sample("piano-grand-staff.musicxml"));
        var part = Assert.Single(result.Project.Parts);

        Assert.Equal("피아노 연습곡", result.Project.Title);
        Assert.Equal(new[] { ClefType.Treble, ClefType.Bass }, part.Staves.Select(s => s.Clef));
        Assert.All(part.Measures, m => Assert.Equal(2, m.KeyFifths));
        Assert.Equal(
            new[]
            {
                "Note D5(74) @0/1 len 1/1 staff 0 voice 1",
                "Note F#5(78) @1/1 len 1/1 staff 0 voice 1",
                "Note A5(81) @2/1 len 2/1 staff 0 voice 1",
                "Note D3(50) @0/1 len 4/1 staff 1 voice 5",
                "Note A3(57) @0/1 len 4/1 staff 1 voice 5",
            },
            part.Measures[0].Events.Select(Describe));
        Assert.Equal(
            new[]
            {
                "Note E5(76) @0/1 len 1/3 staff 0 voice 1",
                "Note F#5(78) @1/3 len 1/3 staff 0 voice 1",
                "Note G5(79) @2/3 len 1/3 staff 0 voice 1",
                "Note A5(81) @1/1 len 3/1 staff 0 voice 1",
                "Rest  @0/1 len 4/1 staff 1 voice 5",
            },
            part.Measures[1].Events.Select(Describe));

        // The triplet is kept as a span of its voice (it used to be a warning).
        Assert.Empty(result.Warnings);
        var tuplet = Assert.Single(part.Measures[1].Tuplets);
        Assert.Equal((0, 1, 0, 1, 1, 3, 2), (tuplet.Staff, tuplet.Voice, tuplet.PositionNumerator, tuplet.DurationNumerator, tuplet.DurationDenominator, tuplet.Actual, tuplet.Normal));
    }

    [Fact]
    public void UnsupportedFeatures_AreReportedByKindWithCounts_AndGraceNotesGoToTheirNote()
    {
        var result = MusicXmlImporter.ImportFile(Sample("unsupported-features.musicxml"));
        var warnings = result.Warnings.ToDictionary(w => w.Message, w => w.Count);

        Assert.Equal(
            new Dictionary<string, int>
            {
                ["지시 기호 <damp>"] = 1,
                ["장식음 기호 <vertical-turn>"] = 1,
                ["아티큘레이션 <other-articulation>"] = 1,
            },
            warnings);
        Assert.Equal("플루트 1마디", result.Warnings.First(w => w.Message == "지시 기호 <damp>").FirstLocation);

        var part = result.Project.Parts.Single();
        Assert.Equal(ClefType.Treble, part.Staves.Single().Clef);
        Assert.DoesNotContain(part.Measures.SelectMany(m => m.Events), e => e.NotationSpelling == "D5");
        // Since schema version 19 the grace note D5 is kept on the note after it, taking no time.
        Assert.Equal("D5", Assert.Single(part.Measures[0].Events[0].GraceNotes).NotationSpelling);
        Assert.Equal(new[] { "노", null, "래" }, part.Measures[0].Events.Select(Verse1));

        // Imported since schema version 17: dynamics, slurs, articulations and fermatas.
        Assert.Equal("p", Assert.Single(part.Measures[0].Directions).Text);
        var slur = Assert.Single(part.Measures[0].Slurs);
        Assert.Equal((0, 0, 3), (slur.PositionNumerator, slur.EndMeasureOffset, slur.EndPositionNumerator));
        Assert.Equal(new[] { Articulation.Staccato }, part.Measures[0].Events[1].Articulations);
        Assert.Equal(new[] { Articulation.Fermata, Articulation.Trill }, part.Measures[0].Events[2].Articulations);

        // Imported since schema version 4: ties, later verses, hyphens, chord symbols, repeats.
        var first = part.Measures[0].Events[0];
        Assert.True(first.TieToNext);
        Assert.False(part.Measures[0].Events[1].TieToNext);
        Assert.Equal(new[] { "1:노:Begin", "2:둘:Single" }, first.Lyrics.Select(l => $"{l.Verse}:{l.Text}:{l.Syllabic}"));
        Assert.Equal("C", Assert.Single(part.Measures[0].ChordSymbols).Text);
        Assert.Equal((true, false), (part.Measures[0].RepeatStart, part.Measures[0].RepeatEnd));
        Assert.Equal((false, true), (part.Measures[1].RepeatStart, part.Measures[1].RepeatEnd));

        // Imported since schema version 5: the bass clef at the start of measure 2.
        var clefChange = Assert.Single(part.Measures[1].ClefChanges);
        Assert.Equal((0, 0, ClefType.Bass), (clefChange.Staff, clefChange.PositionNumerator, clefChange.Clef));
        Assert.Equal(new[] { "0/1", "2/1", "3/1" }, part.Measures[0].Events.Select(e => Fraction(e.PositionNumerator, e.PositionDenominator)));
    }

    /// <summary>
    /// Counts straight from the XML, independently of the importer, and compares with what was imported.
    /// </summary>
    [Theory]
    [InlineData("simple-melody.musicxml")]
    [InlineData("piano-grand-staff.musicxml")]
    [InlineData("unsupported-features.musicxml")]
    [InlineData("hymn-repeats-chords.musicxml")]
    [InlineData("layout-features.musicxml")]
    [InlineData("beams.musicxml")]
    [InlineData("part-groups.musicxml")]
    [InlineData("page-layout.musicxml")]
    [InlineData("lyrics-ties.musicxml")]
    [InlineData("tuplets.musicxml")]
    public void ImportedCounts_MatchCountsTakenDirectlyFromTheSource(string file)
    {
        var source = XDocument.Load(Sample(file));
        var notes = source.Descendants("note").Where(n => n.Element("grace") is null && n.Element("cue") is null).ToList();
        var expected = (
            Parts: source.Root!.Elements("part").Count(),
            Measures: source.Root.Elements("part").Max(p => p.Elements("measure").Count()),
            Notes: notes.Count(n => n.Element("pitch") is not null),
            Rests: notes.Count(n => n.Element("rest") is not null),
            Lyrics: notes.Count(n => n.Elements("lyric").Any(l => l.Elements("text").Any(t => t.Value.Trim().Length > 0))),
            LyricSyllables: notes.Sum(n => n.Elements("lyric").Count(l => l.Elements("text").Any(t => t.Value.Trim().Length > 0))),
            Ties: notes.Count(n => n.Elements("tie").Any(t => (string?)t.Attribute("type") == "start")),
            Chords: source.Descendants("harmony").Count(h => h.Element("root") is not null),
            RepeatStarts: source.Descendants("repeat").Count(r => (string?)r.Attribute("direction") == "forward"),
            RepeatEnds: source.Descendants("repeat").Count(r => (string?)r.Attribute("direction") == "backward"),
            Voltas: source.Descendants("ending").Count(e => (string?)e.Attribute("type") == "start"),
            BeamGroups: notes.Count(n => n.Elements("beam").Any(b => (string?)b.Attribute("number") is null or "1" && b.Value == "begin")),
            Tuplets: source.Descendants("tuplet").Count(t => (string?)t.Attribute("type") == "start"));

        var result = MusicXmlImporter.ImportFile(Sample(file));
        var measures = result.Project.Parts.SelectMany(p => p.Measures).ToList();
        var events = measures.SelectMany(m => m.Events).ToList();

        Assert.Equal(
            (expected.Parts, expected.Measures, expected.Notes, expected.Rests, expected.Lyrics, expected.Ties, expected.Chords),
            (result.Summary.Parts, result.Summary.Measures, result.Summary.Notes, result.Summary.Rests, result.Summary.Lyrics, result.Summary.Ties, result.Summary.ChordSymbols));
        Assert.Equal(expected.Notes, events.Count(e => e.Kind == "Note"));
        Assert.Equal(expected.Rests, events.Count(e => e.Kind == "Rest"));
        Assert.Equal(expected.LyricSyllables, events.Sum(e => e.Lyrics.Count));
        Assert.Equal(expected.Ties, events.Count(e => e.TieToNext));
        Assert.Equal(expected.Chords, measures.Sum(m => m.ChordSymbols.Count));
        Assert.Equal(expected.BeamGroups, events.Count(e => e.Beams.FirstOrDefault(BeamType.End) == BeamType.Begin && e.Beams.Count > 0));
        Assert.Equal(expected.Tuplets, measures.Sum(m => m.Tuplets.Count));
        Assert.Equal(
            (expected.RepeatStarts, expected.RepeatEnds, expected.Voltas),
            (measures.Count(m => m.RepeatStart), measures.Count(m => m.RepeatEnd), measures.Count(m => m.Volta is not null)));
    }

    [Fact]
    public void Tuplets_AreReadAsSpansOfTheirVoice_WithRatioBracketNumberAndPlacement()
    {
        var result = MusicXmlImporter.ImportFile(Sample("tuplets.musicxml"));
        string Describe(ScoreTuplet t) =>
            $"{new Rational(t.PositionNumerator, t.PositionDenominator)}+{new Rational(t.DurationNumerator, t.DurationDenominator)} {t.Actual}:{t.Normal} "
            + $"{(t.Bracket is { } b ? b ? "bracket" : "plain" : "auto")} {t.ShowNumber} {(t.Above is { } a ? a ? "above" : "below" : "stem")}";

        var measures = result.Project.Parts[0].Measures;
        Assert.Equal(new[] { "0+1 3:2 auto Actual stem", "1+1 3:2 auto Actual stem", "2+2 3:2 auto Actual stem" }, measures[0].Tuplets.Select(Describe));
        // The nested triplet: the outer one over two beats and the inner one (3 in the time of 2 of the outer triplet's
        // eighths) over its first third. Both start on a note whose time modification (9:4) is their product.
        Assert.Equal(new[] { "0+1 3:2 plain Actual below", "1+1 3:2 auto Both stem", "2+2 3:2 bracket Actual stem", "2+2/3 3:2 auto Actual stem" },
            measures[1].Tuplets.Select(Describe).OrderBy(d => d, StringComparer.Ordinal));
        Assert.DoesNotContain(result.Warnings, w => w.Message.Contains("잇단음표"));

        // Unmatched or measure-crossing tuplets are dropped with a warning.
        var broken = MusicXmlImporter.ImportXml("""
            <score-partwise><part-list><score-part id="P1"/></part-list><part id="P1">
              <measure number="1"><attributes><divisions>3</divisions></attributes>
                <note><pitch><step>C</step><octave>5</octave></pitch><duration>1</duration><type>eighth</type>
                  <time-modification><actual-notes>3</actual-notes><normal-notes>2</normal-notes></time-modification>
                  <notations><tuplet type="stop" number="4"/></notations></note>
                <note><pitch><step>C</step><octave>5</octave></pitch><duration>1</duration><type>eighth</type>
                  <time-modification><actual-notes>3</actual-notes><normal-notes>2</normal-notes></time-modification>
                  <notations><tuplet type="start"/></notations></note>
              </measure>
              <measure number="2">
                <note><pitch><step>C</step><octave>5</octave></pitch><duration>1</duration><type>eighth</type>
                  <time-modification><actual-notes>3</actual-notes><normal-notes>2</normal-notes></time-modification>
                  <notations><tuplet type="stop"/></notations></note>
              </measure></part></score-partwise>
            """, "broken.musicxml");
        Assert.Empty(broken.Project.Parts[0].Measures.SelectMany(m => m.Tuplets));
        Assert.Contains(broken.Warnings, w => w.Message.Contains("시작이 없는 잇단음표"));
        Assert.Contains(broken.Warnings, w => w.Message.Contains("마디를 넘는 잇단음표"));
    }

    [Fact]
    public void Mxl_WithContainerXml_IsImported()
    {
        var path = Path.Combine(_dir, "with-container.mxl");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(Sample("simple-melody.musicxml"), "score/melody.musicxml");
            using var writer = new StreamWriter(zip.CreateEntry("META-INF/container.xml").Open());
            writer.Write("""<?xml version="1.0"?><container><rootfiles><rootfile full-path="score/melody.musicxml"/></rootfiles></container>""");
        }

        var result = MusicXmlImporter.ImportFile(path);

        Assert.Equal("도레미 노래", result.Project.Title);
        Assert.Equal(7, result.Summary.Notes);
    }

    [Fact]
    public void Mxl_WithoutContainerXml_UsesTheFirstScoreFile()
    {
        var path = Path.Combine(_dir, "no-container.mxl");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            zip.CreateEntryFromFile(Sample("piano-grand-staff.musicxml"), "piano.xml");

        Assert.Equal("피아노 연습곡", MusicXmlImporter.ImportFile(path).Project.Title);
    }

    [Fact]
    public void Hymn_VersesTiesChordSymbolsRepeatsAndVoltasMatchTheSource()
    {
        var result = MusicXmlImporter.ImportFile(Sample("hymn-repeats-chords.musicxml"));
        var measures = result.Project.Parts.Single().Measures;

        Assert.Empty(result.Warnings);
        Assert.Equal((7, 2, 1, 8), (result.Summary.Lyrics, result.Summary.LyricVerses, result.Summary.Ties, result.Summary.ChordSymbols));

        // Lyrics: two verses, a hyphenated word across the tie, an extender in verse 2.
        Assert.Equal(
            new[] { "1:주:Single 2:나:Single", "1:님:Single 2:의:Single", "1:사:Begin 2:삶:Single~", "", "1:랑:End", "1:해:Single", "1:요:Single", "", "1:멘:Single" },
            measures.SelectMany(m => m.Events).Select(e =>
                string.Join(" ", e.Lyrics.Select(l => $"{l.Verse}:{l.Text}:{l.Syllabic}{(l.Extend ? "~" : "")}"))));

        // The tie starts on the B4 at the end of measure 1 and nowhere else.
        var tied = Assert.Single(measures.SelectMany(m => m.Events), e => e.TieToNext);
        Assert.Equal(("B4", 1), (tied.NotationSpelling, measures.FindIndex(m => m.Events.Contains(tied)) + 1));

        Assert.Equal(
            new[] { "1:G@0/1", "1:D/F#@2/1", "2:Em7@0/1", "2:CM7@2/1", "3:Am7b5@0/1", "3:Gsus4@2/1", "3:N.C.@3/1", "4:Gadd9@0/1" },
            measures.SelectMany(m => m.ChordSymbols.Select(c => $"{m.Number}:{c.Text}@{c.PositionNumerator}/{c.PositionDenominator}")));
        var slash = measures[0].ChordSymbols[1];
        Assert.Equal(("D", 0, "major", "F", 1), (slash.RootStep, slash.RootAlter, slash.Kind, slash.BassStep, slash.BassAlter));

        Assert.Equal(
            new[] { "start", "end×2", "", "" },
            measures.Select(m => string.Join(",", new[] { m.RepeatStart ? "start" : null, m.RepeatEnd ? $"end×{m.RepeatTimes}" : null }.Where(x => x is not null))));
        Assert.Null(measures[0].Volta);
        Assert.Equal(("1", "1.", 1, false), (measures[1].Volta!.Numbers, measures[1].Volta!.Text, measures[1].Volta!.MeasureCount, measures[1].Volta!.OpenEnd));
        Assert.Equal(("2", "2.", 1, true), (measures[2].Volta!.Numbers, measures[2].Volta!.Text, measures[2].Volta!.MeasureCount, measures[2].Volta!.OpenEnd));
        Assert.Null(measures[3].Volta);
    }

    [Fact]
    public void LayoutFeatures_ClefChangesHiddenStavesBreaksAndOctaveLinesMatchTheSource()
    {
        var result = MusicXmlImporter.ImportFile(Sample("layout-features.musicxml"));
        var (melody, lower) = (result.Project.Parts[0], result.Project.Parts[1]);

        Assert.Empty(result.Warnings);
        Assert.Equal(("멜로디", "파트 2"), (melody.Name, lower.Name));

        // The first clef is the staff's clef; later ones are changes at the time they appear in the measure.
        Assert.Equal(ClefType.Treble, melody.Staves[0].Clef);
        Assert.Equal(
            new[] { "2: 0/1 Bass", "3: 0/1 Treble", "4: 2/1 Bass" },
            melody.Measures.SelectMany(m => m.ClefChanges.Select(c =>
                $"{m.Number}: {Fraction(c.PositionNumerator, c.PositionDenominator)} {c.Clef}")));
        Assert.All(lower.Measures, m => Assert.Empty(m.ClefChanges));

        // The 8va line (octave-shift type "down") covers both voices of measure 3 and stops before measure 4.
        // Pitches stay as sounded; OctaveShift only says how they are written.
        var m3 = melody.Measures[2].Events;
        Assert.Equal(new[] { 84, 86, 88, 89, 81 }, m3.Select(e => e.MidiPitch));
        Assert.Equal(new[] { "C6", "D6", "E6", "F6", "A5" }, m3.Select(e => e.NotationSpelling));
        Assert.All(m3, e => Assert.Equal(1, e.OctaveShift));
        Assert.All(melody.Measures.Where(m => m.Number != 3).SelectMany(m => m.Events), e => Assert.Equal(0, e.OctaveShift));

        Assert.Equal(new[] { "1:0", "2:0", "3:", "4:", "5:" }, lower.Measures.Select(m => $"{m.Number}:{string.Join(",", m.HiddenStaves)}"));
        Assert.All(melody.Measures, m => Assert.Empty(m.HiddenStaves));

        Assert.Equal(new[] { 3, 5 }, melody.Measures.Where(m => m.SystemBreak).Select(m => m.Number));
        Assert.Equal(new[] { 5 }, melody.Measures.Where(m => m.PageBreak).Select(m => m.Number));
    }

    [Fact]
    public void ChordSymbols_KeepTheirHeight_AndHiddenDegreesAreNotPrinted()
    {
        // Finale writes "C2/E" as kind text "2" plus an added 2nd marked print-object="no".
        const string xml = """
            <score-partwise>
              <part-list><score-part id="P1"><part-name>A</part-name></score-part></part-list>
              <part id="P1"><measure number="1"><attributes><divisions>1</divisions></attributes>
                <harmony default-y="26"><root><root-step>C</root-step></root><kind text="2">major</kind>
                  <bass><bass-step>E</bass-step></bass>
                  <degree print-object="no"><degree-value>2</degree-value><degree-alter>0</degree-alter><degree-type>add</degree-type></degree></harmony>
                <note><pitch><step>C</step><octave>5</octave></pitch><duration>2</duration></note>
                <harmony><root><root-step>G</root-step></root><kind>major</kind>
                  <degree><degree-value>9</degree-value><degree-alter>0</degree-alter><degree-type>add</degree-type></degree></harmony>
                <note><pitch><step>D</step><octave>5</octave></pitch><duration>2</duration></note>
              </measure></part>
            </score-partwise>
            """;

        var symbols = MusicXmlImporter.ImportXml(xml, "chords").Project.Parts[0].Measures[0].ChordSymbols;

        Assert.Equal(new[] { ("C2/E", (double?)2.6), ("Gadd9", null) }, symbols.Select(c => (c.Text, c.Y)));
    }

    [Fact]
    public void LyricPositions_AreReadFromDefaultY_AndPlacementAbove()
    {
        const string xml = """
            <score-partwise>
              <defaults><scaling><millimeters>7</millimeters><tenths>40</tenths></scaling><lyric-font font-family="Finale Lyrics" font-size="11.3"/></defaults>
              <part-list><score-part id="P1"><part-name>A</part-name></score-part></part-list>
              <part id="P1"><measure number="1"><attributes><divisions>1</divisions></attributes>
                <note><pitch><step>C</step><octave>5</octave></pitch><duration>1</duration>
                  <lyric number="1" default-y="-92"><text>가</text></lyric>
                  <lyric number="2" placement="above"><text>나</text></lyric>
                  <lyric number="3"><text>다</text></lyric></note>
                <note><pitch><step>D</step><octave>5</octave></pitch><duration>1</duration>
                  <lyric number="2"><extend type="stop"/></lyric></note>
              </measure></part>
            </score-partwise>
            """;

        var project = MusicXmlImporter.ImportXml(xml, "lyrics").Project;

        var lyrics = project.Parts[0].Measures[0].Events[0].Lyrics;
        Assert.Equal(new double?[] { -9.2, 2.5, null }, lyrics.Select(l => l.Y));
        Assert.Equal(new[] { 2 }, project.Parts[0].Measures[0].Events[1].ExtendEnds);
        Assert.Equal(11.3 * 25.4 / 72, project.Engraving!.LyricFontSize!.Value, 9);
        Assert.Equal("Finale Lyrics", project.Engraving.LyricFontFamily);
    }

    [Theory]
    [InlineData("312.5", 31.25)]
    [InlineData("0", null)]
    [InlineData("-40", null)]
    [InlineData("2500", null)]
    [InlineData("wide", null)]
    public void MeasureWidth_IsKeptInStaffSpaces(string width, double? expected)
    {
        var xml = $"""
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>S</part-name></score-part></part-list>
              <part id="P1"><measure number="1" width="{width}"><attributes><divisions>1</divisions></attributes>
                <note><pitch><step>C</step><octave>5</octave></pitch><duration>4</duration><type>whole</type></note></measure></part>
            </score-partwise>
            """;

        Assert.Equal(expected, MusicXmlImporter.ImportXml(xml, "width").Project.Parts[0].Measures[0].SourceWidth);
    }

    [Theory]
    [InlineData("Times New Roman", "Times New Roman")]
    [InlineData("'Adobe Garamond Pro', serif", "Adobe Garamond Pro")]
    [InlineData(" ", null)]
    public void LyricFont_TheFirstFamilyOfTheListIsKept(string attribute, string? expected)
    {
        var xml = $$"""
            <score-partwise version="4.0">
              <defaults><scaling><millimeters>7</millimeters><tenths>40</tenths></scaling><lyric-font font-family="{{attribute}}" font-size="12"/></defaults>
              <part-list><score-part id="P1"><part-name>S</part-name></score-part></part-list>
              <part id="P1"><measure number="1"><attributes><divisions>1</divisions></attributes>
                <note><pitch><step>C</step><octave>5</octave></pitch><duration>4</duration><type>whole</type></note></measure></part>
            </score-partwise>
            """;

        Assert.Equal(expected, MusicXmlImporter.ImportXml(xml, "font").Project.Engraving!.LyricFontFamily);
    }

    [Fact]
    public void PageLayout_StaffSizeMarginsAndDistancesMatchTheSource()
    {
        var result = MusicXmlImporter.ImportFile(Sample("page-layout.musicxml"));
        var project = result.Project;
        var engraving = project.Engraving!;
        const double mmPerTenth = 7.9551 / 40;

        Assert.Empty(result.Warnings);
        Assert.Equal(("A4", "Portrait"), (project.PageSize, project.Orientation));
        Assert.Equal(10 * mmPerTenth, engraving.StaffSpace, 9);
        Assert.All(new[] { engraving.MarginLeft, engraving.MarginRight, engraving.MarginTop, engraving.MarginBottom },
            m => Assert.Equal(101 * mmPerTenth, m, 9));
        Assert.Equal((7.2, 3.3, 8.0), (engraving.SystemDistance, engraving.TopSystemDistance, engraving.StaffDistance));

        // Per-system values, in staff spaces, stored on the measure that starts the system.
        var (upper, lower) = (project.Parts[0].Measures, project.Parts[1].Measures);
        Assert.Equal(new double?[] { 31.3, null, 10 }, upper.Select(m => m.TopSystemDistance));
        Assert.Equal(new double?[] { null, 17.4, null }, upper.Select(m => m.SystemDistance));
        Assert.Equal("0:14.5", string.Join(";", lower[1].StaffDistances.Select(d => $"{d.Staff}:{d.Distance}")));
        Assert.All(upper.Concat(lower).Where(m => m != lower[1]), m => Assert.Empty(m.StaffDistances));
    }

    [Theory]
    [InlineData(1100, 1405, "Letter", "Portrait", null)]
    [InlineData(1405, 1100, "Letter", "Landscape", null)]
    [InlineData(1056, 1494, "A4", "Portrait", null)]
    [InlineData(1290, 1820, "A4", "Portrait", "지원하지 않는 용지 크기(257×362mm)는 A4로 표시함")]
    public void PageSize_IsTakenFromTheSourcePage(int width, int height, string size, string orientation, string? warning)
    {
        var xml = $"""
            <score-partwise>
              <defaults><scaling><millimeters>7.9551</millimeters><tenths>40</tenths></scaling>
                <page-layout><page-height>{height}</page-height><page-width>{width}</page-width></page-layout></defaults>
              <part-list><score-part id="P1"><part-name>A</part-name></score-part></part-list>
              <part id="P1"><measure number="1"/></part>
            </score-partwise>
            """;

        var result = MusicXmlImporter.ImportXml(xml, "page");

        Assert.Equal((size, orientation), (result.Project.PageSize, result.Project.Orientation));
        Assert.Equal(warning, result.Warnings.SingleOrDefault()?.Message);
    }

    [Fact]
    public void WithoutScaling_TheProjectKeepsDefaultLayout()
    {
        var project = MusicXmlImporter.ImportFile(Sample("simple-melody.musicxml")).Project;

        Assert.Null(project.Engraving);
        Assert.Equal(("A4", "Portrait"), (project.PageSize, project.Orientation));
    }

    [Fact]
    public void PartGroupsAndHiddenNames_MatchTheSource()
    {
        var result = MusicXmlImporter.ImportFile(Sample("part-groups.musicxml"));

        Assert.Empty(result.Warnings);
        Assert.Equal(new[] { ("소프라노", true), ("알토", true), ("피아노", false) }, result.Project.Parts.Select(p => (p.Name, p.ShowName)));
        var group = Assert.Single(result.Project.PartGroups);
        Assert.Equal((0, 1, PartGroupSymbol.Bracket, true), (group.FirstPart, group.LastPart, group.Symbol, group.JoinBarlines));
    }

    [Fact]
    public void NestedAndUnclosedPartGroups_AreReadByNumber()
    {
        // Group 1 (brace, no barline) holds all four parts; group 2 (square, Mensurstrich) holds parts 2–3;
        // group 3 (no symbol) starts at part 4 and is never stopped.
        const string xml = """
            <score-partwise>
              <part-list>
                <part-group number="1" type="start"><group-symbol>brace</group-symbol><group-barline>no</group-barline></part-group>
                <score-part id="P1"><part-name>A</part-name></score-part>
                <part-group number="2" type="start"><group-symbol>square</group-symbol><group-barline>Mensurstrich</group-barline></part-group>
                <score-part id="P2"><part-name>B</part-name></score-part>
                <score-part id="P3"><part-name>C</part-name><part-name-display print-object="no"><display-text>C</display-text></part-name-display></score-part>
                <part-group number="2" type="stop"/>
                <part-group number="3" type="start"/>
                <score-part id="P4"><part-name>D</part-name></score-part>
                <part-group number="1" type="stop"/>
              </part-list>
              <part id="P1"><measure number="1"/></part>
              <part id="P2"><measure number="1"/></part>
              <part id="P3"><measure number="1"/></part>
              <part id="P4"><measure number="1"/></part>
            </score-partwise>
            """;

        var result = MusicXmlImporter.ImportXml(xml, "groups");

        Assert.Equal(
            new[] { (1, 2, PartGroupSymbol.Square, false), (0, 3, PartGroupSymbol.Brace, false), (3, 3, PartGroupSymbol.None, false) },
            result.Project.PartGroups.Select(g => (g.FirstPart, g.LastPart, g.Symbol, g.JoinBarlines)));
        Assert.Equal(new[] { true, true, false, true }, result.Project.Parts.Select(p => p.ShowName));
        Assert.Equal("끝나지 않은 파트 묶음은 마지막 파트까지로 맞춤", Assert.Single(result.Warnings).Message);
    }

    [Fact]
    public void Beams_AreStoredPerLevelAsInTheSource()
    {
        var result = MusicXmlImporter.ImportFile(Sample("beams.musicxml"));
        var measures = result.Project.Parts.Single().Measures;

        Assert.Empty(result.Warnings);
        string Beams(ScoreEvent e) => e.Beams.Count == 0 ? "-" : string.Join("+", e.Beams);
        Assert.Equal(
            new[] { "Begin", "End+BackwardHook", "Begin+ForwardHook", "End", "Begin+Begin", "Continue+Continue", "Continue+Continue", "End+End", "-", "-" },
            measures[1].Events.Select(Beams));
        Assert.Equal(new[] { "Begin", "Continue", "-", "-" }, measures[2].Events.Select(Beams)); // never ended: stored as written
        Assert.Equal(new[] { "Begin", "Continue", "Continue", "End" }, measures[4].Events.Take(4).Select(Beams));
    }

    [Theory]
    [InlineData("""<beam number="1">sideways</beam>""")]
    [InlineData("""<beam number="2">begin</beam>""")]
    [InlineData("""<beam number="1">begin</beam><beam number="1">end</beam>""")]
    public void UnreadableBeams_AreDroppedWithAWarning(string beams)
    {
        var xml = $"""
            <score-partwise>
              <part-list><score-part id="P1"><part-name>A</part-name></score-part></part-list>
              <part id="P1"><measure number="1"><attributes><divisions>2</divisions></attributes>
                <note><pitch><step>C</step><octave>5</octave></pitch><duration>1</duration>{beams}</note>
              </measure></part>
            </score-partwise>
            """;

        var result = MusicXmlImporter.ImportXml(xml, "beams");

        Assert.Empty(result.Project.Parts.Single().Measures.Single().Events.Single().Beams);
        Assert.Equal("읽을 수 없는 빔(음표 잇기) 정보는 무시함", Assert.Single(result.Warnings).Message);
    }

    [Fact]
    public void OctaveLineStop_IncludesTheNoteStartingAtTheStop_AsFinaleWritesIt()
    {
        // Measure 1: 8va from beat 2; the stop sits before the third quarter, which is still under the line.
        // Measure 2 (staff 2): an 8vb whose stop comes before the first chord of measure 3, as Finale writes a line
        // that ends on a downbeat.
        const string xml = """
            <score-partwise>
              <part-list><score-part id="P1"><part-name>A</part-name></score-part></part-list>
              <part id="P1">
                <measure number="1">
                  <attributes><divisions>1</divisions><staves>2</staves></attributes>
                  <note><pitch><step>C</step><octave>5</octave></pitch><duration>1</duration><staff>1</staff></note>
                  <direction><direction-type><octave-shift type="down" size="8"/></direction-type><staff>1</staff></direction>
                  <note><pitch><step>C</step><octave>6</octave></pitch><duration>1</duration><staff>1</staff></note>
                  <direction><direction-type><octave-shift type="stop" size="8"/></direction-type><staff>1</staff></direction>
                  <note><pitch><step>G</step><octave>6</octave></pitch><duration>1</duration><staff>1</staff></note>
                  <note><pitch><step>A</step><octave>5</octave></pitch><duration>1</duration><staff>1</staff></note>
                  <backup><duration>4</duration></backup>
                  <note><pitch><step>C</step><octave>3</octave></pitch><duration>4</duration><staff>2</staff></note>
                </measure>
                <measure number="2">
                  <note><rest/><duration>4</duration><staff>1</staff></note>
                  <backup><duration>4</duration></backup>
                  <direction><direction-type><octave-shift type="up" size="8"/></direction-type><staff>2</staff></direction>
                  <note><pitch><step>C</step><octave>1</octave></pitch><duration>4</duration><staff>2</staff></note>
                </measure>
                <measure number="3">
                  <note><rest/><duration>4</duration><staff>1</staff></note>
                  <backup><duration>4</duration></backup>
                  <direction><direction-type><octave-shift type="stop" size="8"/></direction-type><staff>2</staff></direction>
                  <note><pitch><step>C</step><octave>1</octave></pitch><duration>2</duration><staff>2</staff></note>
                  <note><pitch><step>C</step><octave>2</octave></pitch><duration>2</duration><staff>2</staff></note>
                </measure>
              </part>
            </score-partwise>
            """;

        var measures = MusicXmlImporter.ImportXml(xml, "octave").Project.Parts.Single().Measures;

        string Shifts(int measure, int staff) =>
            string.Join(",", measures[measure].Events.Where(e => e.Kind == "Note" && e.Staff == staff).Select(e => e.OctaveShift));
        Assert.Equal("0,1,1,0", Shifts(0, 0));
        Assert.Equal("0", Shifts(0, 1));
        Assert.Equal("-1", Shifts(1, 1));
        Assert.Equal("-1,0", Shifts(2, 1));
    }

    [Fact]
    public void VoltaSpanningSeveralMeasures_AndUnclosedVolta_AreMeasuredInMeasures()
    {
        const string xml = """
            <score-partwise>
              <part-list><score-part id="P1"><part-name>A</part-name></score-part></part-list>
              <part id="P1">
                <measure number="1"><barline location="left"><ending number="1, 2" type="start"/></barline></measure>
                <measure number="2"/>
                <measure number="3"><barline location="right"><ending number="1, 2" type="stop"/><repeat direction="backward" times="3"/></barline></measure>
                <measure number="4"><barline location="left"><ending number="3" type="start"/></barline></measure>
                <measure number="5"/>
              </part>
            </score-partwise>
            """;

        var result = MusicXmlImporter.ImportXml(xml, "voltas");
        var measures = result.Project.Parts.Single().Measures;

        Assert.Equal(("1, 2", "", 3, false), (measures[0].Volta!.Numbers, measures[0].Volta!.Text, measures[0].Volta!.MeasureCount, measures[0].Volta!.OpenEnd));
        Assert.Equal((true, 3), (measures[2].RepeatEnd, measures[2].RepeatTimes));
        Assert.Equal((2, true), (measures[3].Volta!.MeasureCount, measures[3].Volta!.OpenEnd));
        Assert.Equal("끝나지 않은 볼타(1·2번 괄호)는 마지막 마디까지로 맞춤", Assert.Single(result.Warnings).Message);
    }

    /// <summary>Five parts in Finale's style; <paramref name="chordsInPart1"/> decides whether part 1 has a chord symbol.</summary>
    private static string FinaleStyleParts(bool chordsInPart1) => $"""
        <score-partwise>
          <part-list>
            <score-part id="P1"><part-name print-object="no">MusicXML Part</part-name></score-part>
            <score-part id="P2"><part-name print-object="no">MusicXML Part</part-name></score-part>
            <score-part id="P3"><part-name>Clarinet in Bb</part-name>
              <part-name-display><display-text>Clarinet in B</display-text><accidental-text>flat</accidental-text></part-name-display></score-part>
            <score-part id="P4"><part-name>Voice</part-name></score-part>
            <score-part id="P5"><part-name>Voice</part-name></score-part>
          </part-list>
          <part id="P1"><measure number="1">{(chordsInPart1 ? "<harmony/>" : "")}</measure></part>
          <part id="P2"><measure number="1"/></part>
          <part id="P3"><measure number="1"/></part>
          <part id="P4"><measure number="1"/></part>
          <part id="P5"><measure number="1"><harmony/></measure></part>
        </score-partwise>
        """;

    [Fact]
    public void FinalePlaceholderPartNames_AreNumbered_AndDisplayNamesWin()
    {
        // Finale exports unnamed staves as "MusicXML Part"; part-name-display holds the printed name.
        var result = MusicXmlImporter.ImportXml(FinaleStyleParts(chordsInPart1: true), "finale");

        Assert.Equal(new[] { "파트 1", "파트 2", "Clarinet in B♭", "Voice", "Voice" }, result.Project.Parts.Select(p => p.Name));
        var chordWarning = Assert.Single(result.Warnings);
        Assert.Equal((2, "파트 1 1마디"), (chordWarning.Count, chordWarning.FirstLocation));

        // When names repeat, the location also says which part it is.
        var duplicates = MusicXmlImporter.ImportXml(FinaleStyleParts(chordsInPart1: false), "finale");
        Assert.Equal("5번째 파트(Voice) 1마디", Assert.Single(duplicates.Warnings).FirstLocation);
    }

    public static TheoryData<string, string> InvalidSources => new()
    {
        { "", "비어 있습니다" },
        { "이건 악보가 아닙니다", "XML 형식이 올바르지 않습니다" },
        { "<html><body/></html>", "MusicXML 악보 파일이 아닙니다" },
        { "<score-timewise><part-list/></score-timewise>", "timewise" },
        { "<score-partwise><part-list/></score-partwise>", "가져올 파트가 없습니다" },
        { "<score-partwise><part-list><score-part id='P1'/></part-list><part id='P2'/></score-partwise>", "파트 목록(part-list)에 없는 파트('P2')" },
        { "<score-partwise><part-list><score-part id='P1'/></part-list><part id='P1'><measure>", "XML 형식이 올바르지 않습니다" },
    };

    [Theory]
    [MemberData(nameof(InvalidSources))]
    public void InvalidFiles_ThrowKoreanErrors(string content, string expected)
    {
        var path = Path.Combine(_dir, "bad.musicxml");
        File.WriteAllText(path, content);

        var ex = Assert.Throws<ProjectFileException>(() => MusicXmlImporter.ImportFile(path));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void CorruptMxl_ThrowsKoreanError()
    {
        var path = Path.Combine(_dir, "broken.mxl");
        File.WriteAllBytes(path, new byte[] { (byte)'P', (byte)'K', 3, 4, 1, 2, 3, 4, 5, 6, 7, 8 });

        var ex = Assert.Throws<ProjectFileException>(() => MusicXmlImporter.ImportFile(path));

        Assert.Contains(".mxl", ex.Message);
    }

    [Fact]
    public void ExternalEntities_AreNeverRead()
    {
        var secret = Path.Combine(_dir, "secret.txt");
        File.WriteAllText(secret, "비밀");
        var xml = $"""
            <?xml version="1.0"?>
            <!DOCTYPE score-partwise [ <!ENTITY leak SYSTEM "{new Uri(secret).AbsoluteUri}"> ]>
            <score-partwise>
              <work><work-title>&leak;</work-title></work>
              <part-list><score-part id="P1"><part-name>A</part-name></score-part></part-list>
              <part id="P1"><measure number="1"/></part>
            </score-partwise>
            """;

        // With DTD processing ignored the entity is never expanded, so the parser either rejects it or leaves it empty.
        try
        {
            var result = MusicXmlImporter.ImportXml(xml, "fallback");
            Assert.DoesNotContain("비밀", result.Project.Title);
        }
        catch (ProjectFileException)
        {
        }
    }

    [Fact]
    public void Import_DoesNotChangeTheSourceFile()
    {
        var path = Path.Combine(_dir, "original.musicxml");
        File.Copy(Sample("piano-grand-staff.musicxml"), path);
        var before = File.ReadAllBytes(path);
        var lastWrite = File.GetLastWriteTimeUtc(path);

        MusicXmlImporter.ImportFile(path);

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(lastWrite, File.GetLastWriteTimeUtc(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(_dir));
    }

    [Theory]
    [InlineData("simple-melody.musicxml")]
    [InlineData("piano-grand-staff.musicxml")]
    [InlineData("unsupported-features.musicxml")]
    [InlineData("hymn-repeats-chords.musicxml")]
    [InlineData("layout-features.musicxml")]
    [InlineData("beams.musicxml")]
    [InlineData("part-groups.musicxml")]
    [InlineData("page-layout.musicxml")]
    [InlineData("lyrics-ties.musicxml")]
    [InlineData("percussion.musicxml")]
    [InlineData("finale-marks-2.musicxml")]
    [InlineData("finale-marks-3.musicxml")]
    [InlineData("chord-diagrams-symbols.musicxml")]
    [InlineData("time-key-transpose.musicxml")]
    [InlineData("cue-figured-bass.musicxml")]
    [InlineData("pedal-segno-coda.musicxml")]
    public void ImportedProject_SurvivesSaveAndReopen(string file)
    {
        var imported = MusicXmlImporter.ImportFile(Sample(file)).Project;
        var path = Path.Combine(_dir, Path.ChangeExtension(file, ".scoreproj"));

        ProjectFileService.Save(path, imported);
        var reopened = ProjectFileService.Load(path);

        Assert.Equal(JsonSerializer.Serialize(imported), JsonSerializer.Serialize(reopened));
        Assert.Equal(Sample(file), reopened.ImportedFrom);
    }
}
