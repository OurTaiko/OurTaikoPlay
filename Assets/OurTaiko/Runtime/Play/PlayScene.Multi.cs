using System;
using System.Collections;
using System.Linq;
using OurTaiko.Multi;
using OurTaiko.Multi.Proto;
using UnityEngine;

namespace OurTaiko
{
    // マルチ演奏 (SceneSwitcher.Multi) in Play.Single: the room's song on the course this player
    // took, with the other players' scores along the top (MultiHudView). The play is an ordinary
    // one (the player's own options, drum and offsets, standard judge windows, no オート) and its
    // score is saved and uploaded as after any play. What differs: it starts when the room says
    // every player has loaded, it cannot be paused or restarted, it reports its score to the room
    // as it goes and its result when it ends, and the result screen returns to the room.
    public sealed partial class PlayScene
    {
        public bool IsMulti => multi != null;
        public MultiHudView MultiHud { get; private set; }
        // The play left for the room before it started (the room went back, or the player left).
        public bool MultiAborted { get; private set; }
        // The play is loaded and waiting for the room's start signal.
        public bool IsWaitingForMulti { get; private set; }

        const double StatusSeconds = 0.2, LeaveConfirmSeconds = 3;
        MultiMatch multi;
        double statusSentAt = double.NegativeInfinity, leaveAskedAt = double.NegativeInfinity;
        PlayerStatus lastSent;
        bool finishSent;

        // Before the lanes are made.
        void TakeMulti()
        {
            if (secondLane != null || IsPractice || !switcher.PlaysMulti) return;
            multi = switcher.Multi;
            autoPlay = false;
            judgeWindows = null;
        }

        // Once the lanes exist: the scoreboard, and no way to pause or restart.
        void AddMultiHud()
        {
            var viewport = PlayfieldMask.FindViewport(gameObject.scene);
            if (viewport == null) return;
            MultiHud = new MultiHudView(viewport);
            ShowMultiPlayers();
            MultiSession.RoomChanged += OnMultiRoomChanged;
            if (pauseButton != null) pauseButton.gameObject.SetActive(false);
        }

        void OnMultiRoomChanged(Room room) => ShowMultiPlayers();

        void ShowMultiPlayers()
        {
            var room = MultiSession.Room;
            if (MultiHud == null || room == null || room.Round != multi.Round) return;
            // The players of the round; before the round is under way, everyone present.
            var players = room.Members.Where(m => m.Playing).ToList();
            if (players.Count > 0) MultiHud.SetPlayers(players, MultiSession.MyUserId);
        }

        // The chart could not be read: this player sits the round out.
        void FailMulti(string code)
        {
            MultiSession.Loaded(false, code);
            MultiAborted = true;
            StartCoroutine(ReturnToRoom());
        }

        IEnumerator ReturnToRoom()
        {
            while (switcher.IsSwitching) yield return null;
            switcher.SwitchScene(SceneSwitcher.MultiRoomScene);
        }

        // The play is loaded: tell the room and wait until every player is. The room starts the
        // round for everyone at once; a room that is gone, back in its lobby, or that has left
        // this player out sends the player back to the room screen.
        IEnumerator WaitForMultiStart()
        {
            IsWaitingForMulti = true;
            MultiSession.Loaded(true);
            if (MultiHud != null) MultiHud.SetWaiting(true);
            while (true)
            {
                var room = MultiSession.Room;
                var me = MultiSession.Me;
                bool mine = room != null && room.Round == multi.Round && me != null && me.Playing;
                if (mine && (MultiSession.StartedRound == multi.Round || room.Phase == Phase.Playing)) break;
                if (!mine || room.Phase != Phase.Loading || MultiLeaveAsked())
                {
                    MultiAborted = true;
                    IsWaitingForMulti = false;
                    yield return ReturnToRoom();
                    yield break;
                }
                yield return null;
            }
            IsWaitingForMulti = false;
            if (MultiHud != null) MultiHud.SetWaiting(false);
        }

        // Back twice within a few seconds leaves the room; the first press says so.
        bool MultiLeaveAsked()
        {
            if (!InputManager.GetKeyDown(InputKey.Back)) return false;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now - leaveAskedAt <= LeaveConfirmSeconds)
            {
                _ = MultiSession.LeaveAsync();
                return true;
            }
            leaveAskedAt = now;
            MultiDirector.Ensure().Toast(MultiText.Get("play.leaveConfirm"));
            return false;
        }

        // Every frame of a round: Back, the score to the room, the scoreboard.
        void UpdateMulti()
        {
            if (MultiAborted || Session == null) return;
            if (!IsWaitingForMulti && !IsFinished && songClock.Started && !switcher.IsInputBlocked && MultiLeaveAsked())
            {
                MultiAborted = true;
                LeavePlay(() => switcher.SwitchScene(SceneSwitcher.MultiRoomScene));
                return;
            }
            var status = MultiStatus();
            double now = Time.realtimeSinceStartupAsDouble;
            if (songClock.Started && !finishSent && now - statusSentAt >= StatusSeconds && !Same(status, lastSent))
            {
                statusSentAt = now;
                lastSent = status;
                MultiSession.SendStatus(status);
            }
            if (MultiHud != null) MultiHud.Show(MultiSession.Statuses, MultiSession.MyUserId, status, Session.IsClear);
        }

        PlayerStatus MultiStatus() => new PlayerStatus
        {
            Score = Session.Score, Combo = Session.Combo, MaxCombo = Session.MaxCombo,
            Good = Session.Good, Ok = Session.Ok, Bad = Session.Bad, Drumroll = Session.Rolls,
            Gauge = (int)Math.Round(Math.Max(0, Math.Min(SoulGauge.MaximumPoints, Session.GaugePoints))),
            MusicTimeMs = (int)Math.Max(0, Math.Min(int.MaxValue, ChartTime * 1000)),
        };

        static bool Same(PlayerStatus a, PlayerStatus b)
            => b != null && a.Score == b.Score && a.Combo == b.Combo && a.Good == b.Good && a.Ok == b.Ok && a.Bad == b.Bad
               && a.Drumroll == b.Drumroll && a.Gauge == b.Gauge;

        // The play's end: the final score goes to the room, which ranks the round.
        void FinishMulti()
        {
            if (finishSent) return;
            finishSent = true;
            MultiSession.Finish(MultiStatus(), Session.IsClear);
        }

        void ReleaseMulti()
        {
            if (IsMulti) MultiSession.RoomChanged -= OnMultiRoomChanged;
        }
    }
}
