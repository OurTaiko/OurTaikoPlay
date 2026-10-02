using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OurTaiko.Editor
{
    // Clips for the play scene's soul gauge.
    public static partial class ProjectBuilder
    {
        [MenuItem("OurTaiko/Apply Soul Fire Clips")]
        public static void ApplySoulFireClips() => EditPlayScene(play => AttachSoulFireClips(play.soulGauge));

        // Gauge::draw while full: tamashii_fire cycles 8 frames of 50 ms, and tamashii_overlay is
        // drawn on fire frames 0, 1, 4 and 5.
        static void AttachSoulFireClips(SoulGaugeView view)
        {
            var frames = Enumerable.Range(0, 8).Select(i => Sprite("game/gauge/tamashii_fire/" + i)).ToArray();
            AttachClip(view.fire.gameObject, SaveClip("SoulFire", 20, true, clip =>
                SpriteKeys(clip, "", typeof(UnityEngine.UI.Image), frames, frames.Select((_, i) => i * 0.05f).ToArray())));
            AttachClip(view.soulOverlay.gameObject, SaveClip("SoulOverlay", 20, true, clip =>
                SteppedCurve(clip, "", typeof(UnityEngine.UI.Image), "m_Enabled", (0, 1), (0.1f, 0), (0.2f, 1), (0.3f, 0), (0.4f, 0))));
        }
    }
}
