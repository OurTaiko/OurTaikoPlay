using System.Linq;
using UnityEngine;

namespace OurTaiko
{
    // ゴーストバトル (SceneSwitcher.Ghost) in Play.Double (PlayScene.ChooseMode registers it): the player
    // in the lane of their side and the recorded play striking the other one, as a replay does
    // (ReplayFeed). Both lanes read the recording's block, rebuilt with its options (speed, あべこべ,
    // ドロン, the notes ランダム flipped), so they play the same notes; the player keeps only their own
    // offsets and hit sound. Standard judge windows apply to both, as only those are ever recorded.
    // The recording is judged as it was played, without a judgment offset, and its drum is silent.
    // Back returns to the ghost list, and each lane's result is shown in its lane's place.
    public sealed class GhostMode : PlayMode
    {
        readonly PlayScene scene;
        readonly SceneSwitcher switcher;
        public GhostMatch Match { get; }
        // The recording's lane; Lane1 is the player's.
        public PlayerLane Lane => scene.Lanes.Count > 1 ? scene.Lanes[1] : null;

        internal GhostMode(PlayScene scene, SceneSwitcher switcher)
        {
            this.scene = scene;
            this.switcher = switcher;
            Match = switcher.Ghost;
        }

        internal override bool StandardRules => true;
        // Switching to practice would drop the recording.
        internal override bool OffersModeSwitch => false;

        // The player (seat 0) sits on their side, the recording on the other.
        internal override LaneViews Views(int seat, LaneViews own)
            => (seat == 0 ? Match.PlayerLane : Match.GhostLane) == 0 ? scene.firstLane : scene.secondLane;

        internal override PlayOptions Options(int seat, int side) => new PlayOptions
        {
            speed = Match.Record.Speed, inverse = Match.Record.Inverse, display = Match.Record.Display,
            random = (RandomMode)Match.Record.Random, neiro = seat == 0 ? PlayOptions.ForSeat(side).neiro : PlayOptions.Mute,
        };

        // The song loading scene prepares both lanes' charts behind its curtain; a restart parses here.
        internal override TaikoChart Chart(SongDefinition song, string course, string side, TaikoChart prepared)
            => prepared ?? PrepareChart(song, Match);

        internal override ReplayFeed ReplayOf(int seat) => seat == 1 ? new ReplayFeed(Match.Record) : null;

        // The recording's block with its options, one for each lane.
        public static TaikoChart PrepareChart(SongDefinition song, GhostMatch ghost)
        {
            var chart = song.ParseBlock(ghost.Course, ghost.Side);
            ChartModifiers.Apply(chart, ghost.Record);
            return chart;
        }

        // Each lane's plate: the player's own on their lane, the recording's player on the other.
        internal override void Attach()
        {
            var controller = PlayerInfoController.EnsureInstance();
            var own = switcher.Lead == Side.P1 ? controller.Info : controller.SecondInfo;
            var rival = new PlayerInfo { name = string.IsNullOrEmpty(Match.Nickname) ? L10n.Get("Play", "ghost.defaultName") : Match.Nickname };
            foreach (var plate in Object.FindObjectsByType<NameplateView>(FindObjectsInactive.Include))
                if (plate.gameObject.scene == scene.gameObject.scene) plate.Pin(plate.seat == Match.PlayerLane ? own : rival, plate.seat);
        }

        // Each lane's result in its lane's place, above (1P's) first; only the player's play is kept
        // (ResultScene).
        internal override bool ShowResult()
        {
            var above = scene.Lanes.First(lane => lane.Views == scene.firstLane);
            var below = scene.Lanes.First(lane => lane != above);
            switcher.ShowResult(above.Result, scene.Song, above == scene.Lane1 ? scene.Record : null,
                below.Result, below == scene.Lane1 ? scene.Record : null);
            return true;
        }

        // A ghost battle goes back to its list.
        internal override bool Back()
        {
            scene.LeavePlay(() => SceneSwitcher.EnsureInstance().ReturnToMenu());
            return true;
        }
    }
}
