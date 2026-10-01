using UnityEngine;
using UnityEngine.EventSystems;

namespace OurTaiko
{
    public sealed class DrumPad : MonoBehaviour, IPointerDownHandler
    {
        public bool ka, right;
        // Goes through InputManager so pointer hits share the per-frame input mutex with the keyboard.
        public void OnPointerDown(PointerEventData eventData) =>
            InputManager.Press(ka ? right ? InputKey.RightKa : InputKey.LeftKa : right ? InputKey.RightDon : InputKey.LeftDon);
    }
}
