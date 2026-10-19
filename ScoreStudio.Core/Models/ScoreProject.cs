using System.Text.Json.Serialization;

namespace ScoreStudio.Models;

/// <summary>
/// Music-domain data should remain independent from screen coordinates.
/// This first model is intentionally small and will grow with tested features.
/// </summary>
public sealed class ScoreProject
{
    /// <summary>The project file schema version this build reads and writes.</summary>
    public const int CurrentSchemaVersion = 20;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Title { get; set; } = "새 악보";
    public string Composer { get; set; } = "";
    public string PageSize { get; set; } = "A4";
    public string Orientation { get; set; } = "Portrait";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
    public string? ImportedFrom { get; set; }
    public List<ScorePart> Parts { get; set; } = new();

    /// <summary>Parts joined at the left of each system by a bracket or brace, and optionally by their barlines.</summary>
    public List<ScorePartGroup> PartGroups { get; set; } = new();

    /// <summary>Staff size, margins and spacing taken from the source (e.g. Finale's page layout); null uses defaults.</summary>
    public ScoreEngraving? Engraving { get; set; }

    /// <summary>
    /// Pictures of the original pages a recognised score came from, to compare with it (docs/OMR_COMPARE.md). They never
    /// change with edits, so undo snapshots leave them out.
    /// </summary>
    public List<ScoreSourcePage> SourcePages { get; set; } = new();

    public static ScoreProject CreateNew() => new()
    {
        Title = "새 악보",
        Parts = new List<ScorePart>
        {
            new()
            {
                Name = "파트 1",
                Measures = Enumerable.Range(1, 4).Select(n => new ScoreMeasure { Number = n }).ToList()
            }
        }
    };
}

public sealed class ScorePart
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "파트 1";
    public string Instrument { get; set; } = "Unknown";

    /// <summary>Whether the name is printed left of the first system. Finale exports often hide it.</summary>
    public bool ShowName { get; set; } = true;

    /// <summary>Staves from top to bottom. A piano part has two; most parts have one.</summary>
    public List<ScoreStaff> Staves { get; set; } = new() { new() };

    public List<ScoreMeasure> Measures { get; set; } = new();

    /// <summary>
    /// For a transposing instrument (e.g. clarinet in B♭): how its written notes differ from the sounding ones. The notes
    /// are kept and shown as written; this is carried so the score keeps sounding right in other programs. Null for none.
    /// </summary>
    public ScoreTranspose? Transpose { get; set; }
}

/// <summary>MusicXML's &lt;transpose&gt;: the steps and semitones to add to the written pitch to get the sounding pitch.</summary>
public sealed class ScoreTranspose
{
    /// <summary>Diatonic steps, e.g. −1 for an instrument in B♭.</summary>
    public int Diatonic { get; set; }

    /// <summary>Semitones, e.g. −2 for an instrument in B♭.</summary>
    public int Chromatic { get; set; }

    /// <summary>Whole octaves, e.g. −1 for a tenor saxophone besides its −1/−2.</summary>
    public int OctaveChange { get; set; }
}

/// <summary>
/// Page formatting from the source file. Distances between staves and systems are in staff spaces, so they scale with
/// the staff size; the staff size and margins are in millimetres.
/// </summary>
public sealed class ScoreEngraving
{
    /// <summary>Distance between two staff lines, in millimetres.</summary>
    public double StaffSpace { get; set; } = 1.75;

    public double MarginLeft { get; set; } = 15;
    public double MarginRight { get; set; } = 15;
    public double MarginTop { get; set; } = 15;
    public double MarginBottom { get; set; } = 15;

    /// <summary>From the bottom line of one system to the top line of the next, unless a system says otherwise.</summary>
    public double SystemDistance { get; set; } = 10;

    /// <summary>From the top margin to the top line of a page's first system, unless a system says otherwise.</summary>
    public double TopSystemDistance { get; set; }

