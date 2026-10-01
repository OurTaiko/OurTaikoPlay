using UnityEngine;

namespace OurTaiko
{
    // One drum zone (left/right don or ka). InputManager hit-tests touches and the mouse
    // against every enabled zone in its per-frame update, alongside the keyboard.
    [RequireComponent(typeof(RectTransform))]
    public sealed class DrumPad : MonoBehaviour
    {
        public bool ka, right;
        RectTransform rect;
        Canvas canvas;

        public InputKey Key => ka ? right ? InputKey.RightKa : InputKey.LeftKa : right ? InputKey.RightDon : InputKey.LeftDon;

        void OnEnable()
        {
            rect = (RectTransform)transform;
            canvas = GetComponentInParent<Canvas>();
            InputManager.RegisterPad(this);
        }
        void OnDisable() => InputManager.UnregisterPad(this);

        internal bool Contains(Vector2 screenPoint)
        {
            var root = canvas != null ? canvas.rootCanvas : null;
            var camera = root == null || root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, camera);
        }
    }
}
