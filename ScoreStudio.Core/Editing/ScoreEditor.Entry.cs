using ScoreStudio.Import;
using ScoreStudio.Layout;
using ScoreStudio.Models;

namespace ScoreStudio.Editing;

/// <summary>
/// Finale's Speedy Entry commands that change a note's kind rather than its pitch (docs/MARK_TOOLS.md): hide it, make it
/// a grace note of the next note, and break or join the beam before it.
/// </summary>
public sealed partial class ScoreEditor
{
    /// <summary>Hides the selected chord or rest (it keeps its time and is played), or shows it again.</summary>
    public EditResult ToggleHidden()
    {
        var at = MarkPlace;
        if (ChordAt(at) is not { } chord) return EditResult.Refused("숨길 음표나 쉼표를 먼저 누르세요.");
        var hidden = chord.All(e => e.Hidden);
        return Change(() =>
        {
            foreach (var e in ChordAt(at)!) e.Hidden = !hidden;
            return null;
        }, () => hidden ? "음표를 다시 보이게 했습니다." : "음표를 숨겼습니다(박자는 그대로, 인쇄되지 않음).");
    }

    /// <summary>
    /// Makes the selected chord a grace chord (an eighth with a slash) before the next chord of its voice, as Finale's
    /// Speedy Entry ";" does; the time it took becomes a rest.
    /// </summary>
    public EditResult MakeGrace()
    {
        var at = MarkPlace;
        if (ChordAt(at) is not { } chord || chord[0].Kind != "Note") return EditResult.Refused("꾸밈음으로 바꿀 음표를 먼저 누르세요.");
        if (NextChord(at) is null) return EditResult.Refused("꾸밈음이 붙을 다음 음표가 이 성부에 없습니다.");

        return Change(() =>
        {
            var notes = ChordAt(at)!.OrderBy(e => e.MidiPitch).ToList();
            var (next, nextMeasure) = NextChord(at)!.Value;
            var carrier = next.FirstOrDefault(e => e.GraceNotes.Count > 0) ?? next[0];
            var graces = notes.Select((e, i) => new ScoreGraceNote
            {
                MidiPitch = e.MidiPitch, NotationSpelling = e.NotationSpelling, Type = "eighth", Slash = true, Chord = i > 0,
            });
            // Before the next chord: after the graces already before it, ahead of any that come after the note.
            var index = carrier.GraceNotes.FindIndex(g => g.After);
            carrier.GraceNotes.InsertRange(index < 0 ? carrier.GraceNotes.Count : index, graces);

            var measure = MeasureAt(at);
            foreach (var e in notes) measure.Events.Remove(e);
            measure.Events.Add(new ScoreEvent
            {
                Kind = "Rest", Staff = at.Staff, Voice = at.Voice, PositionNumerator = notes[0].PositionNumerator, PositionDenominator = notes[0].PositionDenominator,
                DurationNumerator = notes[0].DurationNumerator, DurationDenominator = notes[0].DurationDenominator,
            });
            measure.Events = measure.Events.OrderBy(e => e.Staff).ThenBy(e => e.Voice).ThenBy(Start).ToList();
            AutoBeam(measure, at.Staff, at.Voice);
            if (!ReferenceEquals(nextMeasure, measure)) AutoBeam(nextMeasure, at.Staff, at.Voice);
            return null;
        }, () => "다음 음표의 꾸밈음으로 바꿨습니다. 원래 자리는 쉼표가 되었습니다.");
    }

    /// <summary>The next chord of the voice after a place: later in its measure, or the first one of the next measure.</summary>
    private (List<ScoreEvent> Chord, ScoreMeasure Measure)? NextChord(EditPosition at)
    {
        var measures = Project.Parts[at.Part].Measures;
        var later = VoiceChords(measures[at.Measure], at.Staff, at.Voice).FirstOrDefault(c => Start(c[0]) > at.Position);
        if (later is not null) return (later, measures[at.Measure]);
        if (at.Measure + 1 >= measures.Count) return null;
        var first = VoiceChords(measures[at.Measure + 1], at.Staff, at.Voice).FirstOrDefault();
        return first is null ? null : (first, measures[at.Measure + 1]);
    }

    /// <summary>
    /// Finale's Speedy Entry "/": breaks the beam before the selected chord, or, when it starts a beam (or has none),
    /// joins it to the chord before. Only eighths and shorter next to each other can share a beam.
    /// </summary>
    public EditResult BreakBeam()
    {
        var at = MarkPlace;
        if (ChordAt(at) is not { } chord || chord[0].Kind != "Note") return EditResult.Refused("빔을 끊거나 이을 음표를 먼저 누르세요.");
        var measure = MeasureAt(at);
        var chords = VoiceChords(measure, at.Staff, at.Voice);
        var index = chords.FindIndex(c => Start(c[0]) == at.Position);
        int Levels(List<ScoreEvent> c) =>
            c[0].Kind != "Note" ? 0 : Notation.ValueOf(Duration(c[0]), TupletRatioAt(measure, at.Staff, at.Voice, Start(c[0]))) is { IsExact: true } v && v.Type >= NoteType.Eighth
                ? v.Type - NoteType.Eighth + 1 : 0;
        if (Levels(chords[index]) == 0) return EditResult.Refused("8분음표보다 짧은 음표만 빔으로 이을 수 있습니다.");

        // The voice's beam groups as they are now: a group runs from a Begin to its End; an unbeamed chord is alone.
        var groups = new List<List<int>>();
        for (var i = 0; i < chords.Count; i++)
        {
            var first = chords[i][0].Beams.Count > 0 ? chords[i][0].Beams[0] : (BeamType?)null;
            if (first is BeamType.Continue or BeamType.End && groups.Count > 0) groups[^1].Add(i);
            else groups.Add(new List<int> { i });
        }

        var mine = groups.FindIndex(g => g.Contains(index));
        var joined = groups[mine][0] != index;
        if (!joined && (index == 0 || Levels(chords[index - 1]) == 0 || Start(chords[index - 1][0]) + Duration(chords[index - 1][0]) != at.Position))
            return EditResult.Refused("앞 음표와 이을 수 없습니다(앞이 8분음표보다 짧은 음표로 바로 붙어 있어야 합니다).");

        if (joined)
        {
            // Split the group before the chord.
            var tail = groups[mine].Where(i => i >= index).ToList();
            groups[mine].RemoveAll(i => i >= index);
            groups.Insert(mine + 1, tail);
        }
        else
        {
            // Join the chord's group to the one before it.
            groups[mine - 1].AddRange(groups[mine]);
            groups.RemoveAt(mine);
        }

        return Change(() =>
        {
            var voiceChords = VoiceChords(MeasureAt(at), at.Staff, at.Voice);
            foreach (var e in voiceChords.SelectMany(c => c)) e.Beams = new List<BeamType>();
            foreach (var g in groups) SetBeams(g.Select(i => (voiceChords[i], Levels(voiceChords[i]))).ToList());
            return null;
        }, () => joined ? "앞 음표와의 빔을 끊었습니다." : "앞 음표와 빔으로 이었습니다.");
    }
}