    /// <summary>From the bottom line of one staff to the top line of the next within a system, unless a system says otherwise.</summary>
    public double StaffDistance { get; set; } = 8;

    /// <summary>
    /// A page size of its own in mm (both set), e.g. a recognised picture's; it is used instead of
    /// <see cref="ScoreProject.PageSize"/> and <see cref="ScoreProject.Orientation"/>.
    /// </summary>
    public double? PageWidth { get; set; }
    public double? PageHeight { get; set; }

    /// <summary>Lyric font size in millimetres (one em); null uses ScoreStudio's default.</summary>
    public double? LyricFontSize { get; set; }

    /// <summary>
    /// Lyric font family named by the source (the first of its list); null uses ScoreStudio's default. Lyrics in a known
    /// serif family are drawn and measured in a serif font, others in the sans-serif text font.
    /// </summary>
    public string? LyricFontFamily { get; set; }
}

/// <summary>A picture of an original page, and where it lies on a page of the score (to compare the two).</summary>
public sealed class ScoreSourcePage
{
    /// <summary>The score page it belongs to (from 0).</summary>
    public int Page { get; set; }

    /// <summary>The picture as a PNG file (a grayscale page as the recognition program read it).</summary>
    public byte[] Png { get; set; } = Array.Empty<byte>();

    /// <summary>Where the picture lies on the score page, in mm.</summary>
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>What it is, e.g. "악보.pdf 2쪽".</summary>
    public string Name { get; set; } = "";
}

/// <summary>A staff's distance from the staff above it in the system that starts at this measure.</summary>
public sealed class ScoreStaffDistance
{
    public int Staff { get; set; }

    /// <summary>In staff spaces, from the bottom line of the staff above to this staff's top line.</summary>
    public double Distance { get; set; }
}

/// <summary>Consecutive parts grouped on the score, e.g. the choir parts of an anthem.</summary>
public sealed class ScorePartGroup
{
    /// <summary>Index of the first part in the group.</summary>
    public int FirstPart { get; set; }

    /// <summary>Index of the last part in the group (inclusive).</summary>
    public int LastPart { get; set; }

    public PartGroupSymbol Symbol { get; set; } = PartGroupSymbol.Bracket;

    /// <summary>Barlines run through all staves of the group instead of stopping at each part.</summary>
    public bool JoinBarlines { get; set; }
}

public enum PartGroupSymbol
{
    None,
    Bracket,
    Brace,
    Line,
    Square,
}

public sealed class ScoreStaff
{
    public ClefType Clef { get; set; } = ClefType.Treble;
}

/// <summary>Stored in project files by name, so the numeric values may change freely.</summary>
public enum ClefType
{
    Treble,
    Bass,
    Alto,
    Tenor,
    /// <summary>Treble clef sounding an octave lower, used for tenor voices and guitar.</summary>
    Treble8vb,
}

public sealed class ScoreMeasure
{
    public int Number { get; set; } = 1;
    public int Numerator { get; set; } = 4;
    public int Denominator { get; set; } = 4;

    /// <summary>Key signature as a position on the circle of fifths: −7 (seven flats) to 7 (seven sharps).</summary>
    public int KeyFifths { get; set; }

    /// <summary>A start-repeat sign at the beginning of this measure.</summary>
    public bool RepeatStart { get; set; }

    /// <summary>An end-repeat sign at the end of this measure.</summary>
    public bool RepeatEnd { get; set; }

    /// <summary>How many times the repeated section is played in total, when <see cref="RepeatEnd"/> is set.</summary>
    public int RepeatTimes { get; set; } = 2;

    /// <summary>A volta (1st/2nd ending bracket) that starts at this measure, or null.</summary>
    public ScoreVolta? Volta { get; set; }

    public List<ScoreEvent> Events { get; set; } = new();

