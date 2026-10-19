using ScoreStudio.Import;
using ScoreStudio.Models;

namespace ScoreStudio.Layout;

/// <summary>Articulations, slurs, dynamics, text and tempo marks, and hairpins (docs/SPRINT_FINALE_MARKS.md).</summary>
public static partial class ScoreLayoutEngine
{
    /// <summary>Slurs and hairpins that run past the end of a system; they go on at the start of the next.</summary>
    private sealed class PendingSpans
    {
        public List<PendingSlur> Slurs { get; } = new();
        public List<PendingHairpin> Hairpins { get; } = new();
    }

    private sealed record PendingSlur(int PartIndex, int EndStaff, int EndVoice, int EndMeasure, Rational EndPosition, bool Above);

    private sealed record PendingHairpin(int PartIndex, int Staff, int EndMeasure, Rational EndPosition, bool Crescendo, bool Above, double? Y);

    /// <summary>Thickness of a slur in its middle (Bravura's slurMidpointThickness), in staff spaces.</summary>
    private const double SlurThickness = 0.22;

    /// <summary>Bravura's hairpinThickness, in staff spaces.</summary>
    private const double HairpinThickness = 0.16;

    /// <summary>How far a hairpin opens, in staff spaces.</summary>
    private const double HairpinOpening = 1.1;

    /// <summary>Size of expression text and metronome marks, in staff spaces (about 10 points at the usual staff size).</summary>
    private const double ExpressionTextSize = 2.0;

    /// <summary>Gap between a mark and what it must clear, in staff spaces.</summary>
    private const double MarkGap = 0.4;

    /// <summary>
    /// Draws the articulations and fermatas of each chord and rest, and widens the chords' extents by them so that slurs
    /// and text placed later keep clear.
    /// </summary>
    private static void DrawArticulations(List<DrawItem> items, List<PlacedChord> placed, List<PlacedRest> rests, double[] staffTops, double sp)
    {
        for (var i = 0; i < placed.Count; i++)
        {
            var chord = placed[i];
            if (chord.Chord.Articulations.Count == 0) continue;
            var (top, bottom) = DrawChordArticulations(items, chord.Chord, chord.X, chord.Top, chord.Bottom, staffTops[chord.Slot], sp);
            placed[i] = chord with { Top = Math.Min(chord.Top, top), Bottom = Math.Max(chord.Bottom, bottom) };
        }

        foreach (var rest in rests.Where(r => r.Chord is { Articulations.Count: > 0 }))
            DrawChordArticulations(items, rest.Chord!, rest.X, rest.Top, rest.Bottom, staffTops[rest.Slot], sp);
    }

