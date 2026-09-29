using UnityEngine;
using UnityEngine.EventSystems;

namespace OurTaiko
{
    public sealed class DrumPad : MonoBehaviour, IPointerDownHandler
    {
        public PlayScene controller;
        public bool ka, right;
        public void OnPointerDown(PointerEventData eventData) => controller.Hit(ka, right);
    }
}