    /// <summary>Chord symbols written above the part, such as "Bbmaj7/D".</summary>
    public List<ScoreChordSymbol> ChordSymbols { get; set; } = new();

    /// <summary>Tuplets in this measure, as spans of time in one voice (see docs/SPRINT4F_TUPLETS.md).</summary>
    public List<ScoreTuplet> Tuplets { get; set; } = new();

    /// <summary>Clef changes in this measure. The clef at the start of the piece is <see cref="ScoreStaff.Clef"/>.</summary>
    public List<ScoreClefChange> ClefChanges { get; set; } = new();

    /// <summary>Staves of the part (indices from 0) that are hidden in this measure, as in Finale's hidden empty staves.</summary>
    public List<int> HiddenStaves { get; set; } = new();

    /// <summary>When a system starts here: distance from the previous system, in staff spaces (source layout).</summary>
    public double? SystemDistance { get; set; }

    /// <summary>When a page starts here: distance of the first system from the top margin, in staff spaces.</summary>
    public double? TopSystemDistance { get; set; }

    /// <summary>When a system starts here: distances of this part's staves from the staff above (source layout).</summary>
    public List<ScoreStaffDistance> StaffDistances { get; set; } = new();

    /// <summary>A new system starts with this measure (a line break taken from the source file).</summary>
    public bool SystemBreak { get; set; }

    /// <summary>A new page starts with this measure.</summary>
    public bool PageBreak { get; set; }

    /// <summary>
    /// Width of the measure in the source's layout (Finale's measure width), in staff spaces; at the start of a system
    /// it includes the clef, key and time signature. Null when the source gives none.
    /// </summary>
    public double? SourceWidth { get; set; }

    /// <summary>Dynamics, text and tempo marks at points in this measure (docs/SPRINT_FINALE_MARKS.md).</summary>
    public List<ScoreDirection> Directions { get; set; } = new();

    /// <summary>Crescendo and diminuendo hairpins that start in this measure.</summary>
    public List<ScoreHairpin> Hairpins { get; set; } = new();

    /// <summary>Slurs that start in this measure.</summary>
    public List<ScoreSlur> Slurs { get; set; } = new();
}

/// <summary>What a <see cref="ScoreDirection"/> is. Stored in project files by name.</summary>
public enum DirectionKind
{
    /// <summary>A dynamic such as "mf" or "sfz", drawn in the music font's dynamic letters.</summary>
    Dynamic,

    /// <summary>Text such as "rit.", "dolce" or "Andante".</summary>
    Words,

    /// <summary>A metronome mark: <see cref="ScoreDirection.BeatUnit"/> = <see cref="ScoreDirection.PerMinute"/>.</summary>
    Tempo,
}

/// <summary>A mark at one point of a staff that is not attached to a note: a dynamic, a text or a metronome mark.</summary>
public sealed class ScoreDirection
{
    public DirectionKind Kind { get; set; } = DirectionKind.Words;
    public int Staff { get; set; }

    /// <summary>Where it stands in the measure, in quarter notes.</summary>
    public int PositionNumerator { get; set; }
    public int PositionDenominator { get; set; } = 1;

    /// <summary>The dynamic's letters ("mf", "sfz") or the words; empty for a metronome mark.</summary>
    public string Text { get; set; } = "";

    /// <summary>True above the staff, false below; null uses the usual side (dynamics below, text and tempo above).</summary>
    public bool? Above { get; set; }

    /// <summary>Height of the mark's baseline from the source, in staff spaces above the staff's top line; null places it.</summary>
    public double? Y { get; set; }

    public bool Italic { get; set; }
    public bool Bold { get; set; }

    /// <summary>Beat units a metronome mark may name (MusicXML note type names).</summary>
    public static readonly IReadOnlyList<string> BeatUnits = new[] { "breve", "whole", "half", "quarter", "eighth", "16th", "32nd", "64th" };

