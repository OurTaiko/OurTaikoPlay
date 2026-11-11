using System.Linq;
using UnityEngine;

namespace OurTaiko
{
    // ゴーストバトル (SceneSwitcher.Ghost) in DoublePlayScene: the player in the lane of their side and
    // the recorded play striking the other one, as the practice replay does (ReplayFeed). Both lanes
    // read the recording's block, rebuilt with its options (speed, あべこべ, ドロン, the notes ランダム
    // flipped), so they play the same notes; the player keeps only their own offsets and hit sound.
    // Standard judge windows apply to both, as only those are ever recorded. The recording is
    // judged as it was played, without a judgment offset, and its drum is silent.
    public sealed partial class PlayScene
    {
        public bool IsGhost => ghost != null;
        public GhostMatch Ghost => ghost;
        GhostMatch ghost;
        // The recording's lane; Lane1 is the player's.
        public PlayerLane GhostLane => IsGhost && lanes.Count > 1 ? lanes[1] : null;

        // Before the lanes are made.
        void TakeGhost()
        {
            if (secondLane == null || IsPractice || !switcher.PlaysGhost) return;
            ghost = switcher.Ghost;
            autoPlay = false;
            judgeWindows = null;
        }

        // The lane views of a seat: the player (seat 0) sits on their side, the recording on the other.
        LaneViews GhostViews(int seat) => (seat == 0 ? ghost.PlayerLane : ghost.GhostLane) == 0 ? firstLane : secondLane;

        PlayOptions GhostOptions(int seat) => new PlayOptions
        {
            speed = ghost.Record.Speed, inverse = ghost.Record.Inverse, display = ghost.Record.Display,
            random = (RandomMode)ghost.Record.Random, neiro = seat == 0 ? PlayOptions.ForSeat(switcher.Lead.Index()).neiro : PlayOptions.Mute,
        };

        // The recording's block with its options, one for each lane. The song loading scene prepares
        // both behind its curtain; a restart parses here.
        public static TaikoChart PrepareGhostChart(SongDefinition song, GhostMatch ghost)
        {
            var chart = song.ParseBlock(ghost.Course, ghost.Side);
            ChartModifiers.Apply(chart, ghost.Record);
            return chart;
        }

        // Each lane's plate: the player's own on their lane, the recording's player on the other.
        void ShowGhostPlates()
        {
            var controller = PlayerInfoController.EnsureInstance();
            var own = switcher.Lead == Side.P1 ? controller.Info : controller.SecondInfo;
            var rival = new PlayerInfo { name = string.IsNullOrEmpty(ghost.Nickname) ? "Ghost" : ghost.Nickname };
            foreach (var plate in FindObjectsByType<NameplateView>(FindObjectsInactive.Include))
                if (plate.gameObject.scene == gameObject.scene) plate.Pin(plate.seat == ghost.PlayerLane ? own : rival, plate.seat);
        }

        // Each lane's result in its lane's place: above (1P's) first.
        (PlayerLane Above, PlayerLane Below) GhostResultLanes()
        {
            var above = lanes.First(lane => lane.Views == firstLane);
            return (above, lanes.First(lane => lane != above));
        }
    }
}
