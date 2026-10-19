using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace OurTaiko
{
    // The song wheel's swipe (WheelMomentum): dragging upwards pulls the wheel up, towards the next
    // boards. Drags that start on a board bubble up here, and once one starts the event system sends
    // the board no click. A press during a glide stops the wheel and is never a click on the board
    // under it.
    public sealed class WheelSwipe : MonoBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Tooltip("Design pixels of drag per row.")]
        public float pitch = 135;
        public Func<bool> CanSwipe;

        public WheelMomentum Momentum { get; } = new WheelMomentum();
        PointerEventData press;

        bool Allowed => CanSwipe == null || CanSwipe();

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            if (!Allowed || eventData.button != PointerEventData.InputButton.Left) return;
            if (Momentum.Gliding) eventData.eligibleForClick = false;
            Momentum.Press();
            press = eventData;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!Allowed || eventData.button != PointerEventData.InputButton.Left) return;
            Momentum.BeginDrag(GameTimeline.FrameTime);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!Momentum.Dragging) return;
            var canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1;
            Momentum.Drag(eventData.delta.y / Mathf.Max(0.0001f, scale) / pitch, GameTimeline.FrameTime);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            Momentum.EndDrag(GameTimeline.FrameTime);
            press = null;
        }

        // Advances the glide; a press that came up without dragging (the module clears its drag
        // target on release) lets the stopped wheel settle.
        public void Tick()
        {
            if (Momentum.Holding && (press == null || press.pointerDrag != gameObject)) { Momentum.Release(); press = null; }
            Momentum.Tick(GameTimeline.FrameTime);
        }

        public void Stop()
        {
            Momentum.Stop();
            press = null;
        }

        void OnDisable() => Stop();
    }
}
