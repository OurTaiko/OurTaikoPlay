using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace OurTaiko
{
    // No Submit handler: only a deliberate pointer click or a physical Don can leave results.
    public sealed class ResultPointerControl : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Action Clicked;
        public Action<int> Swiped;
        int pointerId;
        bool pressed, suppressClick;
        Vector2 start;
        public void OnPointerDown(PointerEventData data)
        {
            if (pressed || data.button != PointerEventData.InputButton.Left) return;
            if (!Local(data, out start)) return;
            pointerId = data.pointerId; pressed = true; suppressClick = false;
        }
        bool Local(PointerEventData data, out Vector2 position) => RectTransformUtility.ScreenPointToLocalPointInRectangle(
            (RectTransform)transform, data.position, data.pressEventCamera, out position);
        public void OnPointerUp(PointerEventData data)
        {
            if (!pressed || pointerId != data.pointerId) return;
            pressed = false;
            if (!Local(data, out var end)) { suppressClick = true; return; }
            Vector2 delta = end - start;
            if (delta.magnitude > 20) suppressClick = true;
            if (Mathf.Abs(delta.x) >= 60 && Mathf.Abs(delta.x) > Mathf.Abs(delta.y) * 1.25f)
                Swiped?.Invoke(delta.x < 0 ? 1 : -1);
        }
        public void OnPointerClick(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left && data.pointerId == pointerId && !suppressClick)
                Clicked?.Invoke();
        }
        public void OnBeginDrag(PointerEventData data) { if (data.pointerId == pointerId) suppressClick = true; }
        public void OnDrag(PointerEventData data) { }
        public void OnEndDrag(PointerEventData data) { }
        void OnDisable() { pressed = false; suppressClick = true; }
    }
}
