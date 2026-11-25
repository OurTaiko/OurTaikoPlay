using System;
using System.Collections.Generic;

namespace OurTaiko
{
    // One dancer's rig timeline, in rig frames: the hop in, the looped dance, the way out.
    public readonly struct DancerRig
    {
        public readonly int Frames, Dance, Out;
        public DancerRig(int frames, int dance, int leave) { Frames = frames; Dance = dance; Out = leave; }
    }

    // Nijiiro bg_objects/dancer.lua ArcadeDancerGroup: the whole troupe shares one playhead that
    // advances BeatFrames per beat, and every dancer's dance is a pure function of it, so dancers
    // in the same state are on the same frame. Slots are in spawn order: centre, left, right,
    // far left, far right.
    // The hop in is the arcade's, not the script's: it takes InSeconds whatever the tempo (measured
    // on the arcade) and lands on a phrase, where the dance begins. The troupe that opens the play
    // starts hopping in at once, so the playhead starts that far short of the dance.
    public sealed class DancerTroupe
    {
        public const int BeatFrames = 40, Phrase = 80, MinDancers = 3;
        public const double InSeconds = 0.5;

        enum State { Idle, In, Dance, Out }
        // Start is the troupe clock's time the hop in starts; Join, Exit are playhead frames. NaN Join: one of
        // the troupe that opens the play, placed when the first time comes.
        struct Dancer { public State State; public double Start, Join, Exit; }

        readonly DancerRig[] rigs;
        readonly Dancer[] dancers;
        double now = double.NaN, rate;
        // Seconds the troupe has played: like the playhead, it never runs backwards.
        double clock;

        public double Frame { get; private set; }
        public int Active { get; private set; }
        public int Slots => rigs.Length;

        // The fewest on stage: three of the usual troupe, one of a troupe whose lead comes first.
        readonly int least;

        public DancerTroupe(IReadOnlyList<DancerRig> slots, bool leadFirst = false)
        {
            least = leadFirst ? 1 : MinDancers;
            rigs = new DancerRig[slots.Count];
            for (int i = 0; i < rigs.Length; i++) rigs[i] = slots[i];
            dancers = new Dancer[rigs.Length];
            // They hop in together the moment the first time comes.
            SetCount(least);
        }

        // Background:handle_dancer_count with the engine's clear_progress of 1: the fourth dancer
        // from half a gauge, the fifth with the clear state.
        // A collab troupe (leadFirst) starts with its lead alone and grows to five at the clear.
        public static int CountFor(double gauge, bool clear, bool leadFirst = false)
            => clear ? 5
                : leadFirst ? Math.Max(1, Math.Min(5, (int)Math.Floor(4 * gauge + 1)))
                : Math.Max(MinDancers, Math.Min(5, (int)Math.Floor(2 * gauge + 3)));

        // Time never runs the playhead backwards; a tempo change retimes the whole troupe at once.
        public void Advance(double time, double bpm)
        {
            bpm = Math.Abs(bpm);
            rate = (bpm > 0 ? bpm : 120) / 60 * BeatFrames;
            if (double.IsNaN(now))
            {
                now = time;
                // The opening troupe lands on the first phrase, InSeconds from now.
                if (rigs.Length > 0) Frame = rigs[0].Dance - InFrames(rigs[0]);
                for (int i = 0; i < dancers.Length; i++)
                    if (dancers[i].State == State.In)
                    {
                        dancers[i].Join = rigs[i].Dance;
                        dancers[i].Start = clock;
                    }
            }
            double elapsed = Math.Max(0, time - now);
            now = time;
            clock += elapsed;
            Frame += elapsed * rate;
            for (int i = 0; i < dancers.Length; i++)
            {
                ref var dancer = ref dancers[i];
                if (dancer.State == State.In && Frame >= dancer.Join) dancer.State = State.Dance;
                else if (dancer.State == State.Out && Frame >= dancer.Exit + (rigs[i].Frames - rigs[i].Out)) dancer.State = State.Idle;
            }
        }

        // Grows and shrinks slot by slot, from the centre outwards.
        public void SetCount(int count)
        {
            count = Math.Max(Math.Min(least, rigs.Length), Math.Min(rigs.Length, count));
            while (Active < count) Enter(Active++);
            while (Active > count) Leave(--Active);
        }

        // The rig frame this slot shows, or -1 while it is off stage.
        public int FrameOf(int slot)
        {
            var dancer = dancers[slot];
            var rig = rigs[slot];
            switch (dancer.State)
            {
                case State.Idle: return -1;
                // Earned and still waiting for its phrase, or a rig with no way in.
                case State.In when double.IsNaN(now) || clock < dancer.Start || rig.Dance == 0: return -1;
                // The way in, rig frames 0 to Dance, played over InSeconds; it holds its last
                // frame if a slower tempo puts the phrase further off.
                case State.In: return Math.Min(rig.Dance - 1, (int)Math.Floor(rig.Dance * (clock - dancer.Start) / InSeconds));
                case State.Out when Frame >= dancer.Exit:
                    int frame = rig.Out + (int)Math.Floor(Frame - dancer.Exit);
                    return frame >= rig.Frames ? -1 : frame;
                default:
                    double looped = (Frame - rig.Dance) % (rig.Out - rig.Dance);
                    return rig.Dance + (int)Math.Floor(looped);
            }
        }

        void Enter(int slot)
        {
            ref var dancer = ref dancers[slot];
            dancer.State = State.In;
            // Before the first time it is one of the opening troupe; Advance places it.
            if (double.IsNaN(now)) { dancer.Join = double.NaN; return; }
            // The first phrase it can still hop in for whole, the hop timed at the current tempo.
            dancer.Join = PhraseAtOrAfter(Frame + InFrames(rigs[slot]), rigs[slot].Dance);
            dancer.Start = clock + (dancer.Join - Frame) / rate - (rigs[slot].Dance > 0 ? InSeconds : 0);
        }

        // How far the playhead runs during the way in at the current tempo; a rig with no way in has none.
        double InFrames(DancerRig rig) => rig.Dance > 0 ? InSeconds * rate : 0;

        void Leave(int slot)
        {
            ref var dancer = ref dancers[slot];
            if (dancer.State == State.Idle || dancer.State == State.Out) return;
            // Earned and lost again before it ever landed: it simply never arrives.
            if (dancer.State == State.In && !(Frame >= dancer.Join)) { dancer.State = State.Idle; return; }
            dancer.Exit = PhraseAtOrAfter(Frame, rigs[slot].Dance);
            dancer.State = State.Out;
        }

        // Smallest frame at or after this one that is a whole number of phrases from the dance start.
        static double PhraseAtOrAfter(double frame, int dance)
            => dance + Math.Ceiling((frame - dance) / Phrase) * Phrase;
    }
}
