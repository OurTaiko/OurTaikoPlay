using System;

namespace OurTaiko
{
    public static class NoteExpression
    {
        // YataiDON Player::draw_notes: at 50 combo, alternate frames every eighth note.
        // Use absolute chart time at the current BPM, not accumulated beats or a per-note
        // clock. Tempo changes therefore re-evaluate the phase exactly as in the source.
        public static int Frame(double time, double bpm, int combo)
        {
            if (combo < 50 || !(bpm > 0) || double.IsInfinity(bpm) || double.IsNaN(time) || double.IsInfinity(time)) return 0;
            double eighth = Math.Truncate(time / (30.0 / bpm));
            return (int)((eighth % 2 + 2) % 2);
        }
    }
}
