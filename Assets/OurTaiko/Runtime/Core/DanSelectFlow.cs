using System;

namespace OurTaiko
{
    // YataiDON DanSelectScreen without its timer (the simulator does not limit the player's time):
    // ka walks the course ribbon (it wraps), don opens the confirmation on a playable course, where
    // ka moves between オプション, はい and いいえ (left to right, いいえ first) after a 500 ms lock and
    // don picks one. Times are real-clock milliseconds.
    public sealed class DanSelectFlow
    {
        public enum Phase { Browsing, Confirm, Options, Started, Left }
        public enum Choice { Option, Yes, No }

        public const double ConfirmInputLockMs = 500;
        // DanNavigator::move_left/right: the ribbon slides for 166 ms.
        public const double SlideMs = 166;

        readonly Func<int, bool> playable;

        public int Count { get; }
        public int Selected { get; private set; }
        public Phase State { get; private set; } = Phase.Browsing;
        public Choice Cursor { get; private set; } = Choice.No;
        public double ConfirmOpenedAt { get; private set; } = double.NaN;
        public double MovedAt { get; private set; } = double.NaN;
        public int LastMove { get; private set; }

        public DanSelectFlow(int count, Func<int, bool> playable, int selected = 0)
        {
            Count = Math.Max(0, count);
            this.playable = playable ?? (_ => true);
            Selected = Count == 0 ? 0 : ((selected % Count) + Count) % Count;
        }

        public bool IsSelectedPlayable => Count > 0 && playable(Selected);

        // A ka in the ribbon: the previous (-1) or next (+1) course, wrapping around.
        public bool Move(int delta, double now)
        {
            if (State != Phase.Browsing || Count < 2 || delta == 0) return false;
            Selected = ((Selected + delta) % Count + Count) % Count;
            MovedAt = now; LastMove = Math.Sign(delta);
            return true;
        }

        // A don in the ribbon: the confirmation opens (いいえ selected) when the course can be played.
        public bool Open(double now)
        {
            if (State != Phase.Browsing || !IsSelectedPlayable) return false;
            State = Phase.Confirm;
            Cursor = Choice.No;
            ConfirmOpenedAt = now;
            return true;
        }

        public bool AcceptsConfirmInput(double now) => State == Phase.Confirm && now - ConfirmOpenedAt >= ConfirmInputLockMs;

        // A ka in the confirmation, clamped at both ends.
        public bool MoveChoice(int delta, double now)
        {
            if (!AcceptsConfirmInput(now)) return false;
            int next = Math.Max(0, Math.Min(2, (int)Cursor + Math.Sign(delta)));
            if (next == (int)Cursor) return false;
            Cursor = (Choice)next;
            return true;
        }

        // A tap on a choice moves the cursor to it.
        public bool Point(Choice choice, double now)
        {
            if (!AcceptsConfirmInput(now)) return false;
            Cursor = choice;
            return true;
        }

        // A don in the confirmation: the choice under the cursor. Returns what was picked, or null.
        public Choice? Decide(double now)
        {
            if (!AcceptsConfirmInput(now)) return null;
            switch (Cursor)
            {
                case Choice.Option: State = Phase.Options; break;
                case Choice.Yes: State = Phase.Started; break;
                default: State = Phase.Browsing; break;
            }
            return Cursor;
        }

        // The options panel has slid out: back to the confirmation, the cursor still on オプション.
        public void OptionsClosed()
        {
            if (State == Phase.Options) State = Phase.Confirm;
        }

        // Esc / back: the confirmation closes, or the ribbon leaves for Entry.
        public bool Back()
        {
            if (State == Phase.Confirm) { State = Phase.Browsing; return true; }
            if (State == Phase.Browsing) { State = Phase.Left; return true; }
            return false;
        }
    }
}
