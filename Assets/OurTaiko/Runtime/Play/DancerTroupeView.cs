using System;
using System.Collections.Generic;
using OurTaiko.Chara;
using UnityEngine;

namespace OurTaiko
{
    // The play backdrop's dancers (Nijiiro ArcadeDancerGroup's rules): three come in at the start,
    // the fourth joins from half a soul gauge and the fifth with the clear state (a collab troupe's
    // lead comes alone, and the rest by the clear), all on one playhead that follows the chart's
    // tempo. Each dancer's clip holds its whole timeline, drawn where that dancer stands; this view
    // only picks the frame. A troupe that is one movie is all on stage, replayed on the beat.
    public sealed class DancerTroupeView : MonoBehaviour
    {
        [Tooltip("In the order they come: centre, left, right, far left, far right. Each one's clip is drawn in its own place, so they are not dealt out.")]
        public DancerView[] dancers;

        // Null until the stage gives a troupe.
        public DancerTroupe Troupe { get; private set; }
        public bool IsMovie { get; private set; }
        bool leadFirst;
        double shownTime = double.NaN;

        void Awake()
        {
            foreach (var dancer in dancers) dancer.Show(-1);
        }

        // The troupe's members in the order they come, and its rig's frames; no members clears the stage.
        public void Use(IReadOnlyList<OtanimAsset> members, StageTroupe troupe)
        {
            int count = Math.Min(members?.Count ?? 0, dancers.Length);
            for (int i = 0; i < dancers.Length; i++) dancers[i].Use(i < count ? members[i] : null);
            shownTime = double.NaN;
            IsMovie = false;
            Troupe = null;
            if (count == 0) return;
            IsMovie = troupe.movie;
            leadFirst = troupe.leadFirst;
            var rigs = new DancerRig[IsMovie ? 1 : count];
            // A movie has no way in or out: the whole clip is its dance.
            for (int i = 0; i < rigs.Length; i++)
                rigs[i] = IsMovie ? new DancerRig(dancers[i].Frames, 0, dancers[i].Frames) : new DancerRig(dancers[i].Frames, troupe.dance, troupe.LeaveAt(i));
            Troupe = new DancerTroupe(rigs, leadFirst);
        }

        // gauge is the soul gauge's fill relative to its difficulty's clear line. A time already shown (a still pause) is left as it is.
        public void ShowTime(double time, double bpm, double gauge, bool clear)
        {
            if (Troupe == null || time == shownTime) return;
            shownTime = time;
            Troupe.Advance(time, bpm);
            if (IsMovie)
            {
                int frames = dancers[0].Frames;
                dancers[0].Show(frames > 0 ? (int)(Troupe.Frame % frames) : -1);
                return;
            }
            Troupe.SetCount(DancerTroupe.CountFor(gauge, clear, leadFirst));
            for (int i = 0; i < Troupe.Slots; i++) dancers[i].Show(Troupe.FrameOf(i));
        }
    }
}
