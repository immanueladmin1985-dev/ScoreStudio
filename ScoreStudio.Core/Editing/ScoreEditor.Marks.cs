using ScoreStudio.Import;
using ScoreStudio.Models;

namespace ScoreStudio.Editing;

/// <summary>A shape the Smart Shape tool draws from one chord to another.</summary>
public enum ShapeKind
{
    Slur,
    Crescendo,
    Diminuendo,
}

/// <summary>
/// Articulations, expressions (dynamics, text, tempo marks) and smart shapes (slurs, hairpins), as Finale's Articulation,
/// Expression and Smart Shape tools put them in (docs/SPRINT_FINALE_MARKS.md, 2-C).
/// </summary>
public sealed partial class ScoreEditor
{
    private const int MaxMarkTextLength = 60;

    /// <summary>The Smart Shape tool's first chord, waiting for the chord the shape ends on; null when none is chosen.</summary>
    public EditPosition? ShapeStart { get; private set; }

    /// <summary>The shape the Smart Shape tool draws next.</summary>
    public ShapeKind Shape { get; set; } = ShapeKind.Slur;

    public static string ArticulationName(Articulation articulation) => articulation switch
    {
        Articulation.Staccato => "스타카토",
        Articulation.Staccatissimo => "스타카티시모",
        Articulation.Accent => "악센트",
        Articulation.Marcato => "마르카토(강한 악센트)",
        Articulation.Tenuto => "테누토",
        _ => "페르마타",
    };

    public static string ShapeName(ShapeKind shape) => shape switch
    {
        ShapeKind.Slur => "이음줄",
        ShapeKind.Crescendo => "크레셴도",
        _ => "디크레셴도",
    };

    /// <summary>The place a mark goes: the selected chord or rest, else the cursor.</summary>
    private EditPosition MarkPlace => Selection ?? Cursor;

    /// <summary>
    /// Adds an articulation to the selected chord, or takes it off when the chord has it. A rest takes only a fermata.
    /// </summary>
    public EditResult ToggleArticulation(Articulation articulation)
    {
        var at = MarkPlace;
        if (ChordAt(at) is not { } chord) return EditResult.Refused("아티큘레이션을 붙일 음표를 먼저 누르세요.");
        if (chord[0].Kind == "Rest" && articulation != Articulation.Fermata) return EditResult.Refused("쉼표에는 페르마타만 붙일 수 있습니다.");

        var had = chord.Any(e => e.Articulations.Contains(articulation));
        var name = ArticulationName(articulation);
        return Change(() =>
        {
            var notes = ChordAt(at)!;
            if (had)
            {
                foreach (var e in notes) e.Articulations.Remove(articulation);
            }
            else
            {
                notes[0].Articulations.Add(articulation);
                notes[0].Articulations.Sort();
            }

            return null;
        }, () => had ? $"{name}을(를) 뺐습니다." : $"{name}을(를) 붙였습니다.");
    }

    /// <summary>Takes every articulation and fermata off the selected chord.</summary>
    public EditResult ClearArticulations()
    {
        var at = MarkPlace;
        if (ChordAt(at) is null) return EditResult.Refused("아티큘레이션을 지울 음표를 먼저 누르세요.");
        return Change(() =>
        {
            var notes = ChordAt(at)!;
            if (notes.All(e => e.Articulations.Count == 0)) return "이 음표에는 지울 아티큘레이션이 없습니다.";
            foreach (var e in notes) e.Articulations.Clear();
            return null;
        }, () => "아티큘레이션을 모두 지웠습니다.");
    }

    /// <summary>Puts a dynamic ("mf", "sfz", "sempre p") at the selected chord or the cursor, replacing one there; empty text removes it.</summary>
    public EditResult SetDynamic(string text) =>
        SetDirection(DirectionKind.Dynamic, text, d => d.Text = text.Trim(), $"셈여림 '{text.Trim()}'을(를) 넣었습니다.", "셈여림을 지웠습니다.");