    /// <summary>
    /// Articulations go on the notehead side, away from the stem; with two voices, above the upper voice and below the
    /// lower one, past its stem. Nearest the note come staccato, staccatissimo and tenuto, which sit in a space when they
    /// fall inside the staff; then accent and marcato, outside the staff. A fermata goes above the staff (below it for a
    /// lower voice). Returns how far the chord now reaches up and down, without the fermata.
    /// </summary>
    private static (double Top, double Bottom) DrawChordArticulations(
        List<DrawItem> items, PreparedChord chord, double x, double chordTop, double chordBottom, double staffTop, double sp)
    {
        var staffBottom = staffTop + 4 * sp;
        var width = chord.IsRest ? Smufl.RestWidth(chord.IsMeasureRest ? NoteType.Whole : chord.Value.Type) : Smufl.NoteheadWidth(chord.Value.Type);
        var center = x + width * sp / 2;
        var (top, bottom) = (chordTop, chordBottom);
        var above = chord.VoiceRole != 0 ? chord.VoiceRole > 0 : chord.IsRest || !chord.StemUp;

        // The outermost notehead on that side, or the stem's end for a voice whose stem points there.
        double edge;
        if (chord.IsRest || chord.VoiceRole != 0) edge = above ? chordTop : chordBottom;
        else
        {
            var outer = above ? chord.Notes.Max(n => n.Position) : chord.Notes.Min(n => n.Position);
            edge = StaffY(staffTop, outer, sp) + (above ? -0.5 : 0.5) * sp;
        }

        var order = new[] { Articulation.Staccato, Articulation.Staccatissimo, Articulation.Tenuto, Articulation.Accent, Articulation.Marcato };
        foreach (var articulation in order.Where(chord.Articulations.Contains))
        {
            var (glyph, glyphWidth, height) = Smufl.Articulation(articulation, above);
            var h = height * sp;
            var near = edge + (above ? -MarkGap : MarkGap) * sp;
            double glyphTop;
            if (articulation is Articulation.Accent or Articulation.Marcato)
            {
                glyphTop = above ? Math.Min(near, staffTop - MarkGap * sp) - h : Math.Max(near, staffBottom + MarkGap * sp);
            }
            else
            {
                glyphTop = above ? near - h : near;
                // Inside the staff a small mark sits in the middle of the next space outwards, never on a line.
                var middle = glyphTop + h / 2;
                var position = 8 - 2 * (middle - staffTop) / sp;
                if (position is > -1 and < 9)
                {
                    var space = above ? Math.Ceiling(position) : Math.Floor(position);
                    if ((int)space % 2 == 0) space += above ? 1 : -1;
                    glyphTop = staffTop + (8 - space) * sp / 2 - h / 2;
                }
            }

            items.Add(new GlyphItem(glyph, center - glyphWidth * sp / 2, above ? glyphTop + h : glyphTop, 4 * sp));
            edge = above ? glyphTop : glyphTop + h;
            top = Math.Min(top, glyphTop);
            bottom = Math.Max(bottom, glyphTop + h);
        }

        if (chord.Articulations.Contains(Articulation.Fermata))
        {
            var fermataAbove = chord.VoiceRole >= 0;
            var (glyph, glyphWidth, height) = Smufl.Articulation(Articulation.Fermata, fermataAbove);
            var baseline = fermataAbove ? Math.Min(staffTop, top) - 0.5 * sp : Math.Max(staffBottom, bottom) + 0.5 * sp;
            // Slurs end inside the fermata, so it does not count towards the chord's extent.
            items.Add(new GlyphItem(glyph, center - glyphWidth * sp / 2, baseline, 4 * sp));
        }

        return (top, bottom);
    }

