using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace OurTaiko
{
    // TJA time is measured relative to the audio start. Positive OFFSET starts the chart earlier.
    // Commands are kept between note slots, so a BPM/SCROLL change inside a measure stays in place.
    public static class TjaParser
    {
        static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);
        static string Course(string value)
        {
            string[] names = { "Easy", "Normal", "Hard", "Oni", "Edit" };
            return int.TryParse(value, out int n) && n >= 0 && n < names.Length ? names[n] : value;
        }

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
                    if (reading) { found = true; chart.Course = Course(course); chart.Level = level; }
                    continue;
                }
                if (line == "#END") { if (reading) { ended = true; break; } reading = false; continue; }
                if (reading)
                {
                    if (line[0] == '#') tokens.Add(line);
                    else foreach (char c in line.Where(c => !char.IsWhiteSpace(c))) tokens.Add(c.ToString());
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
                    case "BALLOON": balloons = value.Split(',').Where(s => s.Trim().Length > 0).Select(s => (int)Number(s)).ToList(); break;
                }
            }
            if (!found || !ended) throw new FormatException($"No complete {requestedCourse} course (#START / #END).");
            if (!(chart.Bpm > 0) || double.IsInfinity(chart.Bpm)) throw new FormatException("BPM must be positive and finite.");
            double time = -chart.Offset, bpm = chart.Bpm, measure = 1, sx = 1, sy = 0;
            bool gogo = false, barline = true;
            int balloonIndex = 0;
            ChartNote longNote = null;
            var pending = new List<string>();

            void Command(string command)
            {
                int space = command.IndexOf(' ');
                string key = (space < 0 ? command : command.Substring(0, space)).ToUpperInvariant();
                string arg = space < 0 ? "" : command.Substring(space + 1).Trim();
                switch (key)
                {
                    case "#BPMCHANGE": bpm = Number(arg); if (!(bpm > 0) || double.IsInfinity(bpm)) throw new FormatException("Invalid BPMCHANGE."); break;
                    case "#MEASURE": var parts = arg.Split('/'); measure = Number(parts[0]) / Number(parts[1]); if (!(measure > 0) || double.IsInfinity(measure)) throw new FormatException("Invalid MEASURE."); break;
                    case "#SCROLL":
                        var match = Regex.Match(arg, @"^([+-]?[\d.]+)([+-][\d.]+)i$");
                        sx = match.Success ? Number(match.Groups[1].Value) : Number(arg);
                        sy = match.Success ? Number(match.Groups[2].Value) : 0;
                        break;
                    case "#DELAY": time += Number(arg); break;
                    case "#GOGOSTART": gogo = true; break;
                    case "#GOGOEND": gogo = false; break;
                    case "#BARLINEOFF": barline = false; break;
                    case "#BARLINEON": barline = true; break;
                    case "#BRANCHSTART": case "#N": case "#E": case "#M": case "#BMSCROLL": case "#HBSCROLL":
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
                        if (barline) chart.Bars.Add(new ChartNote { Time = time, Bpm = bpm, ScrollX = sx, ScrollY = sy });
                        addedBar = true;
                    }
                    if (token[0] < '0' || token[0] > '9') throw new NotSupportedException($"Unsupported TJA note: {token}");
                    int type = token[0] - '0';
                    if (type == 8)
                    {
                        if (longNote == null) throw new FormatException("Long-note tail has no head.");
                        longNote.EndTime = time;
                        longNote = null;
                    }
                    else if (type != 0)
                    {
                        if (longNote != null) throw new FormatException("Overlapping long notes are not supported.");
                        var note = new ChartNote { Kind = (NoteKind)type, Time = time, EndTime = time, Bpm = bpm, ScrollX = sx, ScrollY = sy, Gogo = gogo };
                        if (note.IsBalloon) note.BalloonHits = balloonIndex < balloons.Count ? balloons[balloonIndex++] : 5;
                        chart.Notes.Add(note);
                        if (note.IsLong) longNote = note;
                    }
                    time += 240.0 / bpm * measure / Math.Max(1, slots);
                    index++;
                }
                if (index == 0) time += 240.0 / bpm * measure;
                pending.Clear();
            }
            foreach (string token in tokens) { if (token == ",") Flush(); else pending.Add(token); }
            if (pending.Any(t => t[0] != '#')) throw new FormatException("Last measure is missing a comma.");
            foreach (string command in pending) Command(command);
            if (longNote != null) throw new FormatException("Long note is missing its 8 tail.");
            chart.Duration = time;
            return chart;
        }
    }
}
