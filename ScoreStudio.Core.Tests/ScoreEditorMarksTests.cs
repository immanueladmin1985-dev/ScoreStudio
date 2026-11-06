using ScoreStudio.Editing;
using ScoreStudio.Import;
using ScoreStudio.Models;

namespace ScoreStudio.Core.Tests;

/// <summary>Putting marks in with the Articulation, Expression and Smart Shape tools (docs/SPRINT_FINALE_MARKS.md, 2-C).</summary>
public sealed class ScoreEditorMarksTests
{
    private static Rational R(long n, long d = 1) => new(n, d);

    /// <summary>Four quarters C D E F in the first measure and G A B C in the second; nothing selected.</summary>
    private static ScoreEditor Editor()
    {
        var editor = new ScoreEditor(ScoreProject.CreateNew());
        foreach (var letter in "CDEFGABC") Assert.True(editor.EnterNote(letter).Done);
        editor.ClearSelection();
        return editor;
    }

    private static EditPosition At(int measure, int beat) => new(0, 0, 1, measure, R(beat));

    private static ScoreMeasure Measure(ScoreEditor editor, int index) => editor.Project.Parts[0].Measures[index];

    [Fact]
    public void EveryArticulation_HasItsOwnName()
    {
        // The Articulation tool's menu lists every mark by this name.
        var names = Enum.GetValues<Articulation>().Select(ScoreEditor.ArticulationName).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void Articulation_IsAddedToTheSelectedNote_TakenOffWhenAddedAgain_AndUndone()
    {
        var editor = Editor();
        editor.Select(At(0, 1));

        Assert.True(editor.ToggleArticulation(Articulation.Staccato).Done);
        Assert.True(editor.ToggleArticulation(Articulation.Accent).Done);
        Assert.Equal(new[] { Articulation.Staccato, Articulation.Accent }, Measure(editor, 0).Events[1].Articulations);

        Assert.True(editor.ToggleArticulation(Articulation.Staccato).Done);
        Assert.Equal(new[] { Articulation.Accent }, Measure(editor, 0).Events[1].Articulations);

        editor.Undo();
        Assert.Equal(new[] { Articulation.Staccato, Articulation.Accent }, Measure(editor, 0).Events[1].Articulations);
        Assert.True(editor.ClearArticulations().Done);
        Assert.Empty(Measure(editor, 0).Events[1].Articulations);
        Assert.False(editor.ClearArticulations().Done);
    }

    [Fact]
    public void Rest_TakesOnlyAFermata()
    {
        var editor = new ScoreEditor(ScoreProject.CreateNew());
        Assert.True(editor.EnterRest().Done);
        editor.Select(At(0, 0));

        Assert.False(editor.ToggleArticulation(Articulation.Accent).Done);
        Assert.True(editor.ToggleArticulation(Articulation.Fermata).Done);
        Assert.Equal(new[] { Articulation.Fermata }, Measure(editor, 0).Events[0].Articulations);
    }

    [Fact]
    public void Dynamic_Text_AndTempo_ReplaceWhatIsAtThePlace_AndCanBeCleared()
    {
        var editor = Editor();
        editor.Select(At(0, 2));

        Assert.True(editor.SetDynamic("p").Done);
        Assert.True(editor.SetDynamic("mf").Done);
        Assert.True(editor.SetExpressionText("dolce").Done);
        Assert.True(editor.SetTempo("quarter", 1, "60").Done);
        Assert.Equal(
            new[] { "Dynamic mf @2", "Words dolce @2 italic", "Tempo  @2 quarter.=60" },
            Measure(editor, 0).Directions.Select(d =>
                $"{d.Kind} {d.Text} @{d.PositionNumerator}" + (d.Italic ? " italic" : "") + (d.Kind == DirectionKind.Tempo ? $" {d.BeatUnit}{new string('.', d.BeatUnitDots)}={d.PerMinute}" : "")));

        Assert.True(editor.SetDynamic("").Done);
        Assert.DoesNotContain(Measure(editor, 0).Directions, d => d.Kind == DirectionKind.Dynamic);
        Assert.False(editor.SetTempo("crotchet", 0, "60").Done);
        Assert.True(editor.ClearExpressions().Done);
        Assert.Empty(Measure(editor, 0).Directions);
    }

    [Fact]
    public void SmartShape_TwoClicksMakeASlurAcrossTheBarline_AndAClickBackwardsStillWorks()
    {
        var editor = Editor();
        Assert.True(editor.ClickShape(At(0, 2)).Done);
        Assert.Equal(At(0, 2), editor.ShapeStart);
        Assert.True(editor.ClickShape(At(1, 1)).Done);
        Assert.Null(editor.ShapeStart);

        var slur = Assert.Single(Measure(editor, 0).Slurs);
        Assert.Equal((2, 1, 1), (slur.PositionNumerator, slur.EndMeasureOffset, slur.EndPositionNumerator));

        // A crescendo clicked from its end to its start goes forwards; clicking the start twice cancels.
        editor.Shape = ShapeKind.Crescendo;
        Assert.True(editor.ClickShape(At(1, 3)).Done);
        Assert.True(editor.ClickShape(At(1, 0)).Done);
        var hairpin = Assert.Single(Measure(editor, 1).Hairpins);
        Assert.Equal((true, 0, 0, 3), (hairpin.Crescendo, hairpin.PositionNumerator, hairpin.EndMeasureOffset, hairpin.EndPositionNumerator));

        Assert.True(editor.ClickShape(At(0, 0)).Done);
        Assert.True(editor.ClickShape(At(0, 0)).Done);
        Assert.Null(editor.ShapeStart);

        editor.Select(At(0, 2));
        Assert.True(editor.ClearShapes().Done);
        Assert.Empty(Measure(editor, 0).Slurs);
        editor.Undo();
        Assert.Single(Measure(editor, 0).Slurs);
    }

    [Fact]
    public void Marks_PutInWithTheTools_SurviveAMusicXmlRoundTrip()
    {
        var editor = Editor();
        editor.Select(At(0, 0));
        editor.ToggleArticulation(Articulation.Tenuto);
        editor.SetDynamic("pp");
        editor.Shape = ShapeKind.Diminuendo;
        editor.ClickShape(At(0, 1));
        editor.ClickShape(At(1, 2));

        var back = MusicXmlImporter.ImportXml(Export.MusicXmlExporter.ToXml(editor.Project), "도구").Project;
        Assert.Equal(new[] { Articulation.Tenuto }, back.Parts[0].Measures[0].Events[0].Articulations);
        Assert.Equal("pp", Assert.Single(back.Parts[0].Measures[0].Directions).Text);
        Assert.False(Assert.Single(back.Parts[0].Measures[0].Hairpins).Crescendo);
    }

    [Theory]
    [InlineData(KeyScope.Articulation, "S", false, EditorCommand.ArticulationStaccato)]
    [InlineData(KeyScope.Articulation, "A", false, EditorCommand.ArticulationAccent)]
    [InlineData(KeyScope.Articulation, "F", false, EditorCommand.ArticulationFermata)]
    [InlineData(KeyScope.Articulation, "Delete", false, EditorCommand.ClearArticulations)]
    [InlineData(KeyScope.Expression, "D4", false, EditorCommand.DynamicMF)]
    [InlineData(KeyScope.Expression, "T", false, EditorCommand.ExpressionText)]
    [InlineData(KeyScope.SmartShape, "OemComma", true, EditorCommand.ShapeCrescendo)]
    [InlineData(KeyScope.SmartShape, "Delete", false, EditorCommand.ClearShapes)]
    [InlineData(KeyScope.SmartShape, "Left", false, EditorCommand.SelectPrevious)] // common keys still work in the tools
    public void MarkTools_HaveTheirKeys(KeyScope scope, string key, bool shift, EditorCommand command) =>
        Assert.Equal(command, KeyMap.Defaults().Find(scope, KeyStroke.Of(key, shift: shift)));

    [Fact]
    public void MarkCommands_RunOnTheEditor()
    {
        var editor = Editor();
        editor.Select(At(0, 3));
        Assert.True(CommandRunner.Run(editor, EditorCommand.ArticulationMarcato)!.Done);
        Assert.True(CommandRunner.Run(editor, EditorCommand.DynamicSfz)!.Done);
        Assert.True(CommandRunner.Run(editor, EditorCommand.ShapeDiminuendo)!.Done);
        Assert.Equal(ShapeKind.Diminuendo, editor.Shape);
        Assert.Null(CommandRunner.Run(editor, EditorCommand.ExpressionText)); // the window asks for the text
        Assert.Equal(new[] { Articulation.Marcato }, Measure(editor, 0).Events[3].Articulations);
        Assert.Equal("sfz", Assert.Single(Measure(editor, 0).Directions).Text);
    }

    [Fact]
    public void SmartShapes_DrawGlissandos_TrillLines_Brackets_AndPedals_AndAreClearedAndUndone()
    {
        var editor = Editor();
        editor.Shape = ShapeKind.Glissando;
        Assert.True(editor.AddShape(At(0, 0), At(0, 1), ShapeKind.Glissando).Done);
        Assert.True(editor.AddShape(At(0, 2), At(1, 0), ShapeKind.TrillLine).Done);
        Assert.True(editor.AddShape(At(1, 1), At(1, 3), ShapeKind.Bracket).Done);
        Assert.True(editor.AddShape(At(0, 0), At(0, 3), ShapeKind.PedalSigns).Done);

        var m0 = Measure(editor, 0);
        var gliss = Assert.Single(m0.Glissandos);
        Assert.Equal((0, 0, 1), (gliss.PositionNumerator, gliss.EndMeasureOffset, gliss.EndPositionNumerator));
        Assert.Contains(Articulation.Trill, m0.Events[2].Articulations); // a trill line comes with its "tr"
        Assert.Equal(new[] { "TrillLine 2→1/0", "Pedal 0→0/3 sign" },
            m0.Lines.OrderBy(l => l.Kind).Select(l => $"{l.Kind} {l.PositionNumerator}→{l.EndMeasureOffset}/{l.EndPositionNumerator}{(l.PedalSign ? " sign" : "")}"));
        var bracket = Assert.Single(Measure(editor, 1).Lines);
        Assert.Equal((LineKind.Bracket, (bool?)true, -1, -1), (bracket.Kind, bracket.Above, bracket.StartHook, bracket.EndHook));

        editor.Select(At(0, 0));
        Assert.True(editor.ClearShapes().Done);
        Assert.Empty(Measure(editor, 0).Glissandos);
        Assert.Equal(LineKind.TrillLine, Assert.Single(Measure(editor, 0).Lines).Kind);
        editor.Undo();
        Assert.Single(Measure(editor, 0).Glissandos);
        Assert.Equal("글리산도", ScoreEditor.ShapeName(ShapeKind.Glissando));
    }

    [Fact]
    public void RehearsalMarks_CountOn_AndSegnoAndCodaToggle()
    {
        var editor = Editor();
        editor.Select(At(0, 0));
        Assert.True(editor.SetRehearsal("").Done);
        editor.Select(At(1, 0));
        Assert.True(editor.SetRehearsal("").Done);
        Assert.Equal(new[] { "A", "B" }, editor.Project.Parts[0].Measures.SelectMany(m => m.Directions).Where(d => d.Kind == DirectionKind.Rehearsal).Select(d => d.Text));
        Assert.True(editor.SetRehearsal("Intro").Done);
        Assert.Equal("Intro", Measure(editor, 1).Directions.Single(d => d.Kind == DirectionKind.Rehearsal).Text);

        Assert.True(editor.ToggleSign(DirectionKind.Segno).Done);
        Assert.Contains(Measure(editor, 1).Directions, d => d.Kind == DirectionKind.Segno);
        Assert.True(editor.ToggleSign(DirectionKind.Segno).Done);
        Assert.DoesNotContain(Measure(editor, 1).Directions, d => d.Kind == DirectionKind.Segno);
        Assert.False(editor.ToggleSign(DirectionKind.Words).Done);
    }

    [Fact]
    public void ChordMarks_Fingerings_Tremolos_BuzzRolls_Arpeggios_AccidentalMarks_AndCueNotes()
    {
        var editor = Editor();
        editor.Select(At(0, 0));
        Assert.True(editor.AddToChordAt(At(0, 0), 4 * 7 + 4).Done); // G4 under the C5
        editor.Select(At(0, 0));

        Assert.True(editor.SetFingering("1 4-5").Done);
        var chord = Measure(editor, 0).Events.Where(e => e.PositionNumerator == 0).OrderBy(e => e.MidiPitch).ToList();
        Assert.Equal(new[] { "1", "4,5" }, chord.Select(e => string.Join(",", e.Fingerings)));
        Assert.False(editor.SetFingering("1 2 3").Done);   // more numbers than notes
        Assert.False(editor.SetFingering("1/2").Done);
        Assert.True(editor.SetFingering("").Done);
        Assert.All(chord, e => Assert.Empty(e.Fingerings));

        Assert.True(editor.SetTremolo(3).Done);
        Assert.Equal(3, chord.Max(e => e.Tremolo));
        Assert.True(editor.ToggleBuzzRoll().Done);
        Assert.Equal((0, true), (chord.Max(e => e.Tremolo), chord.Any(e => e.BuzzRoll)));
        Assert.True(editor.SetArpeggio(ArpeggioKind.Up).Done);
        Assert.All(chord, e => Assert.Equal(ArpeggioKind.Up, e.Arpeggio));
        Assert.True(editor.SetArpeggio(ArpeggioKind.Up).Done);
        Assert.All(chord, e => Assert.Equal(ArpeggioKind.None, e.Arpeggio));
        Assert.True(editor.SetAccidentalMark(1).Done);
        Assert.Equal(1, chord.Select(e => e.AccidentalMark).Single(a => a is not null));
        Assert.True(editor.ToggleCue().Done);
        Assert.All(chord, e => Assert.True(e.Cue));
    }

    [Fact]
    public void CommonTime_AndBarlineFermatas_AreSetOnTheMeasure()
    {
        var editor = Editor();
        editor.Select(At(0, 0));
        Assert.True(editor.ToggleTimeAbbreviation().Done);
        Assert.All(editor.Project.Parts[0].Measures, m => Assert.Equal(TimeSymbol.Common, m.TimeSymbol));
        Assert.True(editor.ToggleTimeAbbreviation().Done);
        Assert.All(editor.Project.Parts[0].Measures, m => Assert.Equal(TimeSymbol.Normal, m.TimeSymbol));

        Assert.True(editor.ToggleBarlineFermata().Done);
        Assert.True(Measure(editor, 0).BarlineFermata);
        editor.Undo();
        Assert.False(Measure(editor, 0).BarlineFermata);
    }

    /// <summary>Eight eighths C D E F G A B C in one measure, beamed by beat.</summary>
    private static ScoreEditor Eighths()
    {
        var editor = new ScoreEditor(ScoreProject.CreateNew());
        editor.SetInputValue(ScoreStudio.Layout.NoteType.Eighth, 0);
        foreach (var letter in "CDEFGABC") Assert.True(editor.EnterNote(letter).Done);
        editor.ClearSelection();
        return editor;
    }

    [Fact]
    public void HiddenNotes_AreToggled_KeepTheirTime_AndAreNotDrawn()
    {
        var editor = Editor();
        editor.Select(At(0, 1));
        Assert.True(editor.ToggleHidden().Done);
        Assert.True(Measure(editor, 0).Events[1].Hidden);
        var heads = ScoreStudio.Layout.ScoreLayoutEngine.Layout(editor.Project).Pages[0].Items.OfType<ScoreStudio.Layout.GlyphItem>()
            .Count(g => g.Glyph == ScoreStudio.Layout.Smufl.NoteheadBlack);
        Assert.Equal(7, heads); // eight quarters, one hidden
        Assert.True(editor.ToggleHidden().Done);
        Assert.False(Measure(editor, 0).Events[1].Hidden);
    }

    [Fact]
    public void GraceNote_TakesTheNoteBeforeTheNextOne_AndLeavesARest()
    {
        var editor = Editor();
        editor.Select(At(0, 3)); // F, before the G of the next measure
        Assert.True(editor.MakeGrace().Done);

        var rest = Measure(editor, 0).Events.Single(e => e.PositionNumerator == 3);
        Assert.Equal(("Rest", 1), (rest.Kind, rest.DurationNumerator));
        var grace = Assert.Single(Measure(editor, 1).Events[0].GraceNotes);
        Assert.Equal((77, "eighth", true), (grace.MidiPitch, grace.Type, grace.Slash)); // F5, entered after C5 D5 E5

        editor.Undo();
        Assert.Equal("Note", Measure(editor, 0).Events.Single(e => e.PositionNumerator == 3).Kind);
        Assert.Empty(Measure(editor, 1).Events[0].GraceNotes);

        editor.Select(At(1, 3)); // the last note has nothing after it
        Assert.False(editor.MakeGrace().Done);
    }

    [Fact]
    public void BreakBeam_SplitsABeamBeforeTheNote_AndJoinsItBackToTheNoteBefore()
    {
        var editor = Eighths();
        string Beams() => string.Join(" ", Measure(editor, 0).Events.Select(e => e.Beams.Count == 0 ? "-" : e.Beams[0].ToString()[0].ToString()));
        Assert.Equal("B E B E B E B E", Beams());

        editor.Select(At(0, 0) with { Position = R(1, 2) }); // the D, second of the first pair
        Assert.True(editor.BreakBeam().Done);
        Assert.Equal("- - B E B E B E", Beams());
        Assert.True(editor.BreakBeam().Done);
        Assert.Equal("B E B E B E B E", Beams());

        editor.Select(At(0, 1)); // the E: join it to the D before
        Assert.True(editor.BreakBeam().Done);
        Assert.Equal("B C C E B E B E", Beams());
        editor.Undo();
        Assert.Equal("B E B E B E B E", Beams());
    }

    [Fact]
    public void OtherKeys_PlusTimeSignatures_SenzaMisura_AndTranspositionChanges_AreSetFromTheCursor()
    {
        var editor = Editor();
        editor.Select(At(0, 0));
        Assert.True(editor.SetOtherKey("F# Bb").Done);
        Assert.All(editor.Project.Parts[0].Measures, m => Assert.Equal("F1,B-1", string.Join(",", m.KeyAccidentals.Select(k => k.Step + k.Alter))));
        Assert.False(editor.SetOtherKey("H#").Done);
        Assert.False(editor.SetOtherKey("F# F#").Done);
        Assert.True(editor.SetKey(2).Done);
        Assert.All(editor.Project.Parts[0].Measures, m => Assert.Empty(m.KeyAccidentals));

        editor.PlaceCursor(0, 0, 1, R(0));
        Assert.True(editor.SetTime("2+2", 4).Done);
        Assert.Equal((4, 4, "2+2"), (Measure(editor, 1).Numerator, Measure(editor, 1).Denominator, Measure(editor, 1).BeatsText));
        Assert.Equal("", Measure(editor, 0).BeatsText);
        Assert.False(editor.SetTime("2-2", 4).Done);
        Assert.True(editor.SetTime(4, 4).Done); // back to plain numbers
        Assert.Equal("", Measure(editor, 1).BeatsText);

        Assert.True(editor.ToggleSenzaMisura().Done);
        Assert.True(Measure(editor, 1).SenzaMisura);
        Assert.True(editor.ToggleSenzaMisura().Done);
        Assert.False(Measure(editor, 1).SenzaMisura);

        Assert.True(editor.SetTransposition(-2, -3).Done);
        Assert.Equal((-2, -3), (Measure(editor, 1).Transpose!.Diatonic, Measure(editor, 1).Transpose!.Chromatic));
        editor.PlaceCursor(0, 0, 0, R(0));
        Assert.True(editor.SetTransposition(-1, -2).Done);
        Assert.Equal(-2, editor.Project.Parts[0].Transpose!.Chromatic);
    }

    [Theory]
    [InlineData("x32010", 6, null, "5:3 4:2 3:0 2:1 1:0")]
    [InlineData("8 10 10 9 8 8", 6, 8, "6:8 5:10 4:10 3:9 2:8 1:8")]
    public void ChordDiagrams_AreReadAsGuitaristsWriteThem(string text, int strings, int? firstFret, string notes)
    {
        var frame = ScoreEditor.ParseFrame(text, out _)!;
        Assert.Equal((strings, firstFret), (frame.Strings, frame.FirstFret));
        Assert.Equal(notes, string.Join(" ", frame.Notes.Select(n => $"{n.String}:{n.Fret}")));
        Assert.Null(ScoreEditor.ParseFrame("xxxxxx", out _));
        Assert.Null(ScoreEditor.ParseFrame("x3z010", out _));
    }

    [Fact]
    public void ChordDiagrams_FiguredBass_AndSymbols_ArePutAtTheChord()
    {
        var editor = Editor();
        editor.Select(At(0, 0));
        Assert.False(editor.SetChordFrame("x32010").Done); // no chord symbol there yet
        Assert.True(editor.EnterChordSymbol("C").Done);
        editor.Select(At(0, 0));
        Assert.True(editor.SetChordFrame("x32010").Done);
        Assert.Equal(5, Measure(editor, 0).ChordSymbols.Single().Frame!.Notes.Count);

        Assert.True(editor.SetFiguredBass("6_ #4 7/").Done);
        var figured = Measure(editor, 0).FiguredBass.Single();
        Assert.Equal(new[] { "|6||_", "sharp|4||", "|7|slash|" }, figured.Figures.Select(f => $"{f.Prefix}|{f.Number}|{f.Suffix}|{(f.Extend ? "_" : "")}"));
        Assert.Equal(1, figured.DurationNumerator); // the extension runs over the quarter note
        Assert.False(editor.SetFiguredBass("6x").Done);
        Assert.True(editor.SetFiguredBass("").Done);
        Assert.Empty(Measure(editor, 0).FiguredBass);

        Assert.True(editor.ToggleSymbol("repeat1Bar").Done);
        Assert.Equal("repeat1Bar", Measure(editor, 0).Directions.Single(d => d.Kind == DirectionKind.Symbol).Text);
        Assert.True(editor.ToggleSymbol("repeat1Bar").Done);
        Assert.DoesNotContain(Measure(editor, 0).Directions, d => d.Kind == DirectionKind.Symbol);
        Assert.False(editor.ToggleSymbol("noSuchGlyph").Done);
    }
}
