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
}
