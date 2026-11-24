using System;
using System.Collections;
using System.Linq;
using OurTaiko.Multi;
using OurTaiko.Multi.Proto;
using UnityEngine;

namespace OurTaiko
{
    // One round of マルチ演奏: the room's song on the course this player took, played alone in
    // Play.Single with the others' scores beside the lane (MultiMode). PlayRequest.MultiRound carries it.
    public sealed class MultiMatch
    {
        // The room's round, so the play knows which start signal is its own.
        public int Round;
        public string Course = "Oni";
    }

    // マルチ演奏 (PlayRequest.Multi) in Play.Single (PlayScene.ChooseMode registers it): the room's
    // song on the course this player took, with the other players' scores along the top
    // (MultiHudView). The play is an ordinary one (the player's own options, drum and offsets,
    // standard judge windows, no オート) and its score is saved and uploaded as after any play. What
    // differs: it starts when the room says every player has loaded, it cannot be paused or
    // restarted, it reports its score to the room as it goes and its result when it ends, and Back
    // and the result screen return to the room.
    public sealed class MultiMode : PlayMode
    {
        const double StatusSeconds = 0.2, LeaveConfirmSeconds = 3;
        readonly PlayScene scene;
        readonly SceneSwitcher switcher;
        readonly MultiMatch match;
        public MultiHudView Hud { get; private set; }
        // The play left for the room before it started (the room went back, or the player left).
        public bool Aborted { get; private set; }
        // The play is loaded and waiting for the room's start signal.
        public bool IsWaiting { get; private set; }

        double statusSentAt = double.NegativeInfinity, leaveAskedAt = double.NegativeInfinity;
        PlayerStatus lastSent;
        bool finishSent;

        internal MultiMode(PlayScene scene, SceneSwitcher switcher, MultiMatch match)
        {
            this.scene = scene;
            this.switcher = switcher;
            this.match = match;
        }

        internal override bool StandardRules => true;
        // A round runs for everyone: no pause and no restart. Back leaves the room (Update).
        internal override bool Pauses => false;
        internal override bool RestartKey => false;
        internal override bool Restart() => true;
        internal override bool Abandoned => Aborted;

        // Once the lanes exist: the scoreboard, and no pause button.
        internal override void Attach()
        {
            var viewport = PlayfieldMask.FindViewport(scene.gameObject.scene);
            if (viewport == null) return;
            Hud = new MultiHudView(viewport);
            ShowPlayers();
            MultiSession.RoomChanged += OnRoomChanged;
            if (scene.pauseButton != null) scene.pauseButton.gameObject.SetActive(false);
        }

        void OnRoomChanged(Room room) => ShowPlayers();

        void ShowPlayers()
        {
            var room = MultiSession.Room;
            if (Hud == null || room == null || room.Round != match.Round) return;
            // The players of the round; before the round is under way, everyone present.
            var players = room.Members.Where(m => m.Playing).ToList();
            if (players.Count > 0) Hud.SetPlayers(players, MultiSession.MyUserId);
        }

        // The chart could not be read: this player sits the round out, back in the room.
        internal override void LoadFailed()
        {
            scene.resultPanel.SetActive(false);
            MultiSession.Loaded(false, "CHART_LOAD_FAILED");
            Aborted = true;
            scene.StartCoroutine(ReturnToRoom());
        }

        IEnumerator ReturnToRoom()
        {
            while (switcher.IsSwitching) yield return null;
            switcher.SwitchScene(SceneSwitcher.MultiRoomScene);
        }

        // The play is loaded: tell the room and wait until every player is. The room starts the
        // round for everyone at once; a room that is gone, back in its lobby, or that has left
        // this player out sends the player back to the room screen.
        internal override IEnumerator Ready()
        {
            IsWaiting = true;
            MultiSession.Loaded(true);
            if (Hud != null) Hud.SetWaiting(true);
            while (true)
            {
                var room = MultiSession.Room;
                var me = MultiSession.Me;
                bool mine = room != null && room.Round == match.Round && me != null && me.Playing;
                if (mine && (MultiSession.StartedRound == match.Round || room.Phase == Phase.Playing)) break;
                if (!mine || room.Phase != Phase.Loading || LeaveAsked())
                {
                    Aborted = true;
                    IsWaiting = false;
                    yield return ReturnToRoom();
                    yield break;
                }
                yield return null;
            }
            IsWaiting = false;
            if (Hud != null) Hud.SetWaiting(false);
        }

        // Back twice within a few seconds leaves the room; the first press says so.
        bool LeaveAsked()
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
        internal override void Update()
        {
            var session = scene.Session;
            if (Aborted || session == null) return;
            if (!IsWaiting && !scene.IsFinished && scene.Clock.Started && !switcher.IsInputBlocked && LeaveAsked())
            {
                Aborted = true;
                scene.LeavePlay(() => switcher.SwitchScene(SceneSwitcher.MultiRoomScene));
                return;
            }
            var status = Status(session);
            double now = Time.realtimeSinceStartupAsDouble;
            if (scene.Clock.Started && !finishSent && now - statusSentAt >= StatusSeconds && !Same(status, lastSent))
            {
                statusSentAt = now;
                lastSent = status;
                MultiSession.SendStatus(status);
            }
            if (Hud != null) Hud.Show(MultiSession.Statuses, MultiSession.MyUserId, status, session.IsClear);
        }

        PlayerStatus Status(PlaySession session) => new PlayerStatus
        {
            Score = session.Score, Combo = session.Combo, MaxCombo = session.MaxCombo,
            Good = session.Good, Ok = session.Ok, Bad = session.Bad, Drumroll = session.Rolls,
            Gauge = (int)Math.Round(Math.Max(0, Math.Min(SoulGauge.MaximumPoints, session.GaugePoints))),
            MusicTimeMs = (int)Math.Max(0, Math.Min(int.MaxValue, scene.ChartTimeNow * 1000)),
        };

        static bool Same(PlayerStatus a, PlayerStatus b)
            => b != null && a.Score == b.Score && a.Combo == b.Combo && a.Good == b.Good && a.Ok == b.Ok && a.Bad == b.Bad
               && a.Drumroll == b.Drumroll && a.Gauge == b.Gauge;

        // The play's end: the final score goes to the room, which ranks the round.
        internal override void Finished()
        {
            if (finishSent) return;
            finishSent = true;
            MultiSession.Finish(Status(scene.Session), scene.Session.IsClear);
        }

        // A room's round goes back to the room.
        internal override bool Back()
        {
            scene.LeavePlay(() => SceneSwitcher.EnsureInstance().SwitchScene(SceneSwitcher.MultiRoomScene));
            return true;
        }

        internal override void Release() => MultiSession.RoomChanged -= OnRoomChanged;
    }
}
