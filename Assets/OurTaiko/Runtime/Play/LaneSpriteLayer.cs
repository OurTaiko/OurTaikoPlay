using UnityEngine;
using UnityEngine.Rendering;

namespace OurTaiko
{
    // Each pass shares one clipped material. Clipping follows the original RectMask2D's
    // rectangle after Canvas layout, including window resizing and offscreen test captures.
    [DisallowMultipleComponent, RequireComponent(typeof(SortingGroup))]
    public sealed class LaneSpriteLayer : MonoBehaviour
    {
        public RectTransform clipRect;
        public Material sourceMaterial;
        Material material;
        Sprite whiteSprite;
        readonly Vector3[] corners = new Vector3[4];
        Vector4 previousClip;
        static readonly int ClipId = Shader.PropertyToID("_ClipRect");

        public Material Material
        {
            get
            {
                if (material == null) material = new Material(sourceMaterial) { name = name + " Clip" };
                return material;
            }
        }
        public Sprite WhiteSprite
        {
            get
            {
                if (whiteSprite == null)
                    whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, Texture2D.whiteTexture.width,
                        Texture2D.whiteTexture.height), new Vector2(.5f, .5f), 1, 0, SpriteMeshType.FullRect);
                return whiteSprite;
            }
        }

        void OnEnable() => Canvas.willRenderCanvases += UpdateClip;
        void OnDisable() => Canvas.willRenderCanvases -= UpdateClip;
        void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (whiteSprite != null) Destroy(whiteSprite);
        }
        void UpdateClip()
        {
            if (material == null || clipRect == null) return;
            clipRect.GetWorldCorners(corners);
            var value = new Vector4(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
            if (value == previousClip) return;
            material.SetVector(ClipId, value);
            previousClip = value;
        }
    }
}
