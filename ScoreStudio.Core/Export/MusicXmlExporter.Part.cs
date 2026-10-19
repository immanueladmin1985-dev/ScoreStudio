using System.Globalization;
using System.Xml.Linq;
using ScoreStudio.Import;
using ScoreStudio.Layout;
using ScoreStudio.Models;

namespace ScoreStudio.Export;

public static partial class MusicXmlExporter
{
    private static readonly string[] TypeNames = { "breve", "whole", "half", "quarter", "eighth", "16th", "32nd", "64th" };

    /// <summary>Dynamics MusicXML has an element for; any other dynamic text is written as &lt;other-dynamics&gt;.</summary>
    private static readonly HashSet<string> DynamicElements = new(StringComparer.Ordinal)
    {
        "p", "pp", "ppp", "pppp", "ppppp", "pppppp", "f", "ff", "fff", "ffff", "fffff", "ffffff",
        "mp", "mf", "sf", "sfp", "sfpp", "fp", "rf", "rfz", "sfz", "sffz", "fz", "n", "pf", "sfzp",
    };

    /// <summary>MusicXML elements for each articulation; a fermata is a notation of its own.</summary>
    private static string ArticulationElement(Articulation articulation) => articulation switch
    {
        Articulation.Staccato => "staccato",
        Articulation.Staccatissimo => "staccatissimo",
        Articulation.Accent => "accent",
        Articulation.Marcato => "strong-accent",
        Articulation.Tenuto => "tenuto",
        _ => "fermata",
    };

    /// <summary>
    /// Numbers for spans (slurs, hairpins) read back by number: the lowest number not taken by a span whose measures
    /// overlap. Spans that only meet in one measure get different numbers too, because the file lists the staves of a
    /// measure one after another and a stop on a lower staff may come after a start on an upper one.
    /// </summary>
    private static List<string> SpanNumbers(IReadOnlyList<(int Start, int End)> spans)
    {
        var numbers = new string[spans.Count];
        var active = new List<(int End, int Number)>();
        foreach (var i in Enumerable.Range(0, spans.Count).OrderBy(i => spans[i].Start))
        {
            active.RemoveAll(a => a.End < spans[i].Start);
            var number = 1;
            while (active.Any(a => a.Number == number)) number++;
            active.Add((spans[i].End, number));
            numbers[i] = number.ToString(CultureInfo.InvariantCulture);
        }

        return numbers.ToList();
    }

    /// <summary>
    /// Writes one part. Each measure holds its staves one after another and, within a staff, one voice after another;
    /// &lt;backup&gt; and &lt;forward&gt; move between them. Chord symbols, clef changes and octave lines of a staff are
    /// written in time order inside the staff's first voice.
    /// </summary>
    private sealed class PartWriter
    {
        private readonly ScorePart _part;
        private readonly string _id;
        private readonly ImportWarningCollector _warnings;
        private readonly int _staves;
        private readonly long _divisions;

        /// <summary>Notes a tie from an earlier note ends on.</summary>
        private readonly HashSet<ScoreEvent> _tieStops = new(ReferenceEqualityComparer.Instance);

        /// <summary>Octave-line starts (shift ≠ 0) and stops (shift 0, with the size that stops) by measure index.</summary>
        private readonly Dictionary<int, List<(int Staff, Rational Position, int Shift, int Size)>> _octaveLines = new();

        /// <summary>Slur starts and stops on the first note of chords.</summary>
        private readonly Dictionary<ScoreEvent, List<XElement>> _slurMarks = new(ReferenceEqualityComparer.Instance);

        /// <summary>Hairpin starts and stops by measure index: staff, time, the &lt;wedge&gt; and the direction's placement.</summary>
        private readonly Dictionary<int, List<(int Staff, Rational Position, XElement Wedge, bool? Above)>> _wedges = new();

        /// <summary>Measure index at which each volta's bracket ends.</summary>
        private readonly Dictionary<int, ScoreVolta> _voltaEnds = new();

        private XElement _measure = null!;
        private Rational _cursor;
        private string _location = "";

        public PartWriter(ScorePart part, string id, ImportWarningCollector warnings)
        {
            _part = part;
            _id = id;
            _warnings = warnings;
            _staves = Math.Max(1, part.Staves.Count);
            _divisions = Divisions(part);
            FindTieStops();
            FindOctaveLines();
            FindSlurs();
            FindHairpins();
            for (var m = 0; m < part.Measures.Count; m++)
            {
                if (part.Measures[m].Volta is { } volta)
                    _voltaEnds[Math.Min(part.Measures.Count - 1, m + Math.Max(1, volta.MeasureCount) - 1)] = volta;
            }
        }