    /// <summary>For a metronome mark: the beat's note value in MusicXML terms ("quarter", "half", "eighth", …).</summary>
    public string BeatUnit { get; set; } = "quarter";

    public int BeatUnitDots { get; set; }

    /// <summary>For a metronome mark: beats per minute as written, e.g. "120" or "60-66".</summary>
    public string PerMinute { get; set; } = "";
}

/// <summary>
/// A crescendo or diminuendo hairpin on one staff, from a point in the measure that holds it to a point in the same or
/// a later measure.
/// </summary>
public sealed class ScoreHairpin
{
    public int Staff { get; set; }
    public bool Crescendo { get; set; } = true;

    public int PositionNumerator { get; set; }
    public int PositionDenominator { get; set; } = 1;

    /// <summary>How many measures after this one it ends: 0 in the same measure.</summary>
    public int EndMeasureOffset { get; set; }

    /// <summary>Where it ends in its last measure, in quarter notes.</summary>
    public int EndPositionNumerator { get; set; }
    public int EndPositionDenominator { get; set; } = 1;

    /// <summary>True above the staff, false below; null below.</summary>
    public bool? Above { get; set; }

    /// <summary>Height from the source, in staff spaces above the staff's top line; null places it.</summary>
    public double? Y { get; set; }
}

/// <summary>
/// A slur from the chord of one voice that starts at <see cref="PositionNumerator"/> in this measure to the chord that
/// starts at the end position, in the same or a later measure. The end may be on another staff of the part (piano).
/// </summary>
public sealed class ScoreSlur
{
    public int Staff { get; set; }
    public int Voice { get; set; } = 1;
    public int PositionNumerator { get; set; }
    public int PositionDenominator { get; set; } = 1;

    /// <summary>How many measures after this one the last chord is: 0 in the same measure.</summary>
    public int EndMeasureOffset { get; set; }

    public int EndStaff { get; set; }
    public int EndVoice { get; set; } = 1;
    public int EndPositionNumerator { get; set; }
    public int EndPositionDenominator { get; set; } = 1;

    /// <summary>True curves above the notes, false below; null on the side away from the stems.</summary>
    public bool? Above { get; set; }
}

/// <summary>Marks written on a note. Stored in project files by name.</summary>
public enum Articulation
{
    Staccato,
    Staccatissimo,
    Accent,

    /// <summary>Strong accent (^).</summary>
    Marcato,

    Tenuto,
    Fermata,

    // Ornaments (docs/ORNAMENTS.md), above the staff like a fermata.
    Trill,
    Turn,
    InvertedTurn,

    /// <summary>Lower mordent (with a vertical stroke), MusicXML's "mordent".</summary>
    Mordent,

    /// <summary>Upper mordent (short trill, no stroke), MusicXML's "inverted-mordent".</summary>
    InvertedMordent,
}

public sealed class ScoreClefChange
{
    public int Staff { get; set; }
    public int PositionNumerator { get; set; }
    public int PositionDenominator { get; set; } = 1;
    public ClefType Clef { get; set; } = ClefType.Treble;
}

/// <summary>
/// A tuplet: <see cref="Actual"/> notes in the time of <see cref="Normal"/>, over a span of one voice. The notes in it
/// keep their real lengths (a triplet eighth lasts a third of a quarter); the span only says how they are grouped and
/// marked.
/// </summary>
public sealed class ScoreTuplet
{
    public int Staff { get; set; }
    public int Voice { get; set; } = 1;

    /// <summary>Where the span starts in the measure, in quarter notes.</summary>
    public int PositionNumerator { get; set; }
    public int PositionDenominator { get; set; } = 1;

    /// <summary>How long the span really lasts, in quarter notes (three triplet eighths: 1).</summary>
    public int DurationNumerator { get; set; } = 1;
    public int DurationDenominator { get; set; } = 1;

    public int Actual { get; set; } = 3;
    public int Normal { get; set; } = 2;

