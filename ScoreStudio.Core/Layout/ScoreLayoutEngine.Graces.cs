namespace ScoreStudio.Layout;

/// <summary>Grace notes: small notes with stems up before (or after) their note (docs/GRACE_NOTES.md).</summary>
public static partial class ScoreLayoutEngine
{
    /// <summary>
    /// Draws a chord's or rest's grace notes. Those before it end just left of its accidentals; those after it start just
    /// right of it. A single grace chord has a flag (and a slash for an acciaccatura); two or more short ones share beams.
    /// </summary>
    /// <param name="ownLeft">How far the chord itself reaches left of <paramref name="x"/> (accidentals, heads), in mm.</param>
    /// <param name="ownRight">Where the chord itself ends on the right, in mm.</param>
    private static void DrawGraces(List<DrawItem> items, PreparedChord chord, double x, double ownLeft, double ownRight, double staffTop, double sp)
    {
        if (chord.Graces.Count == 0) return;
        foreach (var after in new[] { false, true })
        {
            var columns = NotePreparation.GraceColumns(chord.Graces.Where(g => g.After == after).ToList());
            if (columns.Count == 0) continue;

            // Left edges of the columns, in order.
            var widths = columns.Select(c => NotePreparation.GraceColumnWidth(c) * sp).ToList();
            var lefts = new List<double>();
            var edge = after ? ownRight + 0.3 * sp : x - ownLeft - 0.3 * sp - widths.Sum();
            foreach (var width in widths)
            {
                lefts.Add(edge);
                edge += width;
            }

            DrawGraceGroup(items, columns, lefts, staffTop, sp);
        }
    }

    private static void DrawGraceGroup(List<DrawItem> items, List<List<PreparedGrace>> columns, List<double> lefts, double staffTop, double sp)
    {
        const double g = NotePreparation.GraceScale;
        var thickness = Smufl.StemThickness * sp * g;
        var headWidth = Smufl.NoteheadWidth(NoteType.Quarter) * sp * g;
        var beamed = columns.Count >= 2 && columns.All(c => c.All(n => n.Type >= NoteType.Eighth));
        var stems = new List<(double X, double Top, double Bottom)>();

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var accidentalRoom = column.Any(n => n.Accidental is not null) ? (NotePreparation.AccidentalColumnWidth + 0.1) * g * sp : 0;
            var headX = lefts[i] + accidentalRoom;
            var low = column.Min(n => n.Position);
            var high = column.Max(n => n.Position);

            for (var p = -2; p >= low; p -= 2)
                items.Add(new LineItem(headX - 0.3 * g * sp, StaffY(staffTop, p, sp), headX + headWidth + 0.3 * g * sp, StaffY(staffTop, p, sp), Smufl.LegerLineThickness * sp * g));
            for (var p = 10; p <= high; p += 2)
                items.Add(new LineItem(headX - 0.3 * g * sp, StaffY(staffTop, p, sp), headX + headWidth + 0.3 * g * sp, StaffY(staffTop, p, sp), Smufl.LegerLineThickness * sp * g));

            foreach (var note in column)
            {
                var y = StaffY(staffTop, note.Position, sp);
                items.Add(new GlyphItem(Smufl.Notehead(note.Type <= NoteType.Half ? NoteType.Half : NoteType.Quarter), headX, y, 4 * sp * g));
                if (note.Accidental is { } alter)
                    items.Add(new GlyphItem(Smufl.Accidental(alter), headX - (Smufl.AccidentalWidth(alter) + 0.15) * g * sp, y, 4 * sp * g));
            }

            var stemX = headX + headWidth - thickness / 2;
            stems.Add((stemX, StaffY(staffTop, high, sp) - 3.0 * sp * g, StaffY(staffTop, low, sp) - Smufl.StemAttachY * sp * g));
        }

        if (beamed)
        {
            // One level beam over all the stems, as many beams as the shortest value needs.
            var beamY = stems.Min(s => s.Top);
            foreach (var (stemX, _, bottom) in stems) items.Add(new LineItem(stemX, bottom, stemX, beamY, thickness));
            var levels = columns.Min(c => c.Min(n => n.Type)) - NoteType.Quarter;
            for (var level = 0; level < levels; level++)
            {
                var y = beamY + level * (BeamThickness + BeamSpacing) * sp * g;
                items.Add(new BeamItem(stems[0].X - thickness / 2, y, stems[^1].X + thickness / 2, y, BeamThickness * sp * g));
            }

            return;
        }

        for (var i = 0; i < columns.Count; i++)
        {
            var (stemX, top, bottom) = stems[i];
            var type = columns[i].Min(n => n.Type);
            if (type <= NoteType.Whole) continue;
            items.Add(new LineItem(stemX, bottom, stemX, top, thickness));
            if (Smufl.Flag(type, true) is { } flag)
                items.Add(new GlyphItem(flag, stemX - thickness / 2, top + Smufl.FlagAnchorY(type, true) * sp * g, 4 * sp * g));
            if (columns[i].Any(n => n.Slash))
                items.Add(new LineItem(stemX - 0.7 * g * sp, top + 1.9 * g * sp, stemX + 1.0 * g * sp, top + 0.5 * g * sp, thickness));
        }
    }
}
