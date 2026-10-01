using System;
using System.Collections.Generic;
using System.Globalization;

namespace OurTaiko
{
    // Difficulty columns of the original song select (src/libs/global_data.h Difficulty);
    // Edit is the Ura course that shares the Oni column.
    public enum Difficulty { Back = -3, Modifier = -2, Neiro = -1, Easy = 0, Normal, Hard, Oni, Ura }

    public sealed class CourseInfo
    {
        public Difficulty Difficulty;
        public string Course; // The TJA's own COURSE value, passed back to TjaParser.Parse.
        public int Level;
        public bool IsBranching;
    }

    // Song-select metadata of a TJA file: the header and every course with a complete #START/#END.
    public sealed class SongInfo
    {
        public string Title = "Untitled", Subtitle = "", Genre = "";
        public double Bpm = 120, DemoStart;
        public readonly List<CourseInfo> Courses = new List<CourseInfo>();

        static readonly string[] CourseNames = { "Easy", "Normal", "Hard", "Oni", "Edit" };

        public static string CourseName(Difficulty difficulty) => CourseNames[(int)difficulty];

        public static Difficulty? DifficultyOf(string course)
        {
            string value = course.Trim();
            if (int.TryParse(value, out int n)) return n >= 0 && n < CourseNames.Length ? (Difficulty)n : (Difficulty?)null;
            if (value.Equals("Ura", StringComparison.OrdinalIgnoreCase)) return Difficulty.Ura;
            for (int i = 0; i < CourseNames.Length; i++)
                if (value.Equals(CourseNames[i], StringComparison.OrdinalIgnoreCase)) return (Difficulty)i;
            return null;
        }

        public CourseInfo Course(Difficulty difficulty) => Courses.Find(c => c.Difficulty == difficulty);
        public bool Has(Difficulty difficulty) => Course(difficulty) != null;

        public static SongInfo Read(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("The TJA chart is empty.");
            var info = new SongInfo();
            string course = "Oni";
            int level = 0;
            CourseInfo reading = null;
            foreach (string raw in text.TrimStart('﻿').Replace("\r", "").Split('\n'))
            {
                string line = raw.Split(new[] { "//" }, StringSplitOptions.None)[0].Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#START", StringComparison.OrdinalIgnoreCase))
                {
                    var difficulty = DifficultyOf(course);
                    reading = difficulty == null ? null : new CourseInfo { Difficulty = difficulty.Value, Course = course, Level = level };
                    continue;
                }
                if (line.Equals("#END", StringComparison.OrdinalIgnoreCase))
                {
                    // A later block of the same course replaces the earlier one, like the reference parser.
                    if (reading != null) { info.Courses.RemoveAll(c => c.Difficulty == reading.Difficulty); info.Courses.Add(reading); }
                    reading = null;
                    continue;
                }
                if (reading != null)
                {
                    if (line.StartsWith("#BRANCHSTART", StringComparison.OrdinalIgnoreCase)) reading.IsBranching = true;
                    continue;
                }
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string key = line.Substring(0, colon).Trim().ToUpperInvariant(), value = line.Substring(colon + 1).Trim();
                switch (key)
                {
                    case "TITLE": info.Title = value; break;
                    case "SUBTITLE": info.Subtitle = value.TrimStart('-', '+'); break;
                    case "GENRE": info.Genre = value; break;
                    case "BPM": info.Bpm = Number(value, info.Bpm); break;
                    case "DEMOSTART": info.DemoStart = Number(value, 0); break;
                    case "COURSE": course = value; level = 0; break;
                    case "LEVEL": level = (int)Number(value, 0); break;
                }
            }
            info.Courses.Sort((a, b) => a.Difficulty.CompareTo(b.Difficulty));
            return info;
        }

        static double Number(string value, double fallback)
            => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ? result : fallback;
    }
}
