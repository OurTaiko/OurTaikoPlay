using UnityEngine;

namespace OurTaiko
{
    public sealed class SpriteFlipbook : MonoBehaviour
    {
        public Sprite[] frames;
        public float framesPerSecond = 8;
        UnityEngine.UI.Image image;
        void Awake() => image = GetComponent<UnityEngine.UI.Image>();
        public void ShowTime(double seconds)
        {
            if (frames.Length > 0) image.sprite = frames[(int)(System.Math.Max(0, seconds) * framesPerSecond) % frames.Length];
        }
    }
}