        public XElement Write()
        {
            var part = new XElement("part", new XAttribute("id", _id));
            for (var m = 0; m < _part.Measures.Count; m++)
                part.Add(WriteMeasure(m));
            return part;
        }

        private void Warn(string message) => _warnings.Add(message, _location);

        private static Rational Start(ScoreEvent e) => new(e.PositionNumerator, e.PositionDenominator);
        private static Rational Length(ScoreEvent e) => new(e.DurationNumerator, e.DurationDenominator);
        private static Rational Length(ScoreMeasure m) => new(4L * m.Numerator, m.Denominator);

        /// <summary>The smallest number of divisions per quarter note that writes every time in the part as a whole number.</summary>
        private static long Divisions(ScorePart part)
        {
            long divisions = 1;
            void Add(long denominator) => divisions = checked(divisions / Gcd(divisions, denominator) * denominator);
            foreach (var measure in part.Measures)
            {
                foreach (var e in measure.Events) { Add(Start(e).Denominator); Add(Length(e).Denominator); }
                foreach (var t in measure.Tuplets) { Add(t.PositionDenominator); Add(t.DurationDenominator); }
                foreach (var c in measure.ChordSymbols) Add(new Rational(c.PositionNumerator, c.PositionDenominator).Denominator);
                foreach (var c in measure.ClefChanges) Add(new Rational(c.PositionNumerator, c.PositionDenominator).Denominator);
                foreach (var d in measure.Directions) Add(new Rational(d.PositionNumerator, d.PositionDenominator).Denominator);
                foreach (var h in measure.Hairpins)
                {
                    Add(new Rational(h.PositionNumerator, h.PositionDenominator).Denominator);
                    Add(new Rational(h.EndPositionNumerator, h.EndPositionDenominator).Denominator);
                }
            }

            return divisions;

            static long Gcd(long a, long b)
            {
                while (b != 0) (a, b) = (b, a % b);
                return a;
            }
        }

        private string Duration(Rational value) =>
            checked(value.Numerator * (_divisions / value.Denominator)).ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// A tie ends on the note of the same pitch on the same staff that starts where the tied note ends: in its own voice
        /// if there is one, else in another voice (as the layout draws it). At the end of a measure it is the next measure's.
        /// </summary>
        private void FindTieStops()
        {
            for (var m = 0; m < _part.Measures.Count; m++)
            {
                foreach (var e in _part.Measures[m].Events.Where(e => e.TieToNext && e.Kind != "Rest"))
                {
                    var end = Start(e) + Length(e);
                    var (measure, at) = end >= Length(_part.Measures[m]) && m + 1 < _part.Measures.Count
                        ? (_part.Measures[m + 1], end - Length(_part.Measures[m]))
                        : (_part.Measures[m], end);
                    var candidates = measure.Events
                        .Where(n => n.Kind != "Rest" && n.Staff == e.Staff && n.MidiPitch == e.MidiPitch && Start(n) == at)
                        .ToList();
                    if ((candidates.FirstOrDefault(n => n.Voice == e.Voice) ?? candidates.FirstOrDefault()) is { } target)
                        _tieStops.Add(target);
                }
            }
        }

        /// <summary>
        /// Turns each event's octave shift into lines: a line starts at the first event of a run with the same shift and
        /// stops at the start of the run's last event (the importer still shifts notes that start at a stop).
        /// </summary>
        private void FindOctaveLines()
        {
            for (var staff = 0; staff < _staves; staff++)
            {
                var current = 0;
                (int Measure, Rational Position) last = default;
                for (var m = 0; m < _part.Measures.Count; m++)
                {
                    foreach (var e in _part.Measures[m].Events.Where(e => e.Staff == staff).OrderBy(Start))
                    {
                        if (e.OctaveShift != current)
                        {
                            if (current != 0) AddLine(last.Measure, staff, last.Position, 0, current);
                            if (e.OctaveShift != 0) AddLine(m, staff, Start(e), e.OctaveShift, e.OctaveShift);
                            current = e.OctaveShift;
                        }

                        if (current != 0) last = (m, Start(e));
                    }
                }

                if (current != 0) AddLine(last.Measure, staff, last.Position, 0, current);
            }

            void AddLine(int measure, int staff, Rational position, int shift, int size)
            {
                if (!_octaveLines.TryGetValue(measure, out var lines)) _octaveLines[measure] = lines = new();
                lines.Add((staff, position, shift, Math.Abs(size) >= 2 ? 15 : 8));
            }
        }

        /// <summary>The first note or rest written for a voice's chord at a time, which carries the chord's marks.</summary>
        private static ScoreEvent? ChordAt(ScoreMeasure measure, int staff, int voice, Rational position) =>
            measure.Events.FirstOrDefault(e => e.Staff == staff && e.Voice == voice && Start(e) == position);

