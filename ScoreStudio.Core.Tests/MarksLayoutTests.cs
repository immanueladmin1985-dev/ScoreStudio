using ScoreStudio.Layout;
using ScoreStudio.Models;

namespace ScoreStudio.Core.Tests;

/// <summary>Drawing of articulations, slurs, dynamics, text, tempo marks and hairpins (docs/SPRINT_FINALE_MARKS.md, 2-B).</summary>
public sealed class MarksLayoutTests
{
    private const double Sp = 1.75;

    private static ScoreProject Score(int measures, params ScoreEvent[] firstMeasure)
    {
        var project = new ScoreProject
        {
            Title = "기호",
            Parts = { new ScorePart { Name = "", Measures = Enumerable.Range(1, measures).Select(n => new ScoreMeasure { Number = n }).ToList() } },
        };
        project.Parts[0].Measures[0].Events.AddRange(firstMeasure);
        return project;
    }

    private static ScoreEvent Note(string spelling, int midi, int beat, int length = 1) =>
        new() { MidiPitch = midi, NotationSpelling = spelling, PositionNumerator = beat, DurationNumerator = length };

    private static (PageLayout Page, double StaffTop) Layout(ScoreProject project)
    {
        var layout = ScoreLayoutEngine.Layout(project);
        return (layout.Pages[0], layout.Pages[0].Systems[0].Staves[0].Top);
    }

    /// <summary>Hairpins are the only slanted lines on these pages: staff lines are level and barlines and stems upright.</summary>
    private static List<LineItem> HairpinLines(PageLayout page) =>
        page.Items.OfType<LineItem>().Where(l => l.X1 != l.X2 && l.Y1 != l.Y2).OrderBy(l => l.X1).ToList();

    private static GlyphItem Glyph(PageLayout page, char glyph) => Assert.Single(page.Items.OfType<GlyphItem>(), g => g.Glyph == glyph);

    [Fact]
    public void Staccato_SitsInASpaceAwayFromTheStem_AndAccentGoesOutsideTheStaff()
    {
        // B4 (middle line) has its stem down, so its marks go above; G4 (stem up) gets them below.
        var high = Note("B4", 71, 0);
        high.Articulations.AddRange(new[] { Articulation.Staccato, Articulation.Accent });
        var low = Note("G4", 67, 1);
        low.Articulations.Add(Articulation.Staccato);
        var (page, top) = Layout(Score(1, high, low));

        var staccatoAbove = Glyph(page, Smufl.Articulation(Articulation.Staccato, true).Glyph);
        var middle = staccatoAbove.Y - 0.336 * Sp / 2;
        Assert.True(middle < top + 2 * Sp - 0.5 * Sp, "the staccato is above the notehead");
        Assert.Equal(0.5, Math.Abs((middle - top) / Sp % 1), 3);

        var accent = Glyph(page, Smufl.Articulation(Articulation.Accent, true).Glyph);
        Assert.True(accent.Y <= top, "the accent is above the staff");
        Assert.True(accent.Y < staccatoAbove.Y - 0.336 * Sp, "the accent is outside the staccato");

        var staccatoBelow = Glyph(page, Smufl.Articulation(Articulation.Staccato, false).Glyph);
        Assert.True(staccatoBelow.Y > top + 3 * Sp, "the staccato of the stem-up note is below its notehead");
    }

    [Fact]
    public void Fermata_IsAboveTheStaff()
    {
        var note = Note("C4", 60, 0, 4);
        note.Articulations.Add(Articulation.Fermata);
        var (page, top) = Layout(Score(1, note));

        Assert.True(Glyph(page, Smufl.Articulation(Articulation.Fermata, true).Glyph).Y <= top - 0.5 * Sp + 1e-9);
    }

    [Fact]
    public void Dynamic_IsDrawnInDynamicLettersBelowTheStaff_OrAboveItWhenTheStaffHasLyrics()
    {
        var project = Score(1, Note("C5", 72, 0, 4));
        project.Parts[0].Measures[0].Directions.Add(new ScoreDirection { Kind = DirectionKind.Dynamic, Text = "mf" });
        var (page, top) = Layout(project);

        var m = Glyph(page, Smufl.DynamicLetter('m')!.Value.Glyph);
        var f = Glyph(page, Smufl.DynamicLetter('f')!.Value.Glyph);
        Assert.Equal(m.Y, f.Y);
        Assert.Equal(m.X + 1.748 * Sp, f.X, 6);
        Assert.True(m.Y > top + 4 * Sp, "below the staff");

        project.Parts[0].Measures[0].Events[0].Lyrics.Add(new ScoreLyric { Text = "아" });
        (page, top) = Layout(project);
        Assert.True(Glyph(page, Smufl.DynamicLetter('m')!.Value.Glyph).Y < top, "above a staff with lyrics");
    }