    /// <summary>
    /// Draws the slurs of one system from chord to chord. A slur curves above when either end's stem points down (below
    /// when both point up); with two voices, above the upper voice and below the lower. It rises to clear the chords in
    /// between. A slur whose last chord is in a later system runs to the end of this one and goes on in the next.
    /// </summary>
    private static void DrawSlurs(
        List<DrawItem> items, ScoreProject project, PlannedSystem plan, List<PlacedChord> placed, List<StaffSlot> slots, double[] staffTops,
        double contentLeft, double systemRight, PendingSpans pending, double sp)
    {
        if (plan.MeasureCount == 0) return;
        var lastMeasure = plan.Measures[^1].Index;

        PlacedChord? Find(int part, int staff, int voice, int measure, Rational at)
        {
            var slot = slots.FindIndex(s => s.PartIndex == part && s.StaffIndex == staff);
            return slot < 0 ? null : placed.FirstOrDefault(p => p.Slot == slot && p.Measure == measure && p.Chord.Voice == voice && p.Chord.Start == at);
        }

        var carried = pending.Slurs.ToList();
        pending.Slurs.Clear();
        foreach (var slur in carried)
        {
            var slot = slots.FindIndex(s => s.PartIndex == slur.PartIndex && s.StaffIndex == slur.EndStaff);
            if (slot < 0) continue;
            if (slur.EndMeasure > lastMeasure)
            {
                var y = slur.Above ? staffTops[slot] - sp : staffTops[slot] + 5 * sp;
                AddSlur(items, contentLeft, y, systemRight - 0.5 * sp, y, slur.Above, new List<(double, double)>(), sp);
                pending.Slurs.Add(slur);
                continue;
            }

            var end = Find(slur.PartIndex, slur.EndStaff, slur.EndVoice, slur.EndMeasure, slur.EndPosition);
            if (end is null) continue;
            var (endX, endY) = SlurEnd(end, slur.Above, sp);
            AddSlur(items, contentLeft, endY, endX, endY, slur.Above, Obstacles(placed, end.Slot, null, end, slur.Above, sp), sp);
        }

        foreach (var planned in plan.Measures)
        {
            for (var p = 0; p < project.Parts.Count; p++)
            {
                if (planned.Index >= project.Parts[p].Measures.Count) continue;
                foreach (var slur in project.Parts[p].Measures[planned.Index].Slurs)
                {
                    var start = Find(p, slur.Staff, slur.Voice, planned.Index, new Rational(slur.PositionNumerator, slur.PositionDenominator));
                    if (start is null) continue;
                    var endMeasure = planned.Index + slur.EndMeasureOffset;
                    var endPosition = new Rational(slur.EndPositionNumerator, slur.EndPositionDenominator);
                    var end = endMeasure <= lastMeasure ? Find(p, slur.EndStaff, slur.EndVoice, endMeasure, endPosition) : null;
                    if (end is null && endMeasure <= lastMeasure) continue;

                    var above = slur.Above
                        ?? (start.Chord.VoiceRole != 0 ? start.Chord.VoiceRole > 0 : !(start.Chord.StemUp && (end?.Chord.StemUp ?? true)));
                    var (startX, startY) = SlurEnd(start, above, sp);
                    if (end is null)
                    {
                        AddSlur(items, startX, startY, systemRight - 0.5 * sp, startY, above, Obstacles(placed, start.Slot, start, null, above, sp), sp);
                        pending.Slurs.Add(new PendingSlur(p, slur.EndStaff, slur.EndVoice, endMeasure, endPosition, above));
                        continue;
                    }

                    var (endX, endY) = SlurEnd(end, above, sp);
                    var obstacles = Obstacles(placed, start.Slot, start, end, above, sp);
                    if (end.Slot != start.Slot) obstacles.AddRange(Obstacles(placed, end.Slot, start, end, above, sp));
                    AddSlur(items, startX, startY, Math.Max(endX, startX + sp), endY, above, obstacles, sp);
                }
            }
        }
    }

    /// <summary>Where a slur meets a chord: over (or under) the middle of its notehead, clear of everything drawn for it.</summary>
    private static (double X, double Y) SlurEnd(PlacedChord chord, bool above, double sp) =>
        (chord.X + Smufl.NoteheadWidth(chord.Chord.Value.Type) * sp / 2, above ? chord.Top - 0.5 * sp : chord.Bottom + 0.5 * sp);

    /// <summary>The chords on a staff strictly between two chords (null: the system's start or end), as X and the edge to clear.</summary>
    private static List<(double X, double Limit)> Obstacles(List<PlacedChord> placed, int slot, PlacedChord? from, PlacedChord? to, bool above, double sp) =>
        placed.Where(p => p.Slot == slot && (from is null || IsAfter(p, from)) && (to is null || IsAfter(to, p)))
            .Select(p => (p.X + Smufl.NoteheadWidth(p.Chord.Value.Type) * sp / 2, above ? p.Top : p.Bottom))
            .ToList();

    /// <summary>
    /// A slur from (x1, y1) to (x2, y2): its height grows with its length, and more when a chord in between would poke
    /// through. With both control points at a quarter of the way in, the curve is 3t(1 − t) of the control height above
    /// the chord between its ends at fraction t.
    /// </summary>
    private static void AddSlur(List<DrawItem> items, double x1, double y1, double x2, double y2, bool above, List<(double X, double Limit)> obstacles, double sp)
    {
        var width = x2 - x1;
        if (width <= 0) return;
        var control = Math.Clamp(width * 0.1, 0.8 * sp, 2.5 * sp) * 4 / 3;
        foreach (var (x, limit) in obstacles)
        {
            var t = (x - x1) / width;
            if (t is <= 0.05 or >= 0.95) continue;
            var line = y1 + (y2 - y1) * t;
            var need = (above ? line - (limit - MarkGap * sp) : (limit + MarkGap * sp) - line) / (3 * t * (1 - t));
            control = Math.Max(control, need);
        }

        control = Math.Min(control, 8 * sp) * (above ? -1 : 1);
        items.Add(new CurveItem(x1, y1, x1 + width / 4, y1 + control, x2 - width / 4, y2 + control, x2, y2, SlurThickness * sp));
    }