        private void FindSlurs()
        {
            var slurs = new List<(int Measure, ScoreSlur Slur, ScoreEvent First, ScoreEvent Last)>();
            for (var m = 0; m < _part.Measures.Count; m++)
            {
                foreach (var slur in _part.Measures[m].Slurs)
                {
                    var end = m + slur.EndMeasureOffset;
                    var first = ChordAt(_part.Measures[m], slur.Staff, slur.Voice, new Rational(slur.PositionNumerator, slur.PositionDenominator));
                    var last = end < _part.Measures.Count
                        ? ChordAt(_part.Measures[end], slur.EndStaff, slur.EndVoice, new Rational(slur.EndPositionNumerator, slur.EndPositionDenominator))
                        : null;
                    if (first is null || last is null || ReferenceEquals(first, last))
                    {
                        _location = $"{_part.Name} {_part.Measures[m].Number}마디";
                        Warn("음표를 찾을 수 없는 이음줄은 내보내지 않음");
                        continue;
                    }

                    slurs.Add((m, slur, first, last));
                }
            }

            var numbers = SpanNumbers(slurs.Select(s => (s.Measure, s.Measure + s.Slur.EndMeasureOffset)).ToList());
            for (var i = 0; i < slurs.Count; i++)
            {
                var start = new XElement("slur", new XAttribute("type", "start"), new XAttribute("number", numbers[i]));
                if (slurs[i].Slur.Above is { } above) start.Add(new XAttribute("placement", above ? "above" : "below"));
                Mark(_slurMarks, slurs[i].First, start);
                Mark(_slurMarks, slurs[i].Last, new XElement("slur", new XAttribute("type", "stop"), new XAttribute("number", numbers[i])));
            }
        }

        private void FindHairpins()
        {
            var hairpins = new List<(int Measure, ScoreHairpin Hairpin, int End)>();
            for (var m = 0; m < _part.Measures.Count; m++)
            {
                foreach (var hairpin in _part.Measures[m].Hairpins)
                    hairpins.Add((m, hairpin, Math.Min(_part.Measures.Count - 1, m + hairpin.EndMeasureOffset)));
            }

            var numbers = SpanNumbers(hairpins.Select(h => (h.Measure, h.End)).ToList());
            for (var i = 0; i < hairpins.Count; i++)
            {
                var (m, hairpin, end) = hairpins[i];
                var start = new XElement("wedge", new XAttribute("type", hairpin.Crescendo ? "crescendo" : "diminuendo"), new XAttribute("number", numbers[i]));
                if (hairpin.Y is { } y) start.Add(new XAttribute("default-y", Number(y * 10)));
                AddWedge(m, hairpin.Staff, new Rational(hairpin.PositionNumerator, hairpin.PositionDenominator), start, hairpin.Above);
                AddWedge(end, hairpin.Staff, new Rational(hairpin.EndPositionNumerator, hairpin.EndPositionDenominator),
                    new XElement("wedge", new XAttribute("type", "stop"), new XAttribute("number", numbers[i])), hairpin.Above);
            }

            void AddWedge(int measure, int staff, Rational position, XElement wedge, bool? above)
            {
                if (!_wedges.TryGetValue(measure, out var list)) _wedges[measure] = list = new();
                list.Add((staff, position, wedge, above));
            }
        }

        private static void Mark(Dictionary<ScoreEvent, List<XElement>> marks, ScoreEvent note, XElement mark)
        {
            if (!marks.TryGetValue(note, out var list)) marks[note] = list = new();
            list.Add(mark);
        }

        private XElement WriteMeasure(int index)
        {
            var measure = _part.Measures[index];
            var previous = index > 0 ? _part.Measures[index - 1] : null;
            _location = $"{_part.Name} {measure.Number}마디";
            _measure = new XElement("measure", new XAttribute("number", measure.Number.ToString(CultureInfo.InvariantCulture)));
            if (measure.SourceWidth is { } width) _measure.Add(new XAttribute("width", Number(width * 10)));
            _cursor = Rational.Zero;

            WritePrint(measure, index);
            WriteLeftBarline(measure);
            WriteAttributes(measure, previous);

            var groups = measure.Events
                .Where(e => e.Staff >= 0 && e.Staff < _staves)
                .GroupBy(e => (e.Staff, e.Voice))
                .ToDictionary(g => g.Key, g => Chords(g));
            if (measure.Events.Any(e => e.Staff < 0 || e.Staff >= _staves))
                Warn("없는 보표의 음표·쉼표는 내보내지 않음");

            var context = new MeasureContext(measure, Accidentals(measure), TupletMarks(measure, groups));
            for (var staff = 0; staff < _staves; staff++)
            {
                var items = StaffItems(measure, index, staff);
                var voices = groups.Keys.Where(k => k.Staff == staff).Select(k => k.Voice).OrderBy(v => v).ToList();
                if (voices.Count == 0)
                    WriteSequence(context, staff, null, new List<Chord>(), items);
                for (var i = 0; i < voices.Count; i++)
                    WriteSequence(context, staff, voices[i], groups[(staff, voices[i])], i == 0 ? items : new List<Item>());
            }

            WriteRightBarline(measure, index);
            return _measure;
        }

