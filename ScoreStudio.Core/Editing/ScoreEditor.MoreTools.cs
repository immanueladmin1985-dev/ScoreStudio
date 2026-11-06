using ScoreStudio.Import;
using ScoreStudio.Layout;
using ScoreStudio.Models;

namespace ScoreStudio.Editing;

/// <summary>
/// Tools for the last marks that were only imported (docs/MARK_TOOLS.md, "남은 도구"): other keys, senza misura,
/// transposition changes, chord diagrams, figured bass and music-font symbols.
/// </summary>
public sealed partial class ScoreEditor
{
    /// <summary>
    /// Reads another key as written: accidentals separated by spaces, e.g. "F# Bb" or "C## G#"; "b" flats, "#" sharps,
    /// "n" a natural. Null with the reason when it cannot be read.
    /// </summary>
    public static List<ScoreKeyAccidental>? ParseKeyAccidentals(string text, out string error)
    {
        error = "";
        var result = new List<ScoreKeyAccidental>();
        foreach (var token in text.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var step = char.ToUpperInvariant(token[0]);
            var rest = token[1..];
            int? alter = rest switch { "#" => 1, "##" or "x" => 2, "b" => -1, "bb" => -2, "n" => 0, _ => null };
            if (step is < 'A' or > 'G' || alter is null || result.Any(r => r.Step == step.ToString()))
            {
                error = $"'{token}'을(를) 읽을 수 없습니다. F# Bb처럼 음이름과 #·b를 띄어 쓰세요.";
                return null;
            }

            result.Add(new ScoreKeyAccidental { Step = step.ToString(), Alter = alter.Value });
        }

        if (result.Count is 0 or > 7) error = "변화음을 1~7개 쓰세요.";
        return result.Count is 0 or > 7 ? null : result;
    }

    /// <summary>
    /// Sets another key (docs/FINALE_MARKS_3.md) from the cursor's measure up to the next key change, the same in every
    /// part. Notes keep their pitches.
    /// </summary>
    public EditResult SetOtherKey(string text)
    {
        if (ParseKeyAccidentals(text, out var error) is not { } accidentals) return EditResult.Refused(error);
        if (!CursorIsValid()) return EditResult.Refused("조표를 바꿀 마디를 먼저 누르세요.");
        var first = Cursor.Measure;
        var measures = Project.Parts[Cursor.Part].Measures;
        static string Key(ScoreMeasure m) => m.KeyFifths + ":" + string.Join(",", m.KeyAccidentals.Select(k => k.Step + k.Alter));
        var old = Key(measures[first]);
        var last = first;
        while (last + 1 < measures.Count && Key(measures[last + 1]) == old) last++;
        var name = string.Join(" ", accidentals.Select(a => a.Step + (a.Alter switch { 2 => "𝄪", 1 => "♯", -1 => "♭", -2 => "𝄫", _ => "♮" })));
        return Change(() =>
        {
            foreach (var part in Project.Parts)
                for (var m = first; m <= last && m < part.Measures.Count; m++)
                {
                    part.Measures[m].KeyFifths = 0;
                    part.Measures[m].KeyAccidentals = accidentals.Select(a => new ScoreKeyAccidental { Step = a.Step, Alter = a.Alter }).ToList();
                }

            return null;
        }, () => $"{Span(first, last)}의 조표를 바꿨습니다: {name}");
    }

    /// <summary>Makes the cursor's measure senza misura (no time signature shown), or measured again, in every part.</summary>
    public EditResult ToggleSenzaMisura()
    {
        if (!CursorIsValid()) return EditResult.Refused("마디를 먼저 누르세요.");
        var index = Cursor.Measure;
        var senza = Project.Parts[Cursor.Part].Measures[index].SenzaMisura;
        return Change(() =>
        {
            foreach (var part in Project.Parts.Where(p => index < p.Measures.Count)) part.Measures[index].SenzaMisura = !senza;
            return null;
        }, () => senza ? $"{index + 1}마디에 박자표를 다시 씁니다." : $"{index + 1}마디를 박자 없음(senza misura)으로 했습니다.");
    }

    /// <summary>Transposing instruments the instrument-change menu offers: name, diatonic steps, semitones.</summary>
    public static readonly (string Name, int Diatonic, int Chromatic)[] Transpositions =
    {
        ("C (이조 없음)", 0, 0), ("B♭ (클라리넷, 트럼펫, 테너 색소폰은 옥타브 아래)", -1, -2), ("A (클라리넷)", -2, -3),
        ("E♭ (알토 색소폰, 6도 아래)", -5, -9), ("F (호른)", -4, -7), ("G (알토 플루트)", -3, -5), ("D (트럼펫)", 1, 2),
    };

