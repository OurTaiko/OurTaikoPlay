using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko.Chara
{
    // An animated puchichara (.otanim) on a canvas: the clip that suits what its Don is doing,
    // drawn fitted into this rect. DonView makes one beside the Don when the puchichara is an
    // .otanim instead of a two-frame picture.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PuchiAnimGraphic : MaskableGraphic
    {
        static Shader shader;
        Material ownMaterial;
        Otanim anim;
        Texture2D texture;
        Otanim.Clip clip;
        double clipStartMs, nowMs;
        readonly List<UIVertex> vertices = new List<UIVertex>();
        readonly List<int> indices = new List<int>();

        public bool Mirrored { get; set; }
        public Otanim Anim => anim;
        public string ClipName => clip?.Name;

        public override Texture mainTexture => texture != null ? texture : s_WhiteTexture;

        // Which clips a Don's motion asks for, best first; a puchichara without any of them plays
        // "idle", or its first clip. The names are part of the format's contract (Otanim.md).
        public static string[] ClipsFor(DonAnim anim)
        {
            switch ((DonAnim)((int)anim % (int)DonAnim.MirrorOffset))
            {
                case DonAnim.Sabi:
                case DonAnim.SabiStart:
                    return new[] { "gogo" };
                case DonAnim.Combo:
                    return new[] { "combo" };
                case DonAnim.FullGage:
                    return new[] { "fullgauge", "combo" };
                case DonAnim.FullCombo:
                case DonAnim.BalloonSuccess:
                    return new[] { "happy" };
                case DonAnim.Miss:
                case DonAnim.Miss6:
                case DonAnim.MissNormal:
                case DonAnim.BalloonFailure:
                    return new[] { "miss" };
                case DonAnim.NormUp:
                case DonAnim.NormLoop:
                    return new[] { "clear" };
                case DonAnim.BalloonLoop:
                case DonAnim.BalloonNoBeat:
                    return new[] { "balloon" };
                default:
                    return System.Array.Empty<string>();
            }
        }

        public Otanim.Clip PickClip(DonAnim donAnim)
        {
            if (anim == null) return null;
            foreach (var name in ClipsFor(donAnim))
            {
                var c = anim.Find(name);
                if (c != null) return c;
            }
            return anim.Find("idle") ?? anim.Clips[0];
        }

        public void Show(Otanim file, Texture2D picture)
        {
            if (anim == file && texture == picture) return;
            anim = file;
            texture = picture;
            clip = null;
            if (shader == null) shader = Resources.Load<Shader>(DonLibrary.Root + "DonPuchiAnim");
            if (ownMaterial == null && shader != null) material = ownMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            if (ownMaterial != null) ownMaterial.SetFloat("_Premultiplied", file != null && file.Premultiplied ? 1 : 0);
            SetMaterialDirty();
            SetVerticesDirty();
        }

        public void SetTint(Color tint, float amount)
        {
            if (ownMaterial == null) return;
            ownMaterial.SetColor("_TintColor", tint);
            ownMaterial.SetFloat("_TintAmount", amount);
        }

        // Moves the animation to `ms` (the Don's clock) playing what `donAnim` asks for: a clip that
        // changes starts from its beginning, a one-shot then holds its last frame.
        public void Advance(DonAnim donAnim, double ms)
        {
            var wanted = PickClip(donAnim);
            if (wanted != clip)
            {
                clip = wanted;
                clipStartMs = ms;
            }
            nowMs = ms;
            SetVerticesDirty();
        }

        public double ClipSeconds => (nowMs - clipStartMs) / 1000.0;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (anim == null || clip == null) return;
            vertices.Clear();
            indices.Clear();
            anim.Sample(clip, ClipSeconds, rectTransform.rect, Mirrored, color, vertices, indices);
            vh.AddUIVertexStream(vertices, indices);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (ownMaterial != null) { if (Application.isPlaying) Destroy(ownMaterial); else DestroyImmediate(ownMaterial); }
        }
    }
}
