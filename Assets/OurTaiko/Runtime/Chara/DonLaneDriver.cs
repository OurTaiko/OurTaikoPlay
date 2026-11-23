using System;
using OurTaiko.Chara;
using UnityEngine;

namespace OurTaiko
{
    // A lane's 3D Don during play (YataiDON's game Player and its chara): which motion the play
    // calls for, its tempo, its tint, and where it stands. PlayerLane feeds it judgments and the
    // frame's chart time.
    public sealed class DonLaneDriver
    {
        // YataiDON skin chara_3d, Nijiiro layout (1920×1080), from the lane's top left: "game_p1",
        // "game_p2" (a 2P lane, below it), "game_balloon" (while a balloon is being hit). The judge
        // counter panel (JudgeCounterView) stands to the right of game_p1 and game_p2, clear of both.
        static readonly Vector2 SpotP1 = new Vector2(248, 29);
        static readonly Vector2 SpotP2 = new Vector2(240, 558);
        static readonly Vector2 SpotBalloon = new Vector2(630, 180);
        // Player::draw_lane_cover: standing on the lane, over the header and the song title, under
        // the lane (-50) and notes.
        public const int SortingOrder = -60;
        // Player::draw_overlays: holding a balloon, over the lane, its notes and its foreground (50:
        // drum, nameplate) and under the HUD (100) with the judge and balloon counters.
        public const int BalloonSortingOrder = 90;
        const int MissStreakTint = PlayerLane.MissStreakDark; // misses in a row that darken the Don

        readonly PlayerLane lane;
        public DonView View { get; }
        Chara3D Chara => View.Chara;
        readonly Vector2 home;

        bool gogo, gaugeClear, gaugeFull, tintedMissStreak, tintedRainbow;
        // A balloon: its note, whether hits stopped for a while, whether its counter is up.
        int balloon = -1, finishedBalloon = -1;
        bool balloonIdle, balloonCounting;
        double lastBalloonHit;

        public DonLaneDriver(PlayerLane lane, Transform laneRoot, bool lowerLane)
        {
            this.lane = lane;
            home = lowerLane ? SpotP2 : SpotP1;
            View = DonView.Create(laneRoot, "Don", home.x, home.y, sortingOrder: SortingOrder);
            View.transform.SetAsFirstSibling();
            View.Show(DonLooks.For(lane.Seat), false, useSkinPose: true);
            View.Seat = lane.Seat;
        }

        DonAnim RestAnim => gogo ? DonAnim.Sabi : gaugeClear ? DonAnim.NormLoop : DonAnim.Normal;

        // A new play or practice attempt: the Don starts over, as at the start of a song.
        public void Reset()
        {
            gogo = gaugeClear = gaugeFull = tintedMissStreak = tintedRainbow = false;
            balloon = finishedBalloon = -1; balloonIdle = balloonCounting = false;
            if (Chara == null) return;
            Chara.SetTint(Color.black, 0);
            Chara.SetAnim(DonAnim.Normal);
            Stand(false);
        }

        // Where the Don stands and which layer draws it: on its spot under the lane, or holding the
        // balloon over it.
        void Stand(bool holdingBalloon)
        {
            var spot = holdingBalloon ? SpotBalloon : home;
            View.MoveTo(spot.x, spot.y);
            View.SortingOrder = holdingBalloon ? BalloonSortingOrder : SortingOrder;
        }

        public void OnJudged(int index, Judgment result)
        {
            if (Chara == null || lane.Session == null) return;
            var note = lane.Session.Chart.Notes[index];
            if (result == Judgment.Roll)
            {
                if (note.Kind == NoteKind.Balloon) BalloonHit(index, note);
                return;
            }
            if (result == Judgment.Bad)
            {
                // Player::on_miss
                if (!gogo && lane.MissStreak <= MissStreakTint)
                    Chara.SetAnim(lane.MissStreak == MissStreakTint ? DonAnim.Miss6 : DonAnim.Miss);
                return;
            }
            // Player::on_note_correct: every tenth combo outside GOGO TIME
            int combo = lane.DisplayCombo;
            if (!note.IsLong && combo > 0 && combo % 10 == 0 && !gogo) Chara.SetAnim(DonAnim.Combo);
        }