    /// <summary>
    /// Draws the dynamics, text and tempo marks and the hairpins of one system. Marks with a height from the source keep
    /// it. Otherwise dynamics and hairpins share one row per staff, below it (always above it on a staff with lyrics), clear of
    /// the lowest (highest) notes in the system; text and tempo marks go above the staff, clear of the notes under them.
    /// Above the staff they also clear its chord symbols.
    /// Marks that would overlap on a row move right.
    /// </summary>
    private static void DrawDirectionsAndHairpins(
        List<DrawItem> items, ScoreProject project, PlannedSystem plan, List<MeasureLayout> measureLayouts, List<PlacedChord> placed,
        List<StaffSlot> slots, double[] staffTops, Dictionary<int, List<(Rational Time, double X)>> anchors, Dictionary<int, double> symbolTops,
        double contentLeft, double systemRight, PendingSpans pending, double sp)
    {
        var textSize = ExpressionTextSize * sp;

        // Marks above a staff go over its chord symbols as well as its notes.
        double Highest(int slot, double top) => symbolTops.TryGetValue(slot, out var symbolTop) ? Math.Min(top, symbolTop - 0.3 * sp) : top;
        var lyricSlots = placed.Where(p => p.Chord.Lyrics.Count > 0).Select(p => p.Slot).ToHashSet();
        double RowBaseline(int slot, bool above)
        {
            var chords = placed.Where(p => p.Slot == slot).ToList();
            return above
                ? Highest(slot, Math.Min(staffTops[slot] - 1.5 * sp, chords.Select(c => c.Top).DefaultIfEmpty(staffTops[slot]).Min() - 1.2 * sp))
                : Math.Max(staffTops[slot] + 7 * sp, chords.Select(c => c.Bottom).DefaultIfEmpty(staffTops[slot]).Max() + 2.2 * sp);
        }

        // Lyrics take the room below a staff, so there dynamics go above unless the source gives their height.
        bool RowAbove(bool? above, double? y, int slot) => lyricSlots.Contains(slot) && y is null || (above ?? false);

        var taken = new List<(int Slot, bool Above, double Baseline, double Left, double Right)>();
        double Free(int slot, bool above, double baseline, double left, double width)
        {
            var moved = true;
            while (moved)
            {
                moved = false;
                foreach (var t in taken.Where(t => t.Slot == slot && t.Above == above && Math.Abs(t.Baseline - baseline) < 1.5 * sp))
                {
                    if (left < t.Right + 0.6 * sp && left + width > t.Left - 0.6 * sp)
                    {
                        left = t.Right + 0.6 * sp;
                        moved = true;
                    }
                }
            }

            taken.Add((slot, above, baseline, left, left + width));
            return left;
        }

        // Dynamics in each measure, so a hairpin can start after the one at its start and stop before the one at its end.
        var dynamics = new List<(int Slot, int Measure, Rational Position, double Left, double Right)>();

        foreach (var planned in plan.Measures)
        {
            if (!anchors.TryGetValue(planned.Index, out var measureAnchors)) continue;
            for (var p = 0; p < project.Parts.Count; p++)
            {
                if (planned.Index >= project.Parts[p].Measures.Count) continue;
                foreach (var direction in project.Parts[p].Measures[planned.Index].Directions)
                {
                    var slot = slots.FindIndex(s => s.PartIndex == p && s.StaffIndex == direction.Staff);
                    if (slot < 0) continue;
                    var position = new Rational(direction.PositionNumerator, direction.PositionDenominator);
                    var x = XAtTime(position, measureAnchors);
                    var staffTop = staffTops[slot];

                    if (direction.Kind == DirectionKind.Dynamic)
                    {
                        var above = RowAbove(direction.Above, direction.Y, slot);
                        var letters = direction.Text.Select(Smufl.DynamicLetter).ToList();
                        var glyphs = letters.All(l => l is not null);
                        var width = glyphs ? letters.Sum(l => l!.Value.Width) * sp : TextMetrics.Width(direction.Text, textSize);
                        var baseline = direction.Y is { } y ? staffTop - y * sp : RowBaseline(slot, above);
                        var left = Free(slot, above, baseline, x + Smufl.NoteheadWidth(NoteType.Quarter) * sp / 2 - width / 2, width);
                        if (glyphs)
                        {
                            var glyphX = left;
                            foreach (var letter in letters)
                            {
                                items.Add(new GlyphItem(letter!.Value.Glyph, glyphX, baseline, 4 * sp));
                                glyphX += letter.Value.Width * sp;
                            }
                        }
                        else
                        {
                            items.Add(new TextItem(direction.Text, left, baseline, textSize, TextAnchor.Start, Italic: true));
                        }

                        dynamics.Add((slot, planned.Index, position, left, left + width));
                    }
                    else
                    {
                        var above = direction.Above ?? true;
                        var (note, noteWidth) = Smufl.MetronomeNote(direction.BeatUnit);
                        var numberText = $" = {direction.PerMinute}";
                        const double noteScale = 0.6;
                        var width = direction.Kind == DirectionKind.Tempo
                            ? (noteWidth + direction.BeatUnitDots * 0.6) * noteScale * sp + TextMetrics.Width(numberText, textSize)
                            : TextMetrics.Width(direction.Text, textSize) * (direction.Bold ? 1.1 : 1);
                        double baseline;
                        if (direction.Y is { } y) baseline = staffTop - y * sp;
                        else
                        {
                            var under = placed.Where(c => c.Slot == slot && c.X + sp > x && c.X < x + width + sp).ToList();
                            baseline = above
                                ? Highest(slot, Math.Min(staffTop - 1.5 * sp, under.Select(c => c.Top).DefaultIfEmpty(staffTop).Min() - 0.8 * sp))
                                : Math.Max(staffTop + 6.5 * sp, under.Select(c => c.Bottom).DefaultIfEmpty(staffTop).Max() + 2.2 * sp);
                        }

                        var left = Free(slot, above, baseline, x, width);
                        if (direction.Kind == DirectionKind.Words)
                        {
                            items.Add(new TextItem(direction.Text, left, baseline, textSize, TextAnchor.Start, direction.Bold, Italic: direction.Italic));
                            continue;
                        }

                        // The note's head is centred on its baseline, so it sits a little above the text's baseline.
                        var noteBaseline = baseline - 0.35 * sp;
                        items.Add(new GlyphItem(note, left, noteBaseline, 4 * sp * noteScale));
                        var after = left + noteWidth * noteScale * sp;
                        for (var d = 0; d < direction.BeatUnitDots; d++)
                        {
                            items.Add(new GlyphItem(Smufl.MetronomeDot, after + 0.2 * noteScale * sp, noteBaseline, 4 * sp * noteScale));
                            after += 0.6 * noteScale * sp;
                        }

                        items.Add(new TextItem(numberText, after, baseline, textSize, TextAnchor.Start));
                    }
                }
            }
        }

        DrawHairpins(items, project, plan, measureLayouts, slots, staffTops, anchors, dynamics, lyricSlots, RowBaseline, contentLeft, systemRight, pending, sp);
    }