    /// <summary>True draws a bracket, false never; null brackets unless the notes are exactly one beam group.</summary>
    public bool? Bracket { get; set; }

    public TupletNumber ShowNumber { get; set; } = TupletNumber.Actual;

    /// <summary>True above the notes, false below; null on the stem side.</summary>
    public bool? Above { get; set; }
}

/// <summary>What a tuplet's number shows. Stored in project files by name.</summary>
public enum TupletNumber
{
    /// <summary>The number of notes, e.g. "3".</summary>
    Actual,

    /// <summary>The ratio, e.g. "3:2".</summary>
    Both,

    None,
}

/// <summary>A 1st/2nd ending bracket.</summary>
public sealed class ScoreVolta
{
    /// <summary>The passes it applies to, as written in the file, e.g. "1" or "1, 2".</summary>
    public string Numbers { get; set; } = "1";

    /// <summary>Text printed in the bracket, e.g. "1."; empty means "<see cref="Numbers"/>." is printed.</summary>
    public string Text { get; set; } = "";

    /// <summary>How many measures the bracket spans, starting with the measure that holds it.</summary>
    public int MeasureCount { get; set; } = 1;

    /// <summary>True when the bracket has no closing hook at its end (usually the last ending).</summary>
    public bool OpenEnd { get; set; }
}

public sealed class ScoreChordSymbol
{
    public int PositionNumerator { get; set; }
    public int PositionDenominator { get; set; } = 1;
    public int Staff { get; set; }

    /// <summary>Root note letter, A to G.</summary>
    public string RootStep { get; set; } = "C";

    /// <summary>−1 for flat, 1 for sharp.</summary>
    public int RootAlter { get; set; }

    /// <summary>Chord quality in MusicXML terms, e.g. "major", "minor-seventh", "suspended-fourth".</summary>
    public string Kind { get; set; } = "major";

    public string? BassStep { get; set; }
    public int BassAlter { get; set; }

    /// <summary>The symbol as printed, e.g. "Bbmaj7/D".</summary>
    public string Text { get; set; } = "C";

    /// <summary>
    /// Height of the symbol's baseline from the source, in staff spaces above the staff's top line; null lets
    /// ScoreStudio place it above the staff.
    /// </summary>
    public double? Y { get; set; }
}

/// <summary>
/// Duration is stored as a rational number of quarter-note units where practical.
/// MIDI pitch is used as a neutral pitch representation; notation spelling is added separately.
/// </summary>
public sealed class ScoreEvent
{
    public string Kind { get; set; } = "Note";
    public int MidiPitch { get; set; } = 60;
    public int DurationNumerator { get; set; } = 1;
    public int DurationDenominator { get; set; } = 1;
    public int PositionNumerator { get; set; } = 0;
    public int PositionDenominator { get; set; } = 1;
    public string? NotationSpelling { get; set; }

    /// <summary>Lyrics under this note, one entry per verse.</summary>
    public List<ScoreLyric> Lyrics { get; set; } = new();

    /// <summary>Verses whose extender line (from an earlier held syllable) ends at this note.</summary>
    public List<int> ExtendEnds { get; set; } = new();