        private void WritePrint(ScoreMeasure measure, int index)
        {
            var print = new XElement("print");
            if (index > 0 && measure.PageBreak) print.Add(new XAttribute("new-page", "yes"));
            else if (index > 0 && measure.SystemBreak) print.Add(new XAttribute("new-system", "yes"));

            if (measure.SystemDistance is not null || measure.TopSystemDistance is not null)
            {
                var system = new XElement("system-layout");
                if (measure.SystemDistance is { } distance) system.Add(new XElement("system-distance", Number(distance * 10)));
                if (measure.TopSystemDistance is { } top) system.Add(new XElement("top-system-distance", Number(top * 10)));
                print.Add(system);
            }

            foreach (var staff in measure.StaffDistances.OrderBy(d => d.Staff))
                print.Add(new XElement("staff-layout", new XAttribute("number", staff.Staff + 1), new XElement("staff-distance", Number(staff.Distance * 10))));

            if (print.HasAttributes || print.HasElements) _measure.Add(print);
        }

        private void WriteLeftBarline(ScoreMeasure measure)
        {
            if (!measure.RepeatStart && measure.Volta is null) return;
            var barline = new XElement("barline", new XAttribute("location", "left"));
            if (measure.RepeatStart) barline.Add(new XElement("bar-style", "heavy-light"));
            if (measure.Volta is { } volta)
                barline.Add(new XElement("ending", new XAttribute("number", volta.Numbers), new XAttribute("type", "start"), volta.Text));
            if (measure.RepeatStart) barline.Add(new XElement("repeat", new XAttribute("direction", "forward")));
            _measure.Add(barline);
        }

        private void WriteRightBarline(ScoreMeasure measure, int index)
        {
            var isLast = index == _part.Measures.Count - 1;
            var volta = _voltaEnds.GetValueOrDefault(index);
            if (!measure.RepeatEnd && volta is null && !isLast) return;

            var barline = new XElement("barline", new XAttribute("location", "right"),
                new XElement("bar-style", measure.RepeatEnd || isLast ? "light-heavy" : "regular"));
            if (volta is not null)
                barline.Add(new XElement("ending", new XAttribute("number", volta.Numbers), new XAttribute("type", volta.OpenEnd ? "discontinue" : "stop")));
            if (measure.RepeatEnd)
            {
                var repeat = new XElement("repeat", new XAttribute("direction", "backward"));
                if (measure.RepeatTimes != 2) repeat.Add(new XAttribute("times", measure.RepeatTimes));
                barline.Add(repeat);
            }

            _measure.Add(barline);
        }

        private void WriteAttributes(ScoreMeasure measure, ScoreMeasure? previous)
        {
            var attributes = new XElement("attributes");
            if (previous is null) attributes.Add(new XElement("divisions", _divisions.ToString(CultureInfo.InvariantCulture)));
            if (previous is null || previous.KeyFifths != measure.KeyFifths)
                attributes.Add(new XElement("key", new XElement("fifths", measure.KeyFifths)));
            if (previous is null || previous.Numerator != measure.Numerator || previous.Denominator != measure.Denominator)
                attributes.Add(new XElement("time", new XElement("beats", measure.Numerator), new XElement("beat-type", measure.Denominator)));
            if (previous is null && _staves > 1) attributes.Add(new XElement("staves", _staves));
            if (previous is null)
            {
                for (var staff = 0; staff < _staves; staff++)
                    attributes.Add(Clef(staff, _part.Staves.ElementAtOrDefault(staff)?.Clef ?? ClefType.Treble));
            }

            var wasHidden = previous?.HiddenStaves ?? new List<int>();
            for (var staff = 0; staff < _staves; staff++)
            {
                if (measure.HiddenStaves.Contains(staff) == wasHidden.Contains(staff)) continue;
                attributes.Add(new XElement("staff-details", new XAttribute("number", staff + 1),
                    new XAttribute("print-object", measure.HiddenStaves.Contains(staff) ? "no" : "yes")));
            }

            if (attributes.HasElements) _measure.Add(attributes);
        }

