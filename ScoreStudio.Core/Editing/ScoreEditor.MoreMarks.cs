using ScoreStudio.Import;
using ScoreStudio.Models;

namespace ScoreStudio.Editing;

/// <summary>
/// Tools for the marks ScoreStudio first only imported (docs/MARK_TOOLS.md): glissandos, trill lines, brackets and
/// pedals as smart shapes; rehearsal marks, segno and coda as expressions; fingerings, tremolos, buzz rolls, arpeggios,
/// accidental marks and cue notes on the selected chord; common and cut time and barline fermatas on the measure.
/// </summary>
public sealed partial class ScoreEditor
{
    /// <summary>A glissando, trill line, bracket or pedal from one chord to another (the Smart Shape tool).</summary>
    private string? AddLineShape(ScoreMeasure measure, EditPosition from, EditPosition to, ShapeKind shape)
    {
        var (start, end) = (from.Position, to.Position);
        if (shape == ShapeKind.Glissando)
        {
            if (ChordAt(from)!.Any(e => e.Kind == "Rest") || ChordAt(to)!.Any(e => e.Kind == "Rest")) return "글리산도는 음표에서 음표로만 그을 수 있습니다.";
            measure.Glissandos.RemoveAll(g => g.Staff == from.Staff && g.Voice == from.Voice && new Rational(g.PositionNumerator, g.PositionDenominator) == start);
            measure.Glissandos.Add(new ScoreGlissando
            {
                Staff = from.Staff, Voice = from.Voice, PositionNumerator = (int)start.Numerator, PositionDenominator = (int)start.Denominator,
                EndMeasureOffset = to.Measure - from.Measure, EndStaff = to.Staff, EndVoice = to.Voice,
                EndPositionNumerator = (int)end.Numerator, EndPositionDenominator = (int)end.Denominator, Text = "gliss.",
            });
            measure.Glissandos = measure.Glissandos.OrderBy(g => g.Staff).ThenBy(g => g.Voice).ThenBy(g => new Rational(g.PositionNumerator, g.PositionDenominator)).ToList();
            return null;
        }

        var kind = shape switch { ShapeKind.TrillLine => LineKind.TrillLine, ShapeKind.Bracket => LineKind.Bracket, _ => LineKind.Pedal };
        measure.Lines.RemoveAll(l => l.Kind == kind && l.Staff == from.Staff && new Rational(l.PositionNumerator, l.PositionDenominator) == start);
        var line = new ScoreLine
        {
            Kind = kind, Staff = from.Staff, PositionNumerator = (int)start.Numerator, PositionDenominator = (int)start.Denominator,
            EndMeasureOffset = to.Measure - from.Measure, EndPositionNumerator = (int)end.Numerator, EndPositionDenominator = (int)end.Denominator,
        };
        switch (shape)
        {
            case ShapeKind.Bracket:
                (line.Above, line.StartHook, line.EndHook) = (true, -1, -1);
                break;
            case ShapeKind.PedalLine:
                (line.Above, line.PedalLine) = (false, true);
                break;
            case ShapeKind.PedalSigns:
                (line.Above, line.PedalSign) = (false, true);
                break;
            case ShapeKind.TrillLine:
            {
                // Finale's trill smart shape is a "tr" and its wavy line.
                var first = ChordAt(from)![0];
                if (first.Kind != "Rest" && !first.Articulations.Contains(Articulation.Trill))
                {
                    first.Articulations.Add(Articulation.Trill);
                    first.Articulations.Sort();
                }

                break;
            }
        }

        measure.Lines.Add(line);
        measure.Lines = measure.Lines.OrderBy(l => l.Staff).ThenBy(l => new Rational(l.PositionNumerator, l.PositionDenominator)).ThenBy(l => l.Kind).ToList();
        return null;
    }

    /// <summary>
    /// Puts a rehearsal mark at the selected chord or the cursor, replacing one there. Empty text takes the letter after
    /// the last mark before it (A, B, C …), as Finale's rehearsal marks count on.
    /// </summary>
    public EditResult SetRehearsal(string text)
    {
        var at = MarkPlace;
        if (!IsValid(at)) return EditResult.Refused("리허설 마크를 넣을 자리를 먼저 누르세요.");
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            var before = Project.Parts[at.Part].Measures.Take(at.Measure + 1)
                .SelectMany((m, i) => m.Directions.Where(d => d.Kind == DirectionKind.Rehearsal
                    && (i < at.Measure || new Rational(d.PositionNumerator, d.PositionDenominator) < at.Position)))
                .Select(d => d.Text).LastOrDefault();
            trimmed = before is { Length: 1 } letter && letter[0] is >= 'A' and < 'Z' ? ((char)(letter[0] + 1)).ToString() : "A";
        }

