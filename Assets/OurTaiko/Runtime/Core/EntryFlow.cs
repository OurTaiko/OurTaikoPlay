using System;

namespace OurTaiko
{
    // scenes/entry.cpp with Nijiiro's entry_credit_arcade: the credit screen waits for a drum face
    // hit (the player joins), the join animations play, then the mode-select board takes the decide.
    // Single player: only 1P joins. Times are real-clock milliseconds.
    public sealed class EntryFlow
    {
        public enum Phase { SelectSide, SelectMode }

        // entry animation 0 (side select fade, lock_input): 20 ms delay + 100 ms.
        public const double SideInputLockMs = 120;
        // Mode select waits for the join cloud (animation 7: delay = 4's delay 550 + 4's duration
        // 350, then 333 ms of frames); the Don and cloud themselves are not drawn here.
        public const double CloudGateMs = 550 + 350 + 333;
        public const double NameplateFadeMs = 200;  // animation 12
        public const double BoardFadeMs = 160;      // animation 9, the decide fade-out

        public Phase State { get; private set; } = Phase.SelectSide;
        public double StartedAt { get; }
        public double? JoinedAt { get; private set; }
        public double? SelectedAt { get; private set; }

        public EntryFlow(double nowMs) { StartedAt = nowMs; }

        public bool CanJoin(double nowMs) => State == Phase.SelectSide && nowMs - StartedAt >= SideInputLockMs;

        public bool Join(double nowMs)
        {
            if (!CanJoin(nowMs)) return false;
            State = Phase.SelectMode;
            JoinedAt = nowMs;
            return true;
        }

        // mode_select_ready: the cloud texture change has finished.
        public bool IsModeReady(double nowMs) => JoinedAt.HasValue && nowMs - JoinedAt.Value > CloudGateMs;
        public bool IsSelected => SelectedAt.HasValue;

        // BoxManager::select_box. Input waits for the board; the timer may pick it as soon as it is up.
        public bool Select(double nowMs)
        {
            if (!IsModeReady(nowMs) || SelectedAt.HasValue) return false;
            SelectedAt = nowMs;
            return true;
        }

        public float BoardFade(double nowMs) => SelectedAt.HasValue ? 1 - Clamp01((nowMs - SelectedAt.Value) / BoardFadeMs) : 1;
        public bool IsFinished(double nowMs) => SelectedAt.HasValue && nowMs - SelectedAt.Value >= BoardFadeMs;
        public float NameplateAlpha(double nowMs) => JoinedAt.HasValue ? Clamp01((nowMs - JoinedAt.Value) / NameplateFadeMs) : 0;

        static float Clamp01(double t) => (float)(t < 0 ? 0 : t > 1 ? 1 : t);
    }
}