    /// <summary>Puts text ("dolce", "rit.") above the staff at the selected chord or the cursor, replacing text there; empty text removes it.</summary>
    public EditResult SetExpressionText(string text, bool italic = true, bool bold = false) =>
        SetDirection(DirectionKind.Words, text, d => (d.Text, d.Italic, d.Bold) = (text.Trim(), italic, bold), $"글자 '{text.Trim()}'을(를) 넣었습니다.", "글자를 지웠습니다.");

    /// <summary>
    /// Puts a metronome mark (a beat unit such as "quarter", with dots, = <paramref name="perMinute"/>) at the selected
    /// chord or the cursor, replacing one there; an empty <paramref name="perMinute"/> removes it.
    /// </summary>
    public EditResult SetTempo(string beatUnit, int dots, string perMinute)
    {
        if (!ScoreDirection.BeatUnits.Contains(beatUnit) || dots is < 0 or > 2) return EditResult.Refused("메트로놈 표시의 음표가 올바르지 않습니다.");
        if (perMinute.Trim().Length > 12) return EditResult.Refused("빠르기는 12자까지 쓸 수 있습니다(예: 120, 60-66).");
        return SetDirection(DirectionKind.Tempo, perMinute, d => (d.BeatUnit, d.BeatUnitDots, d.PerMinute) = (beatUnit, dots, perMinute.Trim()),
            $"메트로놈 표시(= {perMinute.Trim()})를 넣었습니다.", "메트로놈 표시를 지웠습니다.");
    }

    private EditResult SetDirection(DirectionKind kind, string text, Action<ScoreDirection> fill, string added, string removed)
    {
        var at = MarkPlace;
        if (!IsValid(at)) return EditResult.Refused("기호를 넣을 자리를 먼저 누르세요.");
        var trimmed = text.Trim();
        if (trimmed.Length > MaxMarkTextLength || trimmed.Any(char.IsControl)) return EditResult.Refused($"기호의 글자는 {MaxMarkTextLength}자까지 쓸 수 있습니다.");

        return Change(() =>
        {
            var measure = MeasureAt(at);
            var count = measure.Directions.RemoveAll(d => d.Kind == kind && d.Staff == at.Staff && new Rational(d.PositionNumerator, d.PositionDenominator) == at.Position);
            if (trimmed.Length == 0) return count == 0 ? "그 자리에 지울 기호가 없습니다." : null;

            var direction = new ScoreDirection
            {
                Kind = kind, Staff = at.Staff, PositionNumerator = (int)at.Position.Numerator, PositionDenominator = (int)at.Position.Denominator,
            };
            fill(direction);
            measure.Directions.Add(direction);
            measure.Directions = measure.Directions.OrderBy(d => d.Staff).ThenBy(d => new Rational(d.PositionNumerator, d.PositionDenominator)).ToList();
            return null;
        }, () => trimmed.Length == 0 ? removed : added);
    }

    /// <summary>Removes the dynamics, text and tempo marks at the selected chord or the cursor.</summary>
    public EditResult ClearExpressions()
    {
        var at = MarkPlace;
        if (!IsValid(at)) return EditResult.Refused("기호를 지울 자리를 먼저 누르세요.");
        return Change(() =>
            MeasureAt(at).Directions.RemoveAll(d => d.Staff == at.Staff && new Rational(d.PositionNumerator, d.PositionDenominator) == at.Position) == 0
                ? "그 자리에는 지울 셈여림·글자가 없습니다."
                : null,
            () => "셈여림·글자를 지웠습니다.");
    }

    /// <summary>
    /// Smart Shape tool click: the first chord starts a shape, the second ends it (<see cref="AddShape"/>). Clicking the
    /// first chord again cancels.
    /// </summary>
    public EditResult ClickShape(EditPosition at)
    {
        if (ChordAt(at) is null) return EditResult.Refused($"{ShapeName(Shape)}을(를) 시작할 음표를 누르세요.");
        var start = ShapeStart;
        if (start is null || start == at)
        {
            ShapeStart = start is null ? at : null;
            Select(at);
            return EditResult.Ok(ShapeStart is null
                ? $"{ShapeName(Shape)} 시작을 취소했습니다."
                : $"{ShapeName(Shape)}을(를) 시작할 음표를 골랐습니다. 끝날 음표를 누르세요.");
        }

        ShapeStart = null;
        var result = AddShape(start, at, Shape);
        Select(at);
        return result;
    }