    /// <summary>
    /// Versions 1–3 stored a single lyric here. Loading moves it into <see cref="Lyrics"/>; it is never written.
    /// </summary>
    [JsonPropertyName("Lyric")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyLyric { get; set; }

    /// <summary>A tie from this note to the next note of the same pitch.</summary>
    public bool TieToNext { get; set; }

    /// <summary>
    /// Octave-line shift: 1 for 8va and 2 for 15ma (written lower than sounding), −1 for 8vb and −2 for 15mb.
    /// <see cref="MidiPitch"/> and <see cref="NotationSpelling"/> always hold the sounding pitch.
    /// </summary>
    public int OctaveShift { get; set; }

    /// <summary>Index into the part's staves, starting at 0.</summary>
    public int Staff { get; set; }

    /// <summary>Voice number within the part, starting at 1.</summary>
    public int Voice { get; set; } = 1;

    /// <summary>
    /// Beams at this note, one entry per beam level: index 0 is the 8th-note beam, index 1 the 16th-note beam, and so on.
    /// Empty when the note is not beamed (it keeps its flag). In a chord, the first note carries the chord's beams.
    /// </summary>
    public List<BeamType> Beams { get; set; } = new();

    /// <summary>
    /// Stem direction written in the source (Finale writes one for every note): true up, false down; null leaves it to
    /// ScoreStudio's rules.
    /// </summary>
    public bool? StemUp { get; set; }

    /// <summary>Staccato, accent, fermata and the like on this note, in enum order. In a chord they apply to the whole chord.</summary>
    public List<Articulation> Articulations { get; set; } = new();

    /// <summary>
    /// Grace notes played before this note or rest (or, marked <see cref="ScoreGraceNote.After"/>, after it), in order.
    /// They take no time in the measure. In a chord the first note carries them (docs/GRACE_NOTES.md).
    /// </summary>
    public List<ScoreGraceNote> GraceNotes { get; set; } = new();

    /// <summary>A "let ring" tie: a short tie to nothing, the note is left to sound (docs/ORNAMENTS.md).</summary>
    public bool LetRing { get; set; }

    /// <summary>Strokes of a single-note tremolo through the stem (1–4); 0 for none.</summary>
    public int Tremolo { get; set; }

    /// <summary>
    /// Strokes of a two-note tremolo from this chord to the next chord of its voice (1–4); 0 for none. Both chords keep
    /// their real lengths and are written at twice that value, as usual for such tremolos.
    /// </summary>
    public int TremoloToNext { get; set; }
}

/// <summary>A grace note: a small note without time of its own, before (or after) the note that carries it.</summary>
public sealed class ScoreGraceNote
{
    public int MidiPitch { get; set; } = 72;
    public string? NotationSpelling { get; set; }

    /// <summary>The written value in MusicXML terms: "eighth" (the usual), "16th", "32nd", "quarter" or "half".</summary>
    public string Type { get; set; } = "eighth";

    /// <summary>A slash through the stem (acciaccatura).</summary>
    public bool Slash { get; set; }

    /// <summary>Sounds together with the grace note before it (a grace chord).</summary>
    public bool Chord { get; set; }

    /// <summary>Comes after the carrying note instead of before it (Finale's grace notes at the end of a measure).</summary>
    public bool After { get; set; }

    /// <summary>Grace note values a file may hold.</summary>
    public static readonly IReadOnlyList<string> Types = new[] { "half", "quarter", "eighth", "16th", "32nd", "64th" };
}

public sealed class ScoreLyric
{
    public int Verse { get; set; } = 1;
    public string Text { get; set; } = "";
    public LyricSyllabic Syllabic { get; set; } = LyricSyllabic.Single;

    /// <summary>An extender line follows the syllable (it is held over several notes).</summary>
    public bool Extend { get; set; }

    /// <summary>
    /// Height of the lyric baseline from the source, in staff spaces above the staff's top line (negative is below the
    /// staff). Null lets ScoreStudio place the line under the staff.
    /// </summary>
    public double? Y { get; set; }
}

/// <summary>What a beam does at one note, as in MusicXML's &lt;beam&gt;.</summary>
public enum BeamType
{
    Begin,
    Continue,
    End,

    /// <summary>A short beam pointing right from this note only (e.g. a 16th followed by a dotted 8th).</summary>
    ForwardHook,

    /// <summary>A short beam pointing left from this note only (e.g. the 16th after a dotted 8th).</summary>
    BackwardHook,
}

/// <summary>Where a syllable sits in its word; a hyphen follows Begin and Middle.</summary>
public enum LyricSyllabic
{
    Single,
    Begin,
    Middle,
    End,
}