    [Fact]
    public void WordsAndTempo_AreAboveTheStaffSideBySide()
    {
        var project = Score(1, Note("C5", 72, 0, 4));
        project.Parts[0].Measures[0].Directions.Add(new ScoreDirection { Kind = DirectionKind.Words, Text = "Andante", Bold = true });
        project.Parts[0].Measures[0].Directions.Add(new ScoreDirection { Kind = DirectionKind.Tempo, BeatUnit = "quarter", PerMinute = "76" });
        var (page, top) = Layout(project);

        var words = Assert.Single(page.Items.OfType<TextItem>(), t => t.Text == "Andante");
        Assert.True(words.Bold && !words.Italic && words.Y < top);
        var note = Glyph(page, Smufl.MetronomeNote("quarter").Glyph);
        Assert.True(note.X > words.X + TextMetrics.Width("Andante", words.Size), "the metronome mark follows the text");
        Assert.Contains(page.Items.OfType<TextItem>(), t => t.Text == " = 76" && t.X > note.X);
    }

    [Fact]
    public void Crescendo_OpensFromAPointToItsEnd_AndContinuesIntoTheNextSystem()
    {
        var project = Score(2, Note("C5", 72, 0, 4));
        project.Parts[0].Measures[0].Hairpins.Add(new ScoreHairpin { Crescendo = true, PositionNumerator = 0, EndMeasureOffset = 0, EndPositionNumerator = 4 });
        project.Parts[0].Measures[1].Hairpins.Add(new ScoreHairpin { Crescendo = false, PositionNumerator = 0, EndMeasureOffset = 0, EndPositionNumerator = 2 });
        var (page, top) = Layout(project);

        var lines = HairpinLines(page);
        Assert.Equal(4, lines.Count);
        Assert.All(lines, l => Assert.True(l.Y1 > top + 4 * Sp, "below the staff"));
        var (cresc, dim) = (lines.Take(2).ToList(), lines.Skip(2).ToList());
        Assert.Equal(cresc[0].Y1, cresc[1].Y1, 6);
        Assert.Equal(1.1 * Sp, Math.Abs(cresc[0].Y2 - cresc[1].Y2), 6);
        Assert.Equal(dim[0].Y2, dim[1].Y2, 6);
        Assert.Equal(1.1 * Sp, Math.Abs(dim[0].Y1 - dim[1].Y1), 6);

        // A hairpin to the third measure of a score broken after the first goes on in the second system.
        project = Score(3, Note("C5", 72, 0, 4));
        project.Parts[0].Measures[1].SystemBreak = true;
        project.Parts[0].Measures[0].Hairpins.Add(new ScoreHairpin { PositionNumerator = 0, EndMeasureOffset = 2, EndPositionNumerator = 2 });
        var layout = ScoreLayoutEngine.Layout(project);
        var systems = layout.Pages[0].Systems;
        var hairpinLines = HairpinLines(layout.Pages[0]);
        Assert.Contains(hairpinLines, l => l.Y1 > systems[0].Top && l.Y1 < systems[1].Top);
        Assert.Contains(hairpinLines, l => l.Y1 > systems[1].Top);
    }

    [Fact]
    public void Slur_CurvesOverTheNotesBetweenItsEnds()
    {
        // Stems down (notes above the middle line): the slur goes above and clears the high note in the middle.
        var project = Score(1, Note("C5", 72, 0), Note("A5", 81, 1), Note("D5", 74, 2));
        project.Parts[0].Measures[0].Slurs.Add(new ScoreSlur { PositionNumerator = 0, EndPositionNumerator = 2 });
        var (page, top) = Layout(project);

        var slur = Assert.Single(page.Items.OfType<CurveItem>());
        Assert.True(slur.ControlY1 < slur.Y1, "the slur curves upwards");
        var peak = (slur.Y1 + 3 * slur.ControlY1 + 3 * slur.ControlY2 + slur.Y2) / 8;
        var a5 = top - Sp; // A5 is on the first ledger line above the treble staff
        Assert.True(peak < a5 - 0.5 * Sp, "the slur clears the A5 notehead");
    }

    [Fact]
    public void Slur_ToALaterSystem_IsDrawnInBothSystems()
    {
        var project = Score(2, Note("C5", 72, 0, 4));
        project.Parts[0].Measures[1].Events.Add(Note("D5", 74, 0, 4));
        project.Parts[0].Measures[1].SystemBreak = true;
        project.Parts[0].Measures[0].Slurs.Add(new ScoreSlur { PositionNumerator = 0, EndMeasureOffset = 1, EndPositionNumerator = 0 });
        var layout = ScoreLayoutEngine.Layout(project);

        Assert.Equal(2, layout.Pages[0].Systems.Count);
        Assert.Equal(2, layout.Pages[0].Items.OfType<CurveItem>().Count());
    }
}