    /// <summary>
    /// Changes the cursor part's transposing instrument from the cursor's measure on (B♭ clarinet to A, for example).
    /// In the first measure it sets the part's instrument. Written notes stay as they are.
    /// </summary>
    public EditResult SetTransposition(int diatonic, int chromatic, int octaveChange = 0)
    {
        if (diatonic is < -14 or > 14 || chromatic is < -24 or > 24 || octaveChange is < -3 or > 3) return EditResult.Refused("이조 값이 범위를 벗어났습니다.");
        if (!CursorIsValid()) return EditResult.Refused("이조를 바꿀 마디를 먼저 누르세요.");
        var (partIndex, index) = (Cursor.Part, Cursor.Measure);
        var transpose = diatonic == 0 && chromatic == 0 && octaveChange == 0 ? null : new ScoreTranspose { Diatonic = diatonic, Chromatic = chromatic, OctaveChange = octaveChange };
        return Change(() =>
        {
            var part = Project.Parts[partIndex];
            if (index == 0) part.Transpose = transpose;
            else part.Measures[index].Transpose = transpose ?? new ScoreTranspose();
            return null;
        }, () => $"'{Project.Parts[partIndex].Name}' 파트의 이조를 {index + 1}마디부터 바꿨습니다(음표는 기보음 그대로).");
    }

    /// <summary>
    /// Reads a chord diagram as guitarists write it: one entry per string from the lowest, "x" not played, "0" open, a
    /// fret number otherwise, e.g. "x32010" or, with frets past 9, "8 10 10 9 8 8". Null with the reason when it cannot
    /// be read.
    /// </summary>
    public static ScoreFrame? ParseFrame(string text, out string error)
    {
        error = "";
        var trimmed = text.Trim();
        var tokens = trimmed.Contains(' ') ? trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries) : trimmed.Select(c => c.ToString()).ToArray();
        if (tokens.Length is < 2 or > 12)
        {
            error = "줄마다 x, 0, 프렛 번호를 아래 줄부터 쓰세요(예: x32010).";
            return null;
        }

