using System.Collections.Generic;

namespace OurTaiko
{
    // A recorded play's drum presses (Online.PlayRecord), handed back in the order the game judged
    // them. The record holds judged chart times with the judgment offset already taken off, so a
    // replay judges them in a session without one, at any frame rate and playback speed.
    public sealed class ReplayFeed
    {
        public Online.PlayRecord Record { get; }
        readonly List<(double Ms, int Type)> inputs;
        int next;

        public ReplayFeed(Online.PlayRecord record) { Record = record; inputs = record.Inputs; }

        public int Remaining => inputs.Count - next;

        // The next press when it is due by chart time `time` (seconds). A hard audio resync can leave an
        // earlier time after a later one; the order of the record is kept, as the game judged it.
        public bool TryTake(double time, out double at, out bool ka, out bool right)
        {
            at = 0; ka = right = false;
            if (next >= inputs.Count || inputs[next].Ms / 1000 > time) return false;
            var (ms, type) = inputs[next++];
            at = ms / 1000; ka = Online.PlayRecord.IsKa(type); right = Online.PlayRecord.IsRight(type);
            return true;
        }

        // The play as it stood at `time`: every press up to it judged, without sounds or effects, in a
        // new session that evaluates the branches. The feed goes on from there.
        public PlaySession SessionAt(TaikoChart chart, double time)
        {
            next = 0;
            var session = new PlaySession(chart);
            while (TryTake(time, out double at, out bool ka, out _)) session.Hit(ka, at);
            session.Advance(time, false);
            return session;
        }
    }
}