        private XElement Clef(int staff, ClefType clef)
        {
            var (sign, line, octave) = clef switch
            {
                ClefType.Bass => ("F", 4, 0),
                ClefType.Alto => ("C", 3, 0),
                ClefType.Tenor => ("C", 4, 0),
                ClefType.Treble8vb => ("G", 2, -1),
                _ => ("G", 2, 0),
            };
            var element = new XElement("clef");
            if (_staves > 1) element.Add(new XAttribute("number", staff + 1));
            element.Add(new XElement("sign", sign), new XElement("line", line));
            if (octave != 0) element.Add(new XElement("clef-octave-change", octave));
            return element;
        }

        /// <summary>Notes of one voice in time order; notes starting together form one chord, rests stand alone.</summary>
        private static List<Chord> Chords(IEnumerable<ScoreEvent> events)
        {
            var chords = new List<Chord>();
            foreach (var e in events.OrderBy(Start))
            {
                if (e.Kind != "Rest" && chords.Count > 0 && chords[^1].Notes[0].Kind != "Rest" && chords[^1].Position == Start(e))
                    chords[^1].Notes.Add(e);
                else
                    chords.Add(new Chord(Start(e), new List<ScoreEvent> { e }));
            }

            return chords;
        }

        /// <summary>
        /// The accidental to print before each note: when its alteration differs from the key signature or from an earlier
        /// note on the same line and octave of the staff in this measure. A note a tie carries over the barline prints none.
        /// </summary>
        private Dictionary<ScoreEvent, string> Accidentals(ScoreMeasure measure)
        {
            var result = new Dictionary<ScoreEvent, string>(ReferenceEqualityComparer.Instance);
            foreach (var staff in measure.Events.Where(e => e.Kind != "Rest").GroupBy(e => e.Staff))
            {
                var alters = new Dictionary<(int Step, int Octave), int>();
                foreach (var note in staff.OrderBy(Start))
                {
                    var pitch = Notation.Spell(note, measure.KeyFifths);
                    var line = (pitch.Step, pitch.Octave);
                    var current = alters.TryGetValue(line, out var alter) ? alter : Notation.KeyAlter(measure.KeyFifths, pitch.Step);
                    if (_tieStops.Contains(note) && Start(note).Sign == 0) continue;
                    if (pitch.Alter == current) continue;

                    alters[line] = pitch.Alter;
                    result[note] = pitch.Alter switch
                    {
                        -3 => "triple-flat",
                        -2 => "flat-flat",
                        -1 => "flat",
                        0 => "natural",
                        1 => "sharp",
                        2 => "double-sharp",
                        _ => "triple-sharp",
                    };
                }
            }

            return result;
        }

        /// <summary>
        /// The &lt;tuplet&gt; marks on the first note of each chord: a start where a tuplet's span begins and a stop on its
        /// last chord. A tuplet inside another gets the next number, so the two are read back as separate spans.
        /// </summary>
        private Dictionary<ScoreEvent, List<XElement>> TupletMarks(ScoreMeasure measure, Dictionary<(int Staff, int Voice), List<Chord>> groups)
        {
            var marks = new Dictionary<ScoreEvent, List<XElement>>(ReferenceEqualityComparer.Instance);
            Rational TupletStart(ScoreTuplet t) => new(t.PositionNumerator, t.PositionDenominator);
            Rational TupletEnd(ScoreTuplet t) => TupletStart(t) + new Rational(t.DurationNumerator, t.DurationDenominator);

            for (var i = 0; i < measure.Tuplets.Count; i++)
            {
                var tuplet = measure.Tuplets[i];
                var (start, end) = (TupletStart(tuplet), TupletEnd(tuplet));
                var chords = groups.GetValueOrDefault((tuplet.Staff, tuplet.Voice)) ?? new List<Chord>();
                var first = chords.FirstOrDefault(c => c.Position == start);
                var last = chords.LastOrDefault(c => c.Position >= start && c.Position < end);
                if (first is null || last is null)
                {
                    Warn("음표가 없는 잇단음표의 숫자·괄호는 내보내지 않음");
                    continue;
                }

                var outer = measure.Tuplets.Where((o, j) => j != i && o.Staff == tuplet.Staff && o.Voice == tuplet.Voice
                    && TupletStart(o) <= start && TupletEnd(o) >= end
                    && (TupletStart(o) < start || TupletEnd(o) > end || j < i)).Count();
                var number = (outer + 1).ToString(CultureInfo.InvariantCulture);

                var startMark = new XElement("tuplet", new XAttribute("type", "start"), new XAttribute("number", number));
                if (tuplet.Bracket is { } bracket) startMark.Add(new XAttribute("bracket", bracket ? "yes" : "no"));
                startMark.Add(new XAttribute("show-number", tuplet.ShowNumber switch { TupletNumber.Both => "both", TupletNumber.None => "none", _ => "actual" }));
                if (tuplet.Above is { } above) startMark.Add(new XAttribute("placement", above ? "above" : "below"));
                startMark.Add(
                    new XElement("tuplet-actual", new XElement("tuplet-number", tuplet.Actual)),
                    new XElement("tuplet-normal", new XElement("tuplet-number", tuplet.Normal)));

                Mark(marks, first.Notes[0], startMark);
                Mark(marks, last.Notes[0], new XElement("tuplet", new XAttribute("type", "stop"), new XAttribute("number", number)));
            }

            return marks;
        }