        var frame = new ScoreFrame { Strings = tokens.Length };
        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i] is "x" or "X") continue;
            if (!int.TryParse(tokens[i], out var fret) || fret is < 0 or > 24)
            {
                error = $"'{tokens[i]}'을(를) 읽을 수 없습니다. x, 0, 프렛 번호만 쓸 수 있습니다.";
                return null;
            }

            frame.Notes.Add(new ScoreFrameNote { String = tokens.Length - i, Fret = fret });
        }

        if (frame.Notes.Count == 0)
        {
            error = "치는 줄이 하나도 없습니다.";
            return null;
        }

        var fretted = frame.Notes.Where(n => n.Fret > 0).Select(n => n.Fret).ToList();
        var (low, high) = fretted.Count == 0 ? (1, 1) : (fretted.Min(), fretted.Max());
        frame.FirstFret = high > 4 ? low : null;
        frame.Frets = Math.Max(4, high - (frame.FirstFret ?? 1) + 1);
        return frame;
    }

    /// <summary>Gives the chord symbol at the selected chord or the cursor a diagram (see <see cref="ParseFrame"/>); empty text removes it.</summary>
    public EditResult SetChordFrame(string text)
    {
        var at = MarkPlace;
        if (!IsValid(at) || MeasureAt(at).ChordSymbols.FirstOrDefault(c => c.Staff == at.Staff && new Rational(c.PositionNumerator, c.PositionDenominator) == at.Position) is null)
            return EditResult.Refused("코드 다이어그램은 코드 이름이 있는 자리에 넣습니다. 먼저 코드 이름을 넣으세요.");
        ScoreFrame? frame = null;
        if (text.Trim().Length > 0 && (frame = ParseFrame(text, out var error)) is null) return EditResult.Refused(error);
        return Change(() =>
        {
            MeasureAt(at).ChordSymbols.First(c => c.Staff == at.Staff && new Rational(c.PositionNumerator, c.PositionDenominator) == at.Position).Frame = frame;
            return null;
        }, () => frame is null ? "코드 다이어그램을 지웠습니다." : "코드 다이어그램을 넣었습니다.");
    }

    /// <summary>
    /// Reads figured bass: figures from the top, separated by spaces, e.g. "6 4", "#6", "b7", "n3", "6+" (a cross),
    /// "7/" (a slash), "4_" (an extension line). Null with the reason when it cannot be read.
    /// </summary>
    public static List<ScoreFigure>? ParseFigures(string text, out string error)
    {
        error = "";
        var figures = new List<ScoreFigure>();
        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var figure = new ScoreFigure();
            var rest = token;
            if (rest.EndsWith('_')) (figure.Extend, rest) = (true, rest[..^1]);
            static string? Sign(char c) => c switch { '#' => "sharp", 'b' => "flat", 'n' => "natural", _ => null };
            if (rest.Length > 0 && Sign(rest[0]) is { } prefix) (figure.Prefix, rest) = (prefix, rest[1..]);
            if (rest.Length > 0 && rest[^1] is '+' or '/' or '\\' or '#' or 'b' or 'n')
            {
                figure.Suffix = rest[^1] switch { '+' => "cross", '/' => "slash", '\\' => "backslash", var c => Sign(c)! };
                rest = rest[..^1];
            }

            if (rest.Length > 4 || rest.Any(c => !char.IsDigit(c)) || (rest.Length == 0 && figure.Prefix.Length == 0 && figure.Suffix.Length == 0))
            {
                error = $"'{token}'을(를) 읽을 수 없습니다. 6 4, #6, b7, 6+, 7/ 처럼 쓰세요.";
                return null;
            }

            figure.Number = rest;
            figures.Add(figure);
        }

        if (figures.Count is 0 or > 6) error = "숫자를 1~6개 쓰세요.";
        return figures.Count is 0 or > 6 ? null : figures;
    }

    /// <summary>Puts figured bass (see <see cref="ParseFigures"/>) at the selected chord or the cursor; empty text removes it.</summary>
    public EditResult SetFiguredBass(string text)
    {
        var at = MarkPlace;
        if (!IsValid(at)) return EditResult.Refused("숫자 저음을 넣을 자리를 먼저 누르세요.");
        List<ScoreFigure>? figures = null;
        if (text.Trim().Length > 0 && (figures = ParseFigures(text, out var error)) is null) return EditResult.Refused(error);
        return Change(() =>
        {
            var measure = MeasureAt(at);
            var removed = measure.FiguredBass.RemoveAll(f => f.Staff == at.Staff && new Rational(f.PositionNumerator, f.PositionDenominator) == at.Position);
            if (figures is null) return removed == 0 ? "그 자리에 지울 숫자 저음이 없습니다." : null;

            // An extension line runs to the end of the note it figures.
            var length = ChordAt(at) is { } chord ? Duration(chord[0]) : Rational.Zero;
            measure.FiguredBass.Add(new ScoreFiguredBass
            {
                Staff = at.Staff, PositionNumerator = (int)at.Position.Numerator, PositionDenominator = (int)at.Position.Denominator,
                DurationNumerator = figures.Any(f => f.Extend) ? (int)length.Numerator : 0, DurationDenominator = figures.Any(f => f.Extend) ? (int)length.Denominator : 1,
                Figures = figures,
            });
            measure.FiguredBass = measure.FiguredBass.OrderBy(f => f.Staff).ThenBy(f => new Rational(f.PositionNumerator, f.PositionDenominator)).ToList();
            return null;
        }, () => figures is null ? "숫자 저음을 지웠습니다." : $"숫자 저음 '{text.Trim()}'을(를) 넣었습니다.");
    }

    /// <summary>Puts a music-font symbol (a SMuFL name ScoreStudio draws) at the selected chord or the cursor, or takes it away.</summary>
    public EditResult ToggleSymbol(string name, bool above = true)
    {
        if (Smufl.Named(name) is null) return EditResult.Refused($"'{name}'은(는) 그릴 수 있는 기호가 아닙니다.");
        var at = MarkPlace;
        if (!IsValid(at)) return EditResult.Refused("기호를 넣을 자리를 먼저 누르세요.");
        bool Here(ScoreDirection d) => d.Kind == DirectionKind.Symbol && d.Text == name && d.Staff == at.Staff && new Rational(d.PositionNumerator, d.PositionDenominator) == at.Position;
        var had = MeasureAt(at).Directions.Any(Here);
        return Change(() =>
        {
            var measure = MeasureAt(at);
            if (had)
            {
                measure.Directions.RemoveAll(Here);
                return null;
            }

            measure.Directions.Add(new ScoreDirection
            {
                Kind = DirectionKind.Symbol, Text = name, Above = above, Staff = at.Staff,
                PositionNumerator = (int)at.Position.Numerator, PositionDenominator = (int)at.Position.Denominator,
            });
            measure.Directions = measure.Directions.OrderBy(d => d.Staff).ThenBy(d => new Rational(d.PositionNumerator, d.PositionDenominator)).ToList();
            return null;
        }, () => had ? $"기호 {name}을(를) 지웠습니다." : $"기호 {name}을(를) 넣었습니다.");
    }
}