    private static void DrawHairpins(
        List<DrawItem> items, ScoreProject project, PlannedSystem plan, List<MeasureLayout> measureLayouts, List<StaffSlot> slots, double[] staffTops,
        Dictionary<int, List<(Rational Time, double X)>> anchors, List<(int Slot, int Measure, Rational Position, double Left, double Right)> dynamics,
        HashSet<int> lyricSlots, Func<int, bool, double> rowBaseline, double contentLeft, double systemRight, PendingSpans pending, double sp)
    {
        if (plan.MeasureCount == 0) return;
        var lastMeasure = plan.Measures[^1].Index;

        // The end of a hairpin: at its time, before a dynamic there, or short of the barline when it ends with the measure.
        double EndX(int slot, int measure, Rational position)
        {
            var measureAnchors = anchors[measure];
            if (dynamics.FirstOrDefault(d => d.Slot == slot && d.Measure == measure && d.Position == position) is { Right: > 0 } dynamic)
                return dynamic.Left - 0.6 * sp;
            if (position >= measureAnchors[^1].Time && measureLayouts.FirstOrDefault(m => m.MeasureIndex == measure) is { } layout)
                return layout.Right - 0.8 * sp;
            return XAtTime(position, measureAnchors);
        }

        double Center(int slot, bool above, double? y) => y is { } height ? staffTops[slot] - height * sp : rowBaseline(slot, above) - 0.5 * sp;

        var carried = pending.Hairpins.ToList();
        pending.Hairpins.Clear();
        foreach (var hairpin in carried)
        {
            var slot = slots.FindIndex(s => s.PartIndex == hairpin.PartIndex && s.StaffIndex == hairpin.Staff);
            if (slot < 0) continue;
            var center = Center(slot, hairpin.Above, hairpin.Y);
            var (open, close) = hairpin.Crescendo ? (0.5, 1.0) : (0.5, 0.0);
            if (hairpin.EndMeasure > lastMeasure)
            {
                AddHairpin(items, contentLeft, systemRight - 0.5 * sp, center, open, open, sp);
                pending.Hairpins.Add(hairpin);
                continue;
            }

            AddHairpin(items, contentLeft, Math.Max(EndX(slot, hairpin.EndMeasure, hairpin.EndPosition), contentLeft + 2 * sp), center, open, close, sp);
        }

        foreach (var planned in plan.Measures)
        {
            if (!anchors.TryGetValue(planned.Index, out var measureAnchors)) continue;
            for (var p = 0; p < project.Parts.Count; p++)
            {
                if (planned.Index >= project.Parts[p].Measures.Count) continue;
                foreach (var hairpin in project.Parts[p].Measures[planned.Index].Hairpins)
                {
                    var slot = slots.FindIndex(s => s.PartIndex == p && s.StaffIndex == hairpin.Staff);
                    if (slot < 0) continue;
                    var position = new Rational(hairpin.PositionNumerator, hairpin.PositionDenominator);
                    var startX = dynamics.FirstOrDefault(d => d.Slot == slot && d.Measure == planned.Index && d.Position == position) is { Right: > 0 } dynamic
                        ? dynamic.Right + 0.6 * sp
                        : XAtTime(position, measureAnchors);
                    var above = lyricSlots.Contains(slot) && hairpin.Y is null || (hairpin.Above ?? false);
                    var center = Center(slot, above, hairpin.Y);
                    var endMeasure = planned.Index + hairpin.EndMeasureOffset;
                    var endPosition = new Rational(hairpin.EndPositionNumerator, hairpin.EndPositionDenominator);
                    var (open, close) = hairpin.Crescendo ? (0.0, 1.0) : (1.0, 0.0);

                    if (endMeasure > lastMeasure)
                    {
                        AddHairpin(items, startX, Math.Max(systemRight - 0.5 * sp, startX + 2 * sp), center, open, 0.5, sp);
                        pending.Hairpins.Add(new PendingHairpin(p, hairpin.Staff, endMeasure, endPosition, hairpin.Crescendo, above, hairpin.Y));
                        continue;
                    }

                    AddHairpin(items, startX, Math.Max(EndX(slot, endMeasure, endPosition), startX + 2 * sp), center, open, close, sp);
                }
            }
        }
    }

    /// <summary>A hairpin from x1 to x2 around a centre line, opening from <paramref name="open"/> to <paramref name="close"/> of its full spread.</summary>
    private static void AddHairpin(List<DrawItem> items, double x1, double x2, double center, double open, double close, double sp)
    {
        var (start, end) = (open * HairpinOpening * sp / 2, close * HairpinOpening * sp / 2);
        items.Add(new LineItem(x1, center - start, x2, center - end, HairpinThickness * sp));
        items.Add(new LineItem(x1, center + start, x2, center + end, HairpinThickness * sp));
    }
}
