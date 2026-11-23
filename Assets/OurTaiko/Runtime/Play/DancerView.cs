using OurTaiko.Chara;
using UnityEngine;

namespace OurTaiko
{
    // One of the backdrop's dancers. Its "rig" clip is its whole timeline at DancerTroupe.BeatFrames
    // frames per second, drawn where it stands on the stage: the way in, the looped dance and the
    // way out. The troupe picks the frame; this only draws it.
    [RequireComponent(typeof(OtanimView))]
    public sealed class DancerView : MonoBehaviour
    {
        public const string RigClip = "rig";

        public int Frame { get; private set; } = int.MinValue;
        // How many frames the rig has; 0 without a file.
        public int Frames => View.File?.Find(RigClip)?.Frames.Length ?? 0;
        OtanimView view;

        public OtanimView View => view != null ? view : view = GetComponent<OtanimView>();

        // This dancer's file; null takes it off the stage.
        public void Use(OtanimAsset file)
        {
            View.Use(file, RigClip);
            Frame = int.MinValue;
            Show(-1);
        }

        // A rig frame, or -1 while off stage.
        public void Show(int frame)
        {
            if (frame == Frame) return;
            Frame = frame;
            // Mid-frame, clear of float rounding. The rig's frames are pictures, each on a part of
            // its own, so none is eased into the next.
            View.Show(frame < 0 ? null : View.Clip, (frame + 0.5) / DancerTroupe.BeatFrames);
        }
    }
}
