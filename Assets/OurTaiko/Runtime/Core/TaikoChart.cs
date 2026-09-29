using System;
using System.Collections.Generic;

namespace OurTaiko
{
    public enum NoteKind { Don = 1, Ka, BigDon, BigKa, Roll, BigRoll, Balloon = 7, Kusudama = 9 }
    public enum Judgment { None, Good, Ok, Bad, Roll }

    public sealed class ChartNote
    {
        public NoteKind Kind;
        public double Time, EndTime, Bpm, ScrollX = 1, ScrollY;
        public bool Gogo;
        public int BalloonHits;
        public bool IsLong => (int)Kind >= 5;
        public bool IsBalloon => Kind == NoteKind.Balloon || Kind == NoteKind.Kusudama;
        public bool IsKa => Kind == NoteKind.Ka || Kind == NoteKind.BigKa;
    }

    public sealed class TaikoChart
    {
        public string Title = "Untitled", Subtitle = "", Course = "Oni";
        public int Level;
        public double Bpm = 120, Offset, Duration;
        public readonly List<ChartNote> Notes = new List<ChartNote>();
        public readonly List<ChartNote> Bars = new List<ChartNote>();
        public readonly List<string> Warnings = new List<string>();
    }
}