        return SetDirection(DirectionKind.Rehearsal, trimmed, d => (d.Text, d.Boxed, d.Bold) = (trimmed, true, true),
            $"리허설 마크 '{trimmed}'을(를) 넣었습니다.", "리허설 마크를 지웠습니다.");
    }

    /// <summary>Puts a segno or coda sign at the selected chord or the cursor, or takes the one there away.</summary>
    public EditResult ToggleSign(DirectionKind sign)
    {
        if (sign is not (DirectionKind.Segno or DirectionKind.Coda)) return EditResult.Refused("세뇨나 코다만 넣을 수 있습니다.");
        var at = MarkPlace;
        if (!IsValid(at)) return EditResult.Refused("기호를 넣을 자리를 먼저 누르세요.");
        var name = sign == DirectionKind.Segno ? "세뇨" : "코다";
        var had = MeasureAt(at).Directions.Any(d => d.Kind == sign && d.Staff == at.Staff && new Rational(d.PositionNumerator, d.PositionDenominator) == at.Position);
        return Change(() =>
        {
            var measure = MeasureAt(at);
            if (had)
            {
                measure.Directions.RemoveAll(d => d.Kind == sign && d.Staff == at.Staff && new Rational(d.PositionNumerator, d.PositionDenominator) == at.Position);
                return null;
            }

            measure.Directions.Add(new ScoreDirection
            {
                Kind = sign, Staff = at.Staff, Above = true, PositionNumerator = (int)at.Position.Numerator, PositionDenominator = (int)at.Position.Denominator,
            });
            measure.Directions = measure.Directions.OrderBy(d => d.Staff).ThenBy(d => new Rational(d.PositionNumerator, d.PositionDenominator)).ToList();
            return null;
        }, () => had ? $"{name}를 지웠습니다." : $"{name}를 넣었습니다.");
    }

    /// <summary>
    /// Fingerings for the selected chord, its notes from the bottom up: "1 3 5", or "4-1" for a substitution. Empty takes
    /// them off. Fingers are numbers or letters (p i m a), up to three characters each.
    /// </summary>
    public EditResult SetFingering(string text, bool above = true)
    {
        var at = MarkPlace;
        if (ChordAt(at) is not { } chord || chord[0].Kind == "Rest") return EditResult.Refused("운지 번호를 붙일 음표를 먼저 누르세요.");
        var groups = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(g => g.Split('-', StringSplitOptions.RemoveEmptyEntries).ToList()).ToList();
        if (groups.Count > chord.Count) return EditResult.Refused($"이 화음은 {chord.Count}음입니다. 운지 번호를 음마다 하나씩 띄어 쓰세요(아래 음부터).");
        if (groups.SelectMany(g => g).Any(f => f.Length > 3 || f.Any(c => !char.IsLetterOrDigit(c))))
            return EditResult.Refused("운지 번호는 숫자나 글자 3자까지 쓸 수 있습니다(예: 1 3 5, 4-1).");

        return Change(() =>
        {
            var notes = ChordAt(at)!.OrderBy(e => e.MidiPitch).ToList();
            for (var i = 0; i < notes.Count; i++)
            {
                notes[i].Fingerings = i < groups.Count ? groups[i] : new List<string>();
                notes[i].FingeringAbove = groups.Count == 0 ? null : above;
            }

            return null;
        }, () => groups.Count == 0 ? "운지 번호를 지웠습니다." : $"운지 번호 '{text.Trim()}'을(를) 붙였습니다.");
    }

    /// <summary>Strokes through the selected chord's stem (a single-note tremolo), 0 to take them off.</summary>
    public EditResult SetTremolo(int strokes)
    {
        if (strokes is < 0 or > 4) return EditResult.Refused("트레몰로 빗금은 1~4개입니다.");
        return ChangeChord("트레몰로", notes =>
        {
            foreach (var e in notes) (e.Tremolo, e.BuzzRoll) = (0, false);
            notes[0].Tremolo = strokes;
        }, strokes == 0 ? "트레몰로를 뺐습니다." : $"트레몰로 빗금 {strokes}개를 넣었습니다.");
    }

    /// <summary>Puts a buzz roll (z on the stem) on the selected chord, or takes it off.</summary>
    public EditResult ToggleBuzzRoll()
    {
        var had = ChordAt(MarkPlace)?.Any(e => e.BuzzRoll) == true;
        return ChangeChord("버즈 롤", notes =>
        {
            foreach (var e in notes) (e.BuzzRoll, e.Tremolo) = (false, 0);
            notes[0].BuzzRoll = !had;
        }, had ? "버즈 롤을 뺐습니다." : "버즈 롤을 넣었습니다.");
    }

    /// <summary>Rolls the selected chord (or takes the roll off when it has this one already).</summary>
    public EditResult SetArpeggio(ArpeggioKind kind)
    {
        var had = ChordAt(MarkPlace)?.FirstOrDefault()?.Arpeggio == kind;
        var name = kind switch { ArpeggioKind.Up => "위로 펴는 아르페지오", ArpeggioKind.Down => "아래로 펴는 아르페지오", ArpeggioKind.NonArpeggio => "논아르페지오", _ => "아르페지오" };
        return ChangeChord("아르페지오", notes =>
        {
            foreach (var e in notes) e.Arpeggio = had || kind == ArpeggioKind.None ? ArpeggioKind.None : kind;
        }, had || kind == ArpeggioKind.None ? "아르페지오를 뺐습니다." : $"{name}를 넣었습니다.");
    }

    /// <summary>An accidental over the selected chord (for its ornament, or editorial): −2…2, or null to take it off.</summary>
    public EditResult SetAccidentalMark(int? alter)
    {
        if (alter is < -2 or > 2) return EditResult.Refused("임시표 기호는 겹내림표부터 겹올림표까지입니다.");
        return ChangeChord("임시표 기호", notes =>
        {
            foreach (var e in notes) e.AccidentalMark = null;
            notes[0].AccidentalMark = alter;
        }, alter is null ? "임시표 기호를 뺐습니다." : "임시표 기호를 넣었습니다.");
    }

    /// <summary>Makes the selected chord a cue chord (small, not played), or a normal one again.</summary>
    public EditResult ToggleCue()
    {
        var at = MarkPlace;
        if (ChordAt(at) is not { } chord) return EditResult.Refused("큐 음표로 바꿀 음표를 먼저 누르세요.");
        var had = chord.All(e => e.Cue);
        return Change(() =>
        {
            foreach (var e in ChordAt(at)!) (e.Cue, e.CueSize) = (!had, false);
            return null;
        }, () => had ? "보통 음표로 바꿨습니다." : "큐 음표(작게, 연주하지 않음)로 바꿨습니다.");
    }

    /// <summary>A change to the notes of the selected chord, which must be notes.</summary>
    private EditResult ChangeChord(string what, Action<List<ScoreEvent>> apply, string done)
    {
        var at = MarkPlace;
        if (ChordAt(at) is not { } chord || chord[0].Kind == "Rest") return EditResult.Refused($"{what}을(를) 붙일 음표를 먼저 누르세요.");
        return Change(() =>
        {
            apply(ChordAt(at)!);
            return null;
        }, () => done);
    }

    /// <summary>Puts a fermata over the barline that closes the cursor's measure (in every part), or takes it off.</summary>
    public EditResult ToggleBarlineFermata()
    {
        if (!IsValid(Cursor with { Position = Rational.Zero })) return EditResult.Refused("마디를 먼저 누르세요.");
        var index = Cursor.Measure;
        var had = Project.Parts.Any(p => index < p.Measures.Count && p.Measures[index].BarlineFermata);
        return Change(() =>
        {
            foreach (var part in Project.Parts.Where(p => index < p.Measures.Count)) part.Measures[index].BarlineFermata = !had;
            return null;
        }, () => had ? $"{index + 1}마디 끝 마디선의 페르마타를 뺐습니다." : $"{index + 1}마디 끝 마디선에 페르마타를 넣었습니다.");
    }

    /// <summary>
    /// Shows 4/4 as C and 2/2 as ¢ (or as numbers again) from the cursor's measure to the next time change, as Finale's
    /// "abbreviate" choice in its Time Signature dialog.
    /// </summary>
    public EditResult ToggleTimeAbbreviation()
    {
        if (!IsValid(Cursor with { Position = Rational.Zero })) return EditResult.Refused("마디를 먼저 누르세요.");
        var measures = Project.Parts[Cursor.Part].Measures;
        var first = Cursor.Measure;
        var time = (measures[first].Numerator, measures[first].Denominator);
        var symbol = time switch { (4, 4) => TimeSymbol.Common, (2, 2) => TimeSymbol.Cut, _ => (TimeSymbol?)null };
        if (symbol is null) return EditResult.Refused("C는 4/4, ¢는 2/2 박자에만 쓸 수 있습니다.");
        var abbreviated = measures[first].TimeSymbol == symbol;
        var last = first;
        while (last + 1 < measures.Count && (measures[last + 1].Numerator, measures[last + 1].Denominator) == time) last++;
        return Change(() =>
        {
            foreach (var part in Project.Parts)
                for (var m = first; m <= last && m < part.Measures.Count; m++)
                    (part.Measures[m].TimeSymbol, part.Measures[m].BeatsText) = (abbreviated ? TimeSymbol.Normal : symbol.Value, "");
            return null;
        }, () => abbreviated ? "박자표를 숫자로 썼습니다." : $"박자표를 {(symbol == TimeSymbol.Common ? "C" : "¢")}로 썼습니다.");
    }
}
