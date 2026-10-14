using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OurTaiko
{
    // TJA time is measured relative to the audio start. Positive OFFSET starts the chart earlier.
    // Commands are kept between note slots, so a BPM/SCROLL change inside a measure stays in place.
    public static class TjaParser
    {
        static double Number(string value) => TjaNumber.Parse(value);
        // A numeric COURSE (0–4) by its name; any other value as written.
        static string Course(string value)
            => int.TryParse(value, out int n) && n >= 0 && n < SongInfo.CourseNames.Count ? SongInfo.CourseNames[n] : value;

        // One parse for every mode: how branches are played is PlaySession's choice.
        public static TaikoChart Parse(string text, string requestedCourse = "Oni")
        {
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("The TJA chart is empty.");
            var chart = new TaikoChart();
            var tokens = new List<string>();
            var balloons = new List<int>();
            string course = "Oni";
            int level = 0;
            bool reading = false, found = false, ended = false;
            foreach (string raw in text.TrimStart('\uFEFF').Replace("\r", "").Split('\n'))
            {
                string line = raw.Split(new[] { "//" }, StringSplitOptions.None)[0].Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#START", StringComparison.OrdinalIgnoreCase))
                {
                    reading = string.Equals(Course(course), Course(requestedCourse), StringComparison.OrdinalIgnoreCase);
                    if (reading) { found = true; chart.Course = Course(course).Split('_')[0]; chart.Level = level; }
                    continue;
                }
                if (line.Equals("#END", StringComparison.OrdinalIgnoreCase)) { if (reading) { ended = true; break; } reading = false; continue; }
                if (reading)
                {
                    if (line[0] == '#') tokens.Add(line);
                    else
                    {
                        foreach (char c in line.Where(c => !char.IsWhiteSpace(c))) tokens.Add(c.ToString());
                    }
                    continue;
                }
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string key = line.Substring(0, colon).Trim().ToUpperInvariant(), value = line.Substring(colon + 1).Trim();
                switch (key)
                {
                    case "TITLE": chart.Title = value; break;
                    case "SUBTITLE": chart.Subtitle = value.TrimStart('-', '+'); break;
                    case "BPM": chart.Bpm = Number(value); break;
                    case "OFFSET": chart.Offset = Number(value); break;
                    case "COURSE": course = value; balloons.Clear(); level = 0; break;
                    case "LEVEL": level = (int)Number(value); break;
                    // Match the reference parser's shared balloon pool and route cursor rewind.
                    case "BALLOONNOR": case "BALLOONEXP": balloons.AddRange(Balloons(value)); break;
                    case "BALLOONMAS": case "BALLOON": balloons = Balloons(value); break;
                }
            }
            if (!found || !ended) throw new FormatException($"No complete {requestedCourse} course (#START / #END).");
            if (!(chart.Bpm > 0) || double.IsInfinity(chart.Bpm)) throw new FormatException("BPM must be positive and finite.");
            new ChartReader(chart, balloons).Read(tokens);
            return chart;
        }

        static List<int> Balloons(string value) => value.Replace('.', ',').Split(',')
            .Where(s => s.Trim().Length > 0).Select(s => (int)Number(s)).ToList();

        sealed class TimingState
        {
            public double Time, Beat, Bpm, Measure = 1, ScrollX = 1, ScrollY;
            public bool Barline = true;
            public int BalloonIndex;
            public TimingState Copy() => (TimingState)MemberwiseClone();
        }

        sealed class ChartReader
        {
            readonly TaikoChart chart;
            readonly List<int> balloons;
            readonly List<string> pending = new List<string>();
            TimingState state, branchStart;
            ChartBranch branch;
            readonly bool[] routesSeen = new bool[3];
            BranchRoute route;
            bool hasRoute, branchBar;
            // Per route, the latest bar line and the latest long-note end after it, for
            // OpenTaiko's branch judgement time (CTja.GetBranchJudgeChipTime).
            readonly double?[] lastBar = new double?[3], rollEndAfterBar = new double?[3];
            ChartNote longNote;
            readonly List<ChartEntry> common = new List<ChartEntry>();
            List<ChartEntry>[] routeLists;
            List<ChartEntry> Entries => branch == null ? common : routeLists[(int)route];

            public ChartReader(TaikoChart chart, List<int> balloons)
            {
                this.chart = chart; this.balloons = balloons;
                state = new TimingState { Time = -chart.Offset, Bpm = chart.Bpm };
                chart.NoteLists.Add(common);
            }

            public void Read(List<string> tokens)
            {
                foreach (string token in tokens)
                {
                    string key = token.Split(new[] { ' ', '\t' }, 2)[0].ToUpperInvariant();
                    if (token == ",") Flush();
                    else if (key == "#BRANCHSTART" || key == "#BRANCHEND" || key == "#N" || key == "#E" || key == "#M")
                    {
                        FlushCommands();
                        Command(token);
                    }
                    else pending.Add(token);
                }
                FlushCommands();
                if (branch != null) EndBranch(); // #BRANCHEND is optional before #END / the next branch.
                CheckLongNote();
                chart.Duration = Math.Max(chart.Duration, state.Time);
                chart.Notes.Sort((a, b) => a.Time.CompareTo(b.Time));
                chart.Bars.Sort((a, b) => a.Time.CompareTo(b.Time));
                var gogos = chart.Gogos.OrderBy(g => g.Time).ToList(); // stable, unlike List.Sort
                chart.Gogos.Clear(); chart.Gogos.AddRange(gogos);
                var tempos = chart.Tempos.OrderBy(t => t.Time).ToList();
                chart.Tempos.Clear(); chart.Tempos.AddRange(tempos);
                NoteMoji.Assign(chart);
            }

            void FlushCommands()
            {
                if (pending.Any(t => t[0] != '#')) throw new FormatException("A measure is missing its comma before a branch boundary or #END.");
                foreach (string token in pending) Command(token);
                pending.Clear();
            }

            void CheckLongNote()
            {
                if (longNote != null) throw new FormatException("Long note is missing its 8 tail before a route boundary or #END.");
            }

            void EndBranch()
            {
                CheckLongNote();
                if (!routesSeen[0]) throw new FormatException("A branch must define #N exactly once.");
                // Match Fanmade's image renderer: omitted E uses N; omitted M uses E.
                if (!routesSeen[1]) branch.Routes[1] = BranchRoute.Normal;
                if (!routesSeen[2]) branch.Routes[2] = branch.Routes[1];
                branch.EndTime = state.Time;
                // As in OurTaikoPlayer, common notes continue from the final authored route.
                branch = null; hasRoute = false; branchBar = false;
            }

            void StartBranch(string arg)
            {
                if (branch != null) EndBranch();
                CheckLongNote();
                branchStart = state.Copy();
                branch = new ChartBranch { Id = chart.Branches.Count, Time = state.Time };
                ReadCondition(branch, arg);
                branch.DecisionTime = JudgeTime(branch.Condition == BranchCondition.Drumroll || branch.Condition == BranchCondition.BalloonHits);
                chart.Branches.Add(branch);
                routeLists = new[] { new List<ChartEntry>(), new List<ChartEntry>(), new List<ChartEntry>() };
                chart.NoteLists.AddRange(routeLists);
                Array.Clear(routesSeen, 0, routesSeen.Length);
                hasRoute = false;
            }

            // OpenTaiko judges a branch at the latest bar line before it (the start of the measure
            // before #BRANCHSTART), or four beats before the chart if there is none. For drumroll
            // and balloon conditions it waits for a roll ending in that last measure. Never after
            // the branch itself.
            double JudgeTime(bool waitForRolls)
            {
                double judge = lastBar.Max() ?? -chart.Offset - 240.0 / chart.Bpm;
                double? rollEnd = rollEndAfterBar.Max();
                if (waitForRolls && rollEnd > judge) judge = Math.Min(rollEnd.Value, state.Time);
                return judge;
            }

            // Marks the current route, or every route outside a branch.
            void Track(double?[] perRoute, double? value, bool latest = false)
            {
                for (int r = 0; r < 3; r++)
                    if (branch == null || r == (int)route) perRoute[r] = latest ? Math.Max(perRoute[r] ?? value.Value, value.Value) : value;
            }

            // OpenTaiko CTja #BRANCHSTART: `type, expert, master[, l|m]`; a missing comma after the
            // type is tolerated. An empty or unreadable condition keeps the current route; unlike
            // OpenTaiko, which drops the whole command, an unknown type or range does the same.
            void ReadCondition(ChartBranch target, string arg)
            {
                target.Condition = BranchCondition.None;
                arg = Regex.Replace(arg, @"(?<=^\s*[a-zA-Z]+)\s*(?=\d)", ",");
                if (string.IsNullOrWhiteSpace(arg)) return;
                string[] parts = arg.Split(',').Select(p => p.Trim()).ToArray();
                if (parts.Length < 3 || parts[0].Length == 0
                    || !TjaNumber.TryParse(parts[1], out double expert) || !TjaNumber.TryParse(parts[2], out double master))
                {
                    Warn("#BRANCHSTART " + arg);
                    return;
                }
                bool upper = char.IsUpper(parts[0][0]);
                BranchCondition? condition = parts[0].ToLowerInvariant() switch
                {
                    "p" => BranchCondition.Accuracy, "pp" => BranchCondition.PercentPerfect,
                    "jp" => BranchCondition.JudgePerfect, "d" => BranchCondition.JudgePerfect,
                    "jg" => BranchCondition.JudgeGood, "jb" => BranchCondition.JudgeBad,
                    "r" => BranchCondition.Drumroll, "rb" => BranchCondition.BalloonHits,
                    "s" => BranchCondition.Score, _ => null,
                };
                string range = parts.Length > 3 ? parts[3] : "";
                if (condition == null || range != "" && range != "l" && range != "m")
                {
                    Warn("#BRANCHSTART " + arg);
                    return;
                }
                target.Condition = condition.Value;
                // s is never big-only; d always is.
                target.BigOnly = parts[0].ToLowerInvariant() == "d" || upper && condition != BranchCondition.Score;
                target.Less = range == "l";
                target.ExpertThreshold = expert; target.MasterThreshold = master;
            }

            void Warn(string warning)
            {
                if (!chart.Warnings.Contains(warning)) chart.Warnings.Add(warning);
            }

            void SelectRoute(BranchRoute selected)
            {
                if (branch == null) throw new FormatException("Route marker without #BRANCHSTART.");
                CheckLongNote();
                if (routesSeen[(int)selected]) throw new FormatException("Duplicate branch route: " + selected);
                routesSeen[(int)selected] = true;
                state = branchStart.Copy(); route = selected; hasRoute = true; branchBar = true;
            }

            ChartNote NewNote() => new ChartNote { Time = state.Time, EndTime = state.Time,
                Beat = state.Beat, Bpm = state.Bpm, ScrollX = state.ScrollX, ScrollY = state.ScrollY,
                BranchId = branch == null ? -1 : branch.Id, Route = route };

            void SetGogo(bool on)
            {
                chart.Gogos.Add(new ChartGogo { Time = state.Time, On = on, BranchId = branch == null ? -1 : branch.Id, Route = route });
            }

            void AddBar()
            {
                if (branch != null && !hasRoute) throw new FormatException("Branch notes must follow #N, #E or #M.");
                var bar = NewNote(); bar.Display = state.Barline; bar.IsBranchStart = branchBar;
                chart.Bars.Add(bar);
                Entries.Add(new ChartEntry(bar, false));
                if (branch != null && branch.FirstEntries[(int)route] == null) branch.FirstEntries[(int)route] = bar;
                branchBar = false;
                Track(lastBar, state.Time);
                Track(rollEndAfterBar, null);
            }

            void Command(string command)
            {
                int space = command.IndexOfAny(new[] { ' ', '\t' });
                string key = (space < 0 ? command : command.Substring(0, space)).ToUpperInvariant();
                string arg = space < 0 ? "" : command.Substring(space + 1).Trim();
                switch (key)
                {
                    case "#BPMCHANGE":
                        state.Bpm = Number(arg);
                        if (!(state.Bpm > 0) || double.IsInfinity(state.Bpm)) throw new FormatException("Invalid BPMCHANGE.");
                        chart.Tempos.Add(new ChartTempo { Time = state.Time, ResumeTime = state.Time, Beat = state.Beat, Bpm = state.Bpm,
                            BranchId = branch == null ? -1 : branch.Id, Route = route });
                        break;
                    case "#MEASURE": var parts = arg.Split('/'); state.Measure = Number(parts[0]) / Number(parts[1]); if (!(state.Measure > 0) || double.IsInfinity(state.Measure)) throw new FormatException("Invalid MEASURE."); break;
                    case "#SCROLL":
                        var match = Regex.Match(arg, @"^([+-]?[\d.]+)([+-][\d.]+)i$");
                        state.ScrollX = match.Success ? Number(match.Groups[1].Value) : Number(arg);
                        state.ScrollY = match.Success ? Number(match.Groups[2].Value) : 0;
                        break;
                    case "#DELAY":
                        double delay = Number(arg);
                        chart.Tempos.Add(new ChartTempo { Time = state.Time, ResumeTime = state.Time + delay,
                            Beat = state.Beat, Bpm = state.Bpm, BranchId = branch == null ? -1 : branch.Id, Route = route });
                        state.Time += delay;
                        break;
                    case "#GOGOSTART": SetGogo(true); break;
                    case "#GOGOEND": SetGogo(false); break;
                    case "#BARLINEOFF": state.Barline = false; break;
                    case "#BARLINEON": state.Barline = true; break;
                    case "#BRANCHSTART": StartBranch(arg); break;
                    case "#BRANCHEND": if (branch == null) throw new FormatException("#BRANCHEND without #BRANCHSTART."); EndBranch(); break;
                    case "#N": SelectRoute(BranchRoute.Normal); break;
                    case "#E": SelectRoute(BranchRoute.Expert); break;
                    case "#M": SelectRoute(BranchRoute.Master); break;
                    case "#SECTION":
                        chart.Sections.Add(new ChartSection { Time = state.Time, BranchId = branch == null ? -1 : branch.Id, Route = route });
                        break;
                    case "#LEVELHOLD": case "#BMSCROLL": case "#HBSCROLL":
                        throw new NotSupportedException($"{key} is not supported by the single-lane port yet.");
                    default: if (!chart.Warnings.Contains(key)) chart.Warnings.Add(key); break;
                }
            }

            void Flush()
            {
                int slots = pending.Count(t => t[0] != '#'), index = 0;
                bool addedBar = false;
                foreach (string token in pending)
                {
                    if (token[0] == '#') { Command(token); continue; }
                    if (!addedBar)
                    {
                        AddBar();
                        addedBar = true;
                    }
                    if (token[0] < '0' || token[0] > '9') throw new NotSupportedException($"Unsupported TJA note: {token}");
                    int type = token[0] - '0';
                    if (type == 8)
                    {
                        if (longNote == null) throw new FormatException("Long-note tail has no head.");
                        longNote.EndTime = state.Time;
                        longNote.TailBpm = state.Bpm;
                        Track(rollEndAfterBar, state.Time, latest: true);
                        Entries.Add(new ChartEntry(longNote, true));
                        longNote = null;
                    }
                    else if (type != 0)
                    {
                        if (longNote != null) throw new FormatException("Overlapping long notes are not supported.");
                        var note = NewNote(); note.Kind = (NoteKind)type;
                        if (note.IsBalloon) { note.BalloonHits = state.BalloonIndex < balloons.Count ? balloons[state.BalloonIndex] : 1; state.BalloonIndex++; }
                        chart.Notes.Add(note);
                        Entries.Add(new ChartEntry(note, false));
                        if (note.IsLong) longNote = note;
                    }
                    state.Time += 240.0 / state.Bpm * state.Measure / Math.Max(1, slots);
                    state.Beat += 4.0 * state.Measure / Math.Max(1, slots);
                    index++;
                }
                if (index == 0) { AddBar(); state.Time += 240.0 / state.Bpm * state.Measure; state.Beat += 4 * state.Measure; }
                chart.Duration = Math.Max(chart.Duration, state.Time);
                pending.Clear();
            }
        }
    }
}