        /// <summary>Chord symbols, clef changes and octave lines of a staff in this measure, by time.</summary>
        private List<Item> StaffItems(ScoreMeasure measure, int index, int staff)
        {
            var items = new List<Item>();
            foreach (var change in measure.ClefChanges.Where(c => c.Staff == staff))
            {
                var element = new XElement("attributes", Clef(staff, change.Clef));
                items.Add(new Item(new Rational(change.PositionNumerator, change.PositionDenominator), element, UsesOffset: false));
            }

            foreach (var line in _octaveLines.GetValueOrDefault(index) ?? new())
            {
                if (line.Staff != staff) continue;
                var shift = new XElement("octave-shift",
                    new XAttribute("type", line.Shift switch { > 0 => "down", < 0 => "up", _ => "stop" }),
                    new XAttribute("size", line.Size));
                var direction = new XElement("direction", new XAttribute("placement", line.Shift < 0 ? "below" : "above"),
                    new XElement("direction-type", shift));
                if (_staves > 1) direction.Add(new XElement("staff", staff + 1));
                items.Add(new Item(line.Position, direction, UsesOffset: false));
            }

            foreach (var chord in measure.ChordSymbols.Where(c => c.Staff == staff))
                items.Add(new Item(new Rational(chord.PositionNumerator, chord.PositionDenominator), Harmony(chord, staff), UsesOffset: true));

            foreach (var direction in measure.Directions.Where(d => d.Staff == staff))
                items.Add(new Item(new Rational(direction.PositionNumerator, direction.PositionDenominator), Direction(direction), UsesOffset: true));

            foreach (var (_, position, wedge, above) in (_wedges.GetValueOrDefault(index) ?? new()).Where(w => w.Staff == staff))
                items.Add(new Item(position, DirectionOf(new XElement(wedge), above, staff), UsesOffset: true));

            return items.OrderBy(i => i.Position).ToList();
        }

        private XElement DirectionOf(XElement type, bool? above, int staff)
        {
            var direction = new XElement("direction");
            if (above is { } a) direction.Add(new XAttribute("placement", a ? "above" : "below"));
            direction.Add(new XElement("direction-type", type));
            if (_staves > 1) direction.Add(new XElement("staff", staff + 1));
            return direction;
        }

        /// <summary>A dynamic, words or metronome mark as a &lt;direction&gt;.</summary>
        private XElement Direction(ScoreDirection direction)
        {
            XElement type;
            switch (direction.Kind)
            {
                case DirectionKind.Dynamic:
                    type = new XElement("dynamics", DynamicElements.Contains(direction.Text)
                        ? new XElement(direction.Text)
                        : new XElement("other-dynamics", direction.Text));
                    break;
                case DirectionKind.Tempo:
                    type = new XElement("metronome", new XElement("beat-unit", direction.BeatUnit));
                    for (var d = 0; d < direction.BeatUnitDots; d++) type.Add(new XElement("beat-unit-dot"));
                    type.Add(new XElement("per-minute", direction.PerMinute));
                    break;
                default:
                    type = new XElement("words", direction.Text);
                    if (direction.Italic) type.Add(new XAttribute("font-style", "italic"));
                    if (direction.Bold) type.Add(new XAttribute("font-weight", "bold"));
                    break;
            }

            if (direction.Y is { } y) type.Add(new XAttribute("default-y", Number(y * 10)));
            return DirectionOf(type, direction.Above, direction.Staff);
        }

        private XElement Harmony(ScoreChordSymbol chord, int staff)
        {
            static string Accidental(int alter) => alter > 0 ? new string('#', alter) : new string('b', -alter);

            // The kind's text is what the symbol prints between its root and bass, so the importer rebuilds the same text.
            var suffix = chord.Text;
            var root = chord.RootStep + Accidental(chord.RootAlter);
            if (suffix.StartsWith(root, StringComparison.Ordinal)) suffix = suffix[root.Length..];
            if (chord.BassStep is not null && suffix.EndsWith("/" + chord.BassStep + Accidental(chord.BassAlter), StringComparison.Ordinal))
                suffix = suffix[..suffix.LastIndexOf('/')];

            var rootElement = new XElement("root", new XElement("root-step", chord.RootStep));
            if (chord.RootAlter != 0) rootElement.Add(new XElement("root-alter", chord.RootAlter));
            var harmony = new XElement("harmony", rootElement, new XElement("kind", new XAttribute("text", suffix), chord.Kind));
            if (chord.Y is { } y) harmony.Add(new XAttribute("default-y", Number(y * 10)));
            if (chord.BassStep is { } bass)
            {
                var bassElement = new XElement("bass", new XElement("bass-step", bass));
                if (chord.BassAlter != 0) bassElement.Add(new XElement("bass-alter", chord.BassAlter));
                harmony.Add(bassElement);
            }

            if (_staves > 1) harmony.Add(new XElement("staff", staff + 1));
            return harmony;
        }

