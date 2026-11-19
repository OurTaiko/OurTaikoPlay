using System;
using System.Collections.Generic;
using UnityEngine;

namespace OurTaiko
{
    // Layers drawn over whatever scene is open that take the keys while they are up, such as the
    // rewards (RewardOverlay). A layer registers once with what says it is up; scenes learn of
    // them through SceneSwitcher.IsInputBlocked and never know which layer it is.
    public static class ModalLayers
    {
        static readonly List<Func<bool>> layers = new List<Func<bool>>();
        static int releasedFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { layers.Clear(); releasedFrame = -1; }

        public static void Register(Func<bool> isUp) { if (!layers.Contains(isUp)) layers.Add(isUp); }
        public static void Unregister(Func<bool> isUp) => layers.Remove(isUp);

        // A layer closed by a key press this frame: the same press must not reach the scene.
        public static void Released() => releasedFrame = Time.frameCount;

        public static bool BlocksInput
        {
            get
            {
                if (Time.frameCount <= releasedFrame) return true;
                foreach (var isUp in layers)
                    if (isUp()) return true;
                return false;
            }
        }
    }
}