    public void CancelShape() => ShapeStart = null;

    /// <summary>
    /// Draws a slur, crescendo or diminuendo from one chord to another of the same part (in either order). A slur
    /// replaces one that starts at the same chord; a hairpin replaces one that starts at the same time on the staff.
    /// </summary>
    public EditResult AddShape(EditPosition from, EditPosition to, ShapeKind shape)
    {
        if (ChordAt(from) is null || ChordAt(to) is null) return EditResult.Refused("시작과 끝 음표를 눌러 주세요.");
        if (from.Part != to.Part) return EditResult.Refused("다른 파트의 음표까지는 이을 수 없습니다.");
        if (to.Measure < from.Measure || (to.Measure == from.Measure && to.Position < from.Position)) (from, to) = (to, from);
        if (from.Measure == to.Measure && from.Position == to.Position) return EditResult.Refused("시작과 끝이 같은 자리입니다. 다른 음표를 누르세요.");

        var name = ShapeName(shape);
        return Change(() =>
        {
            var measure = MeasureAt(from);
            var (start, end) = (from.Position, to.Position);
            if (shape == ShapeKind.Slur)
            {
                measure.Slurs.RemoveAll(s => s.Staff == from.Staff && s.Voice == from.Voice && new Rational(s.PositionNumerator, s.PositionDenominator) == start);
                measure.Slurs.Add(new ScoreSlur
                {
                    Staff = from.Staff, Voice = from.Voice, PositionNumerator = (int)start.Numerator, PositionDenominator = (int)start.Denominator,
                    EndMeasureOffset = to.Measure - from.Measure, EndStaff = to.Staff, EndVoice = to.Voice,
                    EndPositionNumerator = (int)end.Numerator, EndPositionDenominator = (int)end.Denominator,
                });
                measure.Slurs = measure.Slurs.OrderBy(s => s.Staff).ThenBy(s => s.Voice).ThenBy(s => new Rational(s.PositionNumerator, s.PositionDenominator)).ToList();
            }
            else
            {
                measure.Hairpins.RemoveAll(h => h.Staff == from.Staff && new Rational(h.PositionNumerator, h.PositionDenominator) == start);
                measure.Hairpins.Add(new ScoreHairpin
                {
                    Staff = from.Staff, Crescendo = shape == ShapeKind.Crescendo,
                    PositionNumerator = (int)start.Numerator, PositionDenominator = (int)start.Denominator,
                    EndMeasureOffset = to.Measure - from.Measure, EndPositionNumerator = (int)end.Numerator, EndPositionDenominator = (int)end.Denominator,
                });
                measure.Hairpins = measure.Hairpins.OrderBy(h => h.Staff).ThenBy(h => new Rational(h.PositionNumerator, h.PositionDenominator)).ToList();
            }

            return null;
        }, () => $"{from.Measure + 1}마디 {DescribeBeat(from.Position)}부터 {to.Measure + 1}마디 {DescribeBeat(to.Position)}까지 {name}을(를) 넣었습니다.");
    }

    /// <summary>Removes the slurs and hairpins that start at the selected chord.</summary>
    public EditResult ClearShapes()
    {
        var at = MarkPlace;
        if (!IsValid(at)) return EditResult.Refused("지울 이음줄·크레셴도가 시작하는 음표를 먼저 누르세요.");
        ShapeStart = null;
        return Change(() =>
        {
            var measure = MeasureAt(at);
            var removed = measure.Slurs.RemoveAll(s => s.Staff == at.Staff && s.Voice == at.Voice && new Rational(s.PositionNumerator, s.PositionDenominator) == at.Position)
                + measure.Hairpins.RemoveAll(h => h.Staff == at.Staff && new Rational(h.PositionNumerator, h.PositionDenominator) == at.Position);
            return removed == 0 ? "이 음표에서 시작하는 이음줄·크레셴도가 없습니다." : null;
        }, () => "이 음표에서 시작하는 이음줄·크레셴도를 지웠습니다.");
    }
}