        /// <summary>
        /// One voice of a staff (or, with no voice, only the staff's items). Items are written before chords that start
        /// at the same time; chord symbols and directions later than the current time get an &lt;offset&gt; instead of
        /// moving the cursor.
        /// </summary>
        private void WriteSequence(MeasureContext context, int staff, int? voice, List<Chord> chords, List<Item> items)
        {
            var entries = items.Select((item, i) => (item.Position, Order: 0, Index: i, Item: (Item?)item, Chord: (Chord?)null))
                .Concat(chords.Select((chord, i) => (chord.Position, Order: 1, Index: i, Item: (Item?)null, Chord: (Chord?)chord)))
                .OrderBy(e => e.Position).ThenBy(e => e.Order).ThenBy(e => e.Index);

            foreach (var entry in entries)
            {
                if (entry.Item is { } item)
                {
                    // An offset only looks ahead; to go back (e.g. to the start of the next staff), the cursor moves.
                    if (item.UsesOffset && item.Position >= _cursor)
                    {
                        if (item.Position != _cursor)
                        {
                            var offset = new XElement("offset", Duration(item.Position - _cursor));
                            var staffElement = item.Element.Element("staff");
                            if (staffElement is null) item.Element.Add(offset);
                            else staffElement.AddBeforeSelf(offset);
                        }

                        _measure.Add(item.Element);
                    }
                    else
                    {
                        MoveTo(item.Position, null, null);
                        _measure.Add(item.Element);
                    }

                    continue;
                }

                var chord = entry.Chord!;
                MoveTo(chord.Position, voice, staff);
                var onlyRest = chords.Count == 1 && chord.Notes[0].Kind == "Rest" && chord.Position.Sign == 0
                    && Length(chord.Notes[0]) == Length(context.Measure);
                for (var i = 0; i < chord.Notes.Count; i++)
                    _measure.Add(Note(context, chord.Notes[i], isChordMember: i > 0, isMeasureRest: onlyRest));
                _cursor += Length(chord.Notes[0]);
            }
        }

        private void MoveTo(Rational position, int? voice, int? staff)
        {
            if (position > _cursor)
            {
                var forward = new XElement("forward", new XElement("duration", Duration(position - _cursor)));
                if (voice is not null) forward.Add(new XElement("voice", voice.Value));
                if (staff is not null && _staves > 1) forward.Add(new XElement("staff", staff.Value + 1));
                _measure.Add(forward);
            }
            else if (position < _cursor)
            {
                _measure.Add(new XElement("backup", new XElement("duration", Duration(_cursor - position))));
            }

            _cursor = position;
        }

