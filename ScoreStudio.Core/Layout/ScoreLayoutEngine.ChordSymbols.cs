using System.Text;
using ScoreStudio.Import;
using ScoreStudio.Models;

namespace ScoreStudio.Layout;

public static partial class ScoreLayoutEngine
{
    /// <summary>A chord symbol placed at its beat, before its height is known.</summary>
    private sealed record PlacedSymbol(int Slot, int Measure, double X, ScoreChordSymbol Symbol);

    /// <summary>
    /// X of a point in time inside a measure: at a note onset where there is one, otherwise in proportion between the
    /// onsets (or the start and end of the measure) around it.
    /// </summary>
    private static double XAtTime(Rational time, List<(Rational Time, double X)> anchors)
    {
        if (time <= anchors[0].Time) return anchors[0].X;
        for (var i = 1; i < anchors.Count; i++)
        {
            if (time > anchors[i].Time) continue;
            var (t0, x0) = anchors[i - 1];
            var (t1, x1) = anchors[i];
            var span = (t1 - t0).ToDouble();
            return span <= 0 ? x1 : x0 + (x1 - x0) * (time - t0).ToDouble() / span;
        }

        return anchors[^1].X;
    }

    /// <summary>
    /// Draws the chord symbols of one system above their staves, starting at their beat. A staff's symbols share one
    /// baseline: the height the source gives most of them in this system, or else 2.5 spaces above the staff and clear
    /// of the highest note or stem.
    /// </summary>
    /// <returns>The top of the symbols on each staff that has any, so brackets above can clear them.</returns>
    private static Dictionary<int, double> DrawChordSymbols(
        List<DrawItem> items, List<PlacedSymbol> symbols, List<PlacedChord> placed, double[] staffTops, double size, double sp)
    {
        var tops = new Dictionary<int, double>();
        foreach (var staff in symbols.GroupBy(s => s.Slot))
        {
            var staffTop = staffTops[staff.Key];
            var sourceY = staff.Where(s => s.Symbol.Y is not null).GroupBy(s => s.Symbol.Y!.Value)
                .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).Select(g => (double?)g.Key).FirstOrDefault();
            var highest = placed.Where(p => p.Slot == staff.Key).Select(p => p.Top).DefaultIfEmpty(staffTop).Min();
            var baseline = sourceY is { } y ? staffTop - y * sp : Math.Min(staffTop - 2.5 * sp, highest - 0.6 * sp);

            var top = baseline - 0.75 * size;
            foreach (var symbol in staff)
            {
                var text = ChordDisplay(symbol.Symbol.Text);
                items.Add(new TextItem(text, symbol.X, baseline, size, TextAnchor.Start));
                if (symbol.Symbol.Frame is { } frame)
                {
                    // Finger numbers go between the diagram and the symbol, so they need room there.
                    var gap = frame.Notes.Any(n => n.Fingering.Length > 0) ? 2.0 : 0.8;
                    top = Math.Min(top, DrawFrame(items, frame, symbol.X + TextMetrics.Width(text, size) / 2, baseline - 0.75 * size - gap * sp, sp));
                }
            }

            tops[staff.Key] = top;
        }

        return tops;
    }

    private const double FrameStringSpace = 0.8;
    private const double FrameFretSpace = 0.9;

    /// <summary>
    /// A chord diagram (docs/FINALE_MARKS_3.md) centred on <paramref name="center"/> with its bottom at
    /// <paramref name="bottom"/>: strings (the highest on the right), frets with a thick nut when it starts at the nut,
    /// a dot for each fingered note, o and x above open and unplayed strings, a barre, the first fret's number beside
    /// it and finger numbers under it. Returns its top.
    /// </summary>
    private static double DrawFrame(List<DrawItem> items, ScoreFrame frame, double center, double bottom, double sp)
    {
        var width = (frame.Strings - 1) * FrameStringSpace * sp;
        var height = frame.Frets * FrameFretSpace * sp;
        var (left, top) = (center - width / 2, bottom - height);
        var line = Smufl.StaffLineThickness * sp;
        double StringX(int s) => left + (frame.Strings - s) * FrameStringSpace * sp;
        var firstFret = frame.FirstFret ?? 1;

        for (var s = 1; s <= frame.Strings; s++) items.Add(new LineItem(StringX(s), top, StringX(s), bottom, line));
        for (var f = 0; f <= frame.Frets; f++)
        {
            var y = top + f * FrameFretSpace * sp;
            items.Add(new LineItem(left, y, left + width, y, f == 0 && frame.FirstFret is null ? 3 * line : line));
        }

        if (frame.FirstFret is { } fret)
            items.Add(new TextItem($"{fret}fr", left + width + 0.4 * sp, top + 0.8 * sp, 1.6 * sp, TextAnchor.Start));

        var markY = top - 0.6 * sp;
        for (var s = 1; s <= frame.Strings; s++)
        {
            var notes = frame.Notes.Where(n => n.String == s).ToList();
            if (notes.Count == 0) items.Add(new GlyphItem(Smufl.FretboardMuted, StringX(s), markY, 4 * sp));
            else if (notes.Any(n => n.Fret == 0)) items.Add(new GlyphItem(Smufl.FretboardOpen, StringX(s), markY, 4 * sp));
        }

        foreach (var note in frame.Notes.Where(n => n.Fret > 0))
        {
            var y = top + (note.Fret - firstFret + 0.5) * FrameFretSpace * sp;
            if (y < top || y > bottom) continue;
            items.Add(new GlyphItem(Smufl.FretboardDot, StringX(note.String), y, 4 * sp * 1.4));
            if (note.Fingering.Length > 0)
                items.Add(new TextItem(note.Fingering, StringX(note.String), bottom + 1.3 * sp, 1.4 * sp, TextAnchor.Middle));
        }

        // A barre: a thick line across the strings from its start to its stop.
        foreach (var start in frame.Notes.Where(n => n.Barre == "start"))
        {
            var stop = frame.Notes.FirstOrDefault(n => n.Barre == "stop" && n.Fret == start.Fret) ?? frame.Notes.MinBy(n => n.String)!;
            var y = top + (start.Fret - firstFret + 0.5) * FrameFretSpace * sp;
            if (y >= top && y <= bottom) items.Add(new LineItem(StringX(start.String), y, StringX(stop.String), y, 0.5 * sp));
        }

        return markY - 0.4 * sp;
    }

    /// <summary>The printed symbol with real flat and sharp signs after the root and the bass ("Bb/D" → "B♭/D").</summary>
    public static string ChordDisplay(string text)
    {
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            result.Append(text[i]);
            var isNote = text[i] is >= 'A' and <= 'G' && (i == 0 || text[i - 1] == '/');
            while (isNote && i + 1 < text.Length && text[i + 1] is 'b' or '#')
            {
                i++;
                result.Append(text[i] == 'b' ? '♭' : '♯');
            }
        }

        return result.ToString();
    }
}
