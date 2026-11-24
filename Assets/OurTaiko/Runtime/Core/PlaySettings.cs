using System;

namespace OurTaiko
{
    // When the early/late offset shows during play. 不可 never shows one, pressed or missed.
    public enum TimingDisplay { GoodAndOk, Ok, Off }

    public static class TimingDisplays
    {
        public static readonly string[] Names = { "GOOD + OK", "OK", "Off" };
        public static bool Shows(this TimingDisplay display, Judgment judgment) => display switch
        {
            TimingDisplay.GoodAndOk => judgment == Judgment.Good || judgment == Judgment.Ok,
            TimingDisplay.Ok => judgment == Judgment.Ok,
            _ => false,
        };
    }

    // settings.json's play section (GameSettings.play): what the play itself reads.
    [Serializable]
    public sealed class PlaySettings
    {
        // The touch drum in Play.Single: enabled and visible, or disabled and hidden. Only mobile
        // players show it by default; an explicit saved choice wins on every platform. The Editor
        // compiles with the active build target's defines, so it is excluded and always starts off.
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        public bool singlePlayerDrumPad = true;
#else
        public bool singlePlayerDrumPad = false;
#endif
        // Note offsets and misses when browsing a paused or finished practice attempt.
        public bool practiceTimingReview = true;
        // Which pressed judgments show their early/late offset (ms) beside the lane while playing.
        public TimingDisplay timingDisplay = TimingDisplay.GoodAndOk;
        public TimingDisplay TimingDisplay => Enum.IsDefined(typeof(TimingDisplay), timingDisplay) ? timingDisplay : TimingDisplay.GoodAndOk;
        public const int OffsetDefaultMs = 0, OffsetStepMs = 1;
        // A shifts the chart relative to music; B shifts judgment only. Positive means later.
        public int audioOffsetMs = OffsetDefaultMs;
        public int judgeOffsetMs = OffsetDefaultMs;
        public const int InputDelayDefaultMs = 0, InputDelayMaxMs = 100;
        // How long a drum press waits before it is judged, heard and shown, like the arcade. 0 is off.
        public int inputDelayMs = InputDelayDefaultMs;
        // Off: every course judges with its own standard windows. On: the 良/可/不可 half widths below
        // apply to every course in every mode (practice too), and no score is saved or uploaded.
        public bool customJudgeWindow;
        public const int JudgeWindowMinMs = 1, JudgeWindowMaxMs = 300;
        // The standard 鬼 windows (25.025, 75.075 and 108.44 ms), rounded.
        public static readonly int[] StandardJudgeMs = { 25, 75, 108 };
        public int judgeGoodMs = 25, judgeOkMs = 75, judgeBadMs = 108;
        public JudgeWindows? CustomJudgeWindows => customJudgeWindow ? JudgeWindows.FromMs(judgeGoodMs, judgeOkMs, judgeBadMs) : null;

        // Every field is a value, so a shallow copy is a full one.
        public PlaySettings Clone() => (PlaySettings)MemberwiseClone();

        // 0 良, 1 可, 2 不可. A new value moves its neighbours along so that 良 <= 可 <= 不可 holds.
        public int JudgeMs(int window) => window == 0 ? judgeGoodMs : window == 1 ? judgeOkMs : judgeBadMs;
        public void SetJudgeMs(int window, int ms)
        {
            ms = Math.Max(JudgeWindowMinMs, Math.Min(JudgeWindowMaxMs, ms));
            if (window == 0) { judgeGoodMs = ms; judgeOkMs = Math.Max(judgeOkMs, ms); judgeBadMs = Math.Max(judgeBadMs, judgeOkMs); }
            else if (window == 1) { judgeOkMs = ms; judgeGoodMs = Math.Min(judgeGoodMs, ms); judgeBadMs = Math.Max(judgeBadMs, ms); }
            else { judgeBadMs = ms; judgeOkMs = Math.Min(judgeOkMs, ms); judgeGoodMs = Math.Min(judgeGoodMs, judgeOkMs); }
        }
    }
}