        /// <summary>A &lt;note&gt; with its children in the order the MusicXML schema requires.</summary>
        private XElement Note(MeasureContext context, ScoreEvent e, bool isChordMember, bool isMeasureRest)
        {
            var note = new XElement("note");
            if (isChordMember) note.Add(new XElement("chord"));

            var isRest = e.Kind == "Rest";
            if (isRest)
            {
                var rest = new XElement("rest");
                if (isMeasureRest) rest.Add(new XAttribute("measure", "yes"));
                note.Add(rest);
            }
            else
            {
                var pitch = Notation.Spell(e, context.Measure.KeyFifths);
                var pitchElement = new XElement("pitch", new XElement("step", "CDEFGAB"[pitch.Step].ToString()));
                if (pitch.Alter != 0) pitchElement.Add(new XElement("alter", pitch.Alter));
                pitchElement.Add(new XElement("octave", pitch.Octave));
                note.Add(pitchElement);
            }

            var duration = Length(e);
            note.Add(new XElement("duration", Duration(duration)));
            var tieStop = !isRest && _tieStops.Contains(e);
            var tieStart = !isRest && e.TieToNext;
            if (tieStop) note.Add(new XElement("tie", new XAttribute("type", "stop")));
            if (tieStart) note.Add(new XElement("tie", new XAttribute("type", "start")));
            note.Add(new XElement("voice", e.Voice));

            XElement? timeModification = null;
            if (!isMeasureRest)
            {
                var value = Notation.ValueOf(duration, NotePreparation.TupletRatio(context.Measure, e));
                if (!value.IsExact) Warn("음표 종류로 정확히 나타낼 수 없는 길이 (길이는 그대로 쓰고 가장 가까운 짧은 음표로 표시)");
                note.Add(new XElement("type", TypeNames[(int)value.Type]));
                for (var d = 0; d < value.Dots; d++) note.Add(new XElement("dot"));

                // A written value longer than the real length is shortened by a tuplet: written : real = actual : normal.
                var written = Notation.Length(value.Type) * new Rational((1L << (value.Dots + 1)) - 1, 1L << value.Dots);
                if (value.IsExact && written != duration)
                {
                    var ratio = written / duration;
                    timeModification = new XElement("time-modification",
                        new XElement("actual-notes", ratio.Numerator), new XElement("normal-notes", ratio.Denominator));
                }
            }

            if (context.Accidentals.TryGetValue(e, out var accidental)) note.Add(new XElement("accidental", accidental));
            if (timeModification is not null) note.Add(timeModification);
            if (e.StemUp is { } up) note.Add(new XElement("stem", up ? "up" : "down"));
            if (_staves > 1) note.Add(new XElement("staff", e.Staff + 1));

            if (!isChordMember)
            {
                for (var level = 0; level < e.Beams.Count; level++)
                {
                    note.Add(new XElement("beam", new XAttribute("number", level + 1), e.Beams[level] switch
                    {
                        BeamType.Begin => "begin",
                        BeamType.Continue => "continue",
                        BeamType.End => "end",
                        BeamType.ForwardHook => "forward hook",
                        _ => "backward hook",
                    }));
                }
            }

            var notations = new XElement("notations");
            if (tieStop) notations.Add(new XElement("tied", new XAttribute("type", "stop")));
            if (tieStart) notations.Add(new XElement("tied", new XAttribute("type", "start")));
            if (!isChordMember && _slurMarks.TryGetValue(e, out var slurs)) notations.Add(slurs);
            if (!isChordMember && context.TupletMarks.TryGetValue(e, out var marks)) notations.Add(marks);

            // Tenuto with staccato is MusicXML's detached-legato (Finale's "tenuto-staccato").
            var detached = e.Articulations.Contains(Articulation.Tenuto) && e.Articulations.Contains(Articulation.Staccato);
            var articulations = e.Articulations.Distinct()
                .Where(a => a != Articulation.Fermata && !(detached && a is Articulation.Tenuto or Articulation.Staccato))
                .Select(a => new XElement(ArticulationElement(a)))
                .ToList();
            if (detached) articulations.Insert(0, new XElement("detached-legato"));
            if (articulations.Count > 0) notations.Add(new XElement("articulations", articulations));

            if (e.Articulations.Contains(Articulation.Fermata)) notations.Add(new XElement("fermata", new XAttribute("type", "upright")));
            if (notations.HasElements) note.Add(notations);

            foreach (var lyric in e.Lyrics.OrderBy(l => l.Verse))
                note.Add(Lyric(lyric));
            foreach (var verse in e.ExtendEnds)
                note.Add(new XElement("lyric", new XAttribute("number", verse), new XElement("extend", new XAttribute("type", "stop"))));

            return note;
        }

        /// <summary>A syllable; syllables joined by ‿ (an elision) are separate &lt;text&gt; elements.</summary>
        private static XElement Lyric(ScoreLyric lyric)
        {
            var element = new XElement("lyric", new XAttribute("number", lyric.Verse));
            if (lyric.Y is { } y) element.Add(new XAttribute("default-y", Number(y * 10)));
            element.Add(new XElement("syllabic", lyric.Syllabic switch
            {
                LyricSyllabic.Begin => "begin",
                LyricSyllabic.Middle => "middle",
                LyricSyllabic.End => "end",
                _ => "single",
            }));

            var parts = lyric.Text.Split('‿');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0) element.Add(new XElement("elision", "‿"));
                element.Add(new XElement("text", parts[i]));
            }

            if (lyric.Extend) element.Add(new XElement("extend"));
            return element;
        }

        private sealed record Chord(Rational Position, List<ScoreEvent> Notes);

        /// <summary>
        /// Something written between the notes of a staff. With <paramref name="UsesOffset"/> it is placed with an
        /// &lt;offset&gt; from the current time; otherwise the cursor moves to its time.
        /// </summary>
        private sealed record Item(Rational Position, XElement Element, bool UsesOffset);

        private sealed record MeasureContext(
            ScoreMeasure Measure,
            Dictionary<ScoreEvent, string> Accidentals,
            Dictionary<ScoreEvent, List<XElement>> TupletMarks);
    }
}
