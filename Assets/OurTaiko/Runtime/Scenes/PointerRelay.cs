using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace OurTaiko
{
    public sealed class PointerRelay : MonoBehaviour, IPointerClickHandler
    {
        public Action Clicked;
        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke();
    }
}
