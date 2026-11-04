using UnityEngine;

namespace OurTaiko
{
    // One player's lane and the HUD that belongs to it (Player::draw and draw_overlays). It sits on
    // the lane's NoteLane object: 1P's is PlaySceneBase.firstLane, and the two-player scene adds a
    // second one on NoteLane2P (PlayScene.secondLane).
    public class LaneViews : MonoBehaviour
    {
        // Player::draw: lane/lane_difficulty frame = difficulty (Easy..Ura).
        public UnityEngine.UI.Image laneDifficulty;
        public ModifierBadgeView modifierBadges;
        public RectTransform noteLayer, barLayer, mojiLayer;
        public BalloonCounterView balloonCounter;
        public DrumrollCounterView drumrollCounter;
        public UnityEngine.UI.Image judgment;
        public HitFaceView hitFace;
        public HitRingView hitRing;
        public SoulGaugeView soulGauge;
        public NoteArcView noteArcs;
        [Tooltip("drum_don_l/r, drum_kat_l/r; each plays DrumFlash.anim from its own hit.")]
        public UnityEngine.UI.Image[] drumFlashes;
        public ScoreCounterView scoreCounter;
        public ComboView combo;
        public ComboAnnounceView comboAnnounce;
        public JudgeCounterView judgeCounter;
        public TimingFeedbackView timingFeedback;
        public BranchLaneView branchLane;
        public GogoView gogo;
        public EndingView ending;
    }
}