        void BalloonHit(int index, ChartNote note)
        {
            if (index == finishedBalloon) return; // hits judged after it popped
            if (balloon != index) { balloon = index; balloonIdle = true; balloonCounting = false; }
            balloonCounting = true;
            if (balloonIdle)
            {
                balloonIdle = false;
                Chara.SetAnim(DonAnim.BalloonLoop);
            }
            lastBalloonHit = Chara3D.NowMs;
            if (lane.Session.LongHits[index] >= note.BalloonHits)
            {
                // Player::balloon_counter_manager: popped
                EndBalloon();
                Chara.SetAnim(RestAnim);
                Chara.SetAnim(DonAnim.BalloonSuccess);
            }
        }

        void EndBalloon()
        {
            finishedBalloon = balloon;
            balloon = -1; balloonIdle = false; balloonCounting = false;
            Stand(false);
        }

        // Once a frame, on the chart clock: tempo, GOGO, the gauge, the balloon, the tint.
        public void Update(double time)
        {
            if (Chara == null || lane.Session == null) return;
            var session = lane.Session;
            Chara.Bpm = (float)Math.Abs(session.BpmAt(time));

            bool gogoNow = session.IsGogo(time);
            if (gogoNow != gogo)
            {
                gogo = gogoNow;
                if (gogo) { Chara.SetAnim(DonAnim.Sabi); Chara.SetAnim(DonAnim.SabiStart); }
                else if (lane.MissStreak > 0 && lane.MissStreak < MissStreakTint) Chara.SetAnim(DonAnim.Miss);
                else if (lane.MissStreak > 0) Chara.SetAnim(DonAnim.Miss6);
                else Chara.SetAnim(RestAnim);
            }

            var shared = lane.SharedGauge;
            bool fullNow = shared?.IsFull ?? session.IsGaugeFull;
            if (fullNow && !gaugeFull) Chara.SetAnim(DonAnim.FullGage);
            gaugeFull = fullNow;
            bool streakNow = lane.MissStreak >= MissStreakTint;
            if (streakNow != tintedMissStreak || fullNow != tintedRainbow)
            {
                if (tintedMissStreak && !streakNow) Chara.SetAnim(RestAnim);
                tintedMissStreak = streakNow;
                tintedRainbow = fullNow;
                if (streakNow) Chara.SetTint(Color.black, 0.35f);
                else if (fullNow) Chara.SetTint(new Color32(125, 110, 5, 255), 0);
                else Chara.SetTint(Color.black, 0);
            }
            bool clearNow = shared?.IsClear ?? session.IsClear;
            if (clearNow != gaugeClear)
            {
                gaugeClear = clearNow;
                if (!gogo)
                {
                    Chara.SetAnim(RestAnim);
                    Chara.SetAnim(clearNow ? DonAnim.NormUp : DonAnim.NormDown);
                }
            }
            UpdateBalloon(time);
        }

        void UpdateBalloon(double time)
        {
            if (balloon < 0) return;
            var note = lane.Session.Chart.Notes[balloon];
            // (Session.Resolved turns true at the first hit, so only the time tells when it is over.)
            bool over = time > note.EndTime + lane.JudgeOffset;
            if (over)
            {
                bool popped = lane.Session.LongHits[balloon] >= note.BalloonHits;
                EndBalloon();
                Chara.SetAnim(RestAnim);
                if (!popped) Chara.SetAnim(DonAnim.BalloonFailure);
                return;
            }
            // Hits stopped for 400 ms: the Don waits with the balloon.
            if (!balloonIdle && Chara3D.NowMs - lastBalloonHit > 400)
            {
                balloonIdle = true;
                Chara.SetAnim(DonAnim.BalloonNoBeat);
            }
            // Player::draw_lane_cover / draw_overlays: the Don holds the balloon while it is hit.
            Stand(balloonCounting);
        }

        // Player::spawn_ending_anim: a cleared play without a 不可 cheers.
        public void OnEnding(bool cleared, int bad)
        {
            if (Chara == null) return;
            if (balloon >= 0) EndBalloon();
            if (cleared && bad == 0) Chara.SetAnim(DonAnim.FullCombo);
        }

        public void Hide() => View.Hide();
    }
}
