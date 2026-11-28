using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace OurTaiko.Chara
{
    // A 3D Don on a canvas. The Don stands at this RectTransform's pivot, in the 1920×1080 layout's
    // units (YataiDON's skin positions times 1.5 for a 1280 skin); the rect is the area it may
    // cover. It renders into a render target (supersampled, only when something changed, as
    // YataiDON caches it) that the DonImage shows through the outline and tint pass, over the part
    // of the area the Don is in; a puchichara is drawn beside it as a child image. A Don's
    // background is a second picture under that one, made once and kept (BakeBackground).
    [RequireComponent(typeof(DonImage))]
    public sealed class DonView : MonoBehaviour
    {
        public const float LayoutScale = 1.5f; // 1080 lines over YataiDON's 720
        const float Supersample = 2f;
        const int MaxTarget = 2048;
        // Multisampling in the target as well as the supersampling: the silhouette and the black
        // lines are hard-edged geometry, and 2x alone (YataiDON's) leaves them stepped.
        public const int Msaa = 4;

        // The target's MSAA, as much of `wanted` as this device renders into a texture.
        public static int SupportedMsaa(int width, int height, int wanted)
        {
            var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 24) { msaaSamples = wanted };
            return Mathf.Max(1, Mathf.Min(wanted, SystemInfo.GetRenderTextureSupportedMSAASampleCount(desc)));
        }

        // The part of a width × height area the canvas has to draw for a Don that covers `covered`
        // of it (Chara3D.Covered), as a share of the area with y up: widened by the composite's rim
        // (1.5 units at draw scale 1) and the screen pixel it averages over, then out to the next
        // of ShownSteps lines each way, so that a pose a little larger does not rebuild the canvas.
        // The composite reads the target twenty times a pixel; most of the area is empty.
        public static UnityEngine.Rect ShownPart(UnityEngine.Rect covered, float width, float height, float drawScale, float ppu)
        {
            if (covered.width <= 0 || covered.height <= 0 || width <= 0 || height <= 0) return default;
            float margin = 1.5f * drawScale + 2 * Supersample / Mathf.Max(ppu, 0.01f);
            float left = Mathf.Floor((covered.xMin - margin) / width * ShownSteps) / ShownSteps;
            float right = Mathf.Ceil((covered.xMax + margin) / width * ShownSteps) / ShownSteps;
            float bottom = Mathf.Floor((1 - (covered.yMax + margin) / height) * ShownSteps) / ShownSteps;
            float top = Mathf.Ceil((1 - (covered.yMin - margin) / height) * ShownSteps) / ShownSteps;
            left = Mathf.Clamp01(left); right = Mathf.Clamp01(right); bottom = Mathf.Clamp01(bottom); top = Mathf.Clamp01(top);
            return right <= left || top <= bottom ? default : UnityEngine.Rect.MinMaxRect(left, bottom, right, top);
        }
        const float ShownSteps = 32;

        // Makes the picture of a Don's background that the canvas shows under the Don, into `layer`
        // (made or remade here at the size the screen shows it; freed when the Don has none): the
        // background drawn alone into `scratch`, then put through the composite once, which
        // shrinks it to screen pixels and draws its rim. `scratch` is the Don's own render target,
        // whose picture is lost: the Don is drawn into it again right after (a target of its own
        // for this would be as large as the Don's, colour and depth multisampled, for one use).
        // The background does not move, so this happens when a Don is dressed, turned or resized,
        // and showing it afterwards is one texture read a pixel. `bake` is a material of the
        // composite shader kept for this; `supersample` is how many scratch pixels a screen pixel is across.
        public static void BakeBackground(Chara3D chara, CommandBuffer cmd, Material bake, RenderTexture scratch, ref RenderTexture layer,
            float supersample, float width, float height, float x, float y, float size, float outlinePx, float thickness)
        {
            if (!chara.HasBackground)
            {
                Free(ref layer);
                return;
            }
            int lw = Mathf.Max(8, Mathf.CeilToInt(scratch.width / supersample)), lh = Mathf.Max(8, Mathf.CeilToInt(scratch.height / supersample));
            if (layer == null || layer.width != lw || layer.height != lh)
            {
                Free(ref layer);
                layer = new RenderTexture(lw, lh, 0, RenderTextureFormat.ARGB32)
                {
                    name = "Don background", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave,
                };
                layer.Create();
            }
            cmd.Clear();
            chara.RenderBackground(cmd, scratch, width, height, x, y, size, outlinePx);
            cmd.SetRenderTarget(layer, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.ClearRenderTarget(false, true, Color.clear);
            Graphics.ExecuteCommandBuffer(cmd);
            bake.SetFloat("_Thickness", thickness);
            bake.SetFloat("_TintAmount", 0);
            var active = RenderTexture.active;
            Graphics.Blit(scratch, layer, bake);
            RenderTexture.active = active;
        }

        static void Free(ref RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture);
            texture = null;
        }

        [Tooltip("Draw size (YataiDON's draw scale_mul).")]
        public float drawScale = 1f;

        public Chara3D Chara { get; private set; }
        // The clock the motions run on, in milliseconds (real time unless a scene sets one).
        public Func<double> Clock;

        DonImage image;
        RawImage puchi;
        PuchiAnimGraphic puchiAnim;
        Material composite, bake, puchiMaterial;
        RenderTexture target, backLayer;
        CommandBuffer cmd;
        Vector2 renderedSize;
        float renderedScale, renderedPpu;
        readonly Vector3[] corners = new Vector3[4];

        static Shader compositeShader, puchiShader;

        RectTransform Rect => (RectTransform)transform;

        // A Don standing at (x, y) of `parent` in YataiDON's layout terms (from the parent's top left,
        // y down, 1920×1080 units), free to cover width × height around it (pivotY of the area is
        // below its feet). sortingOrder puts it on a canvas of its own among the scene's.
        public static DonView Create(Transform parent, string name, float x, float y, float width = 640, float height = 640,
            float pivotY = 0.2f, int? sortingOrder = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(DonImage));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0.5f, pivotY);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
            if (sortingOrder.HasValue)
            {
                var canvas = go.AddComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = sortingOrder.Value;
            }
            return go.AddComponent<DonView>();
        }

        // Moves the Don to (x, y) of its parent, as Create places it.
        public void MoveTo(float x, float y) => Rect.anchoredPosition = new Vector2(x, -y);

        // The order of the Don's own canvas (Create's sortingOrder), for a Don whose place among the
        // scene's layers changes as it moves.
        public int SortingOrder
        {
            get => GetComponent<Canvas>().sortingOrder;
            set => GetComponent<Canvas>().sortingOrder = value;
        }

        // Fades the Don and its puchichara (a seat's plate fading in, a screen fading out).
        public float Alpha
        {
            get => alpha;
            set
            {
                alpha = Mathf.Clamp01(value);
                if (image != null) image.color = new Color(1, 1, 1, alpha);
                if (puchi != null) puchi.color = new Color(1, 1, 1, alpha);
                if (puchiAnim != null) puchiAnim.color = new Color(1, 1, 1, alpha);
            }
        }
        float alpha = 1;

        void Awake()
        {
            image = GetComponent<DonImage>();
            image.raycastTarget = false;
            if (compositeShader == null) compositeShader = Resources.Load<Shader>(DonLibrary.Root + "DonComposite");
            if (puchiShader == null) puchiShader = Resources.Load<Shader>(DonLibrary.Root + "DonPuchi");
            composite = new Material(compositeShader) { hideFlags = HideFlags.DontSave };
            image.material = composite;
            image.enabled = false;
        }

        // Dresses the Don (a new model when the parts change); the motion starts over at DON_NORMAL.
        public Chara3D Show(DonLook look, bool mirror = false, bool useSkinPose = false)
        {
            if (image == null) Awake();
            Chara?.Dispose();
            Chara = new Chara3D(look, mirror, useSkinPose);
            shownLook = look; shownSkinPose = useSkinPose;
            renderedSize = Vector2.zero;
            return Chara;
        }

        // The seat whose look this Don wears (DonLooks): the seat's account signing in or out, or
        // its look arriving, redresses it, and turning the 3D Don off in the settings hides it.
        // -1: not a seat's.
        public int Seat
        {
            get => seat;
            set
            {
                seat = value;
                if (seat >= 0 && settings == null)
                {
                    settings = SettingManager.EnsureInstance();
                    settings.Changed += OnSettingsChanged;
                    DonDownloads.Changed += OnLookChanged;
                }
            }
        }
        int seat = -1;
        SettingManager settings;
        DonLook shownLook;
        bool shownSkinPose;

        void OnSettingsChanged(GameSettings s)
        {
            if (this != null && seat >= 0) gameObject.SetActive(s.display.chara3D);
        }

        void OnLookChanged(int changed)
        {
            if (this == null || changed != seat || Chara == null) return;
            var look = DonLooks.For(seat);
            if (look.Equals(shownLook)) return;
            var anim = Chara.Anim;
            bool mirror = Chara.Mirrored;
            Show(look, mirror, shownSkinPose).SetAnim((DonAnim)((int)anim % (int)DonAnim.MirrorOffset));
        }

        public void Hide()
        {
            Chara?.Dispose();
            Chara = null;
            Free(ref backLayer);
            if (image != null) { image.enabled = false; image.Back = default; }
            if (puchi != null) puchi.enabled = false;
            if (puchiAnim != null) puchiAnim.enabled = false;
        }

        void OnDestroy()
        {
            if (settings != null) { settings.Changed -= OnSettingsChanged; DonDownloads.Changed -= OnLookChanged; }
            Chara?.Dispose();
            Chara = null;
            Free(ref target);
            Free(ref backLayer);
            cmd?.Release();
            if (composite != null) Destroy(composite);
            if (bake != null) Destroy(bake);
            if (puchiMaterial != null) Destroy(puchiMaterial);
        }

        double Now => Clock != null ? Clock() : Chara3D.NowMs;

        void LateUpdate()
        {
            if (Chara == null) return;
            Chara.Update(Now);
            Draw();
        }

        // Screen pixels per layout unit where this rect is.
        float PixelsPerUnit()
        {
            var canvas = GetComponentInParent<Canvas>();
            canvas = canvas != null ? canvas.rootCanvas : null;
            var rect = Rect.rect;
            if (canvas == null || rect.height <= 0) return 1;
            var cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Rect.GetWorldCorners(corners);
            var a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            var b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            float ppu = Mathf.Abs(b.y - a.y) / rect.height;
            return ppu > 0 && !float.IsInfinity(ppu) ? ppu : 1;
        }

        public void Draw()
        {
            if (Chara == null) return;
            var rect = Rect.rect;
            if (rect.width <= 0 || rect.height <= 0) return;
            float ppu = PixelsPerUnit() * Supersample;
            float fit = Mathf.Min(1, MaxTarget / Mathf.Max(rect.width * ppu, rect.height * ppu));
            ppu *= fit;
            int w = Mathf.Max(16, Mathf.CeilToInt(rect.width * ppu));
            int h = Mathf.Max(16, Mathf.CeilToInt(rect.height * ppu));
            if (target == null || target.width != w || target.height != h)
            {
                Free(ref target);
                target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
                {
                    name = "Don", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                    antiAliasing = SupportedMsaa(w, h, Msaa), hideFlags = HideFlags.DontSave,
                };
                target.Create();
                image.texture = target;
                Chara.Dirty = true;
            }
            bool resized = rect.size != renderedSize || drawScale != renderedScale || !Mathf.Approximately(ppu, renderedPpu);
            if (resized) Chara.Dirty = true;
            // YataiDON's 3 texels of a target at twice the screen: 1.5 screen pixels at 1080 lines.
            float thickness = 1.5f * drawScale * ppu;
            // The background's layer is made again only when the picture it holds would differ.
            bool rebake = Chara.HasBackground ? resized || Chara.BackgroundDirty || backLayer == null : backLayer != null;
            if (Chara.Dirty || rebake)
            {
                cmd ??= new CommandBuffer { name = "Don" };
                var pivot = Rect.pivot;
                float x = pivot.x * rect.width, y = (1 - pivot.y) * rect.height;
                float size = Chara3D.Scale720 * LayoutScale * drawScale;
                float outline = 2.5f * drawScale * (1080f * ppu) / 720f;
                if (rebake)
                {
                    if (bake == null) bake = new Material(compositeShader) { hideFlags = HideFlags.DontSave };
                    BakeBackground(Chara, cmd, bake, target, ref backLayer, Supersample * fit, rect.width, rect.height, x, y, size, outline, thickness);
                    composite.SetTexture("_BackTex", backLayer);
                    image.Back = backLayer != null ? ShownPart(Chara.BackgroundCovered, rect.width, rect.height, drawScale, ppu) : default;
                }
                cmd.Clear();
                Chara.Render(cmd, target, rect.width, rect.height, x, y, size, outline);
                Graphics.ExecuteCommandBuffer(cmd);
                renderedSize = rect.size; renderedScale = drawScale; renderedPpu = ppu;
                image.Shown = ShownPart(Chara.Covered, rect.width, rect.height, drawScale, ppu);
            }
            composite.SetFloat("_Thickness", thickness);
            composite.SetColor("_TintColor", Chara.TintColor);
            composite.SetFloat("_TintAmount", Chara.TintAmount);
            image.enabled = alpha > 0;
            DrawPuchi();
        }

        // YataiDON Chara3D::draw_puchi: 150 px square, 90 px to the Don's back side and 120 up, two
        // frames 850 ms each, bobbing 6 px over two frames.
        void DrawPuchi()
        {
            var texture = Chara.PuchiTexture;
            if (texture == null || Chara.PuchiAnim != null)
            {
                if (puchi != null) puchi.enabled = false;
                DrawPuchiAnim(texture);
                return;
            }
            if (puchiAnim != null) puchiAnim.enabled = false;
            if (puchi == null)
            {
                var go = new GameObject("Puchi", typeof(RectTransform), typeof(RawImage));
                go.layer = gameObject.layer;
                go.transform.SetParent(transform, false);
                puchi = go.GetComponent<RawImage>();
                puchi.raycastTarget = false;
                if (puchiShader != null) puchi.material = puchiMaterial = new Material(puchiShader) { hideFlags = HideFlags.DontSave };
            }
            const double FrameMs = 850.0;
            float s = LayoutScale * drawScale;
            double ms = Chara.PuchiMs;
            int frame = (int)(ms / FrameMs) % 2;
            float bob = Mathf.Sin((float)(ms / (FrameMs * 2.0) * 2.0 * Math.PI)) * 6f * s;
            float size = 150f * s;
            var rt = (RectTransform)puchi.transform;
            rt.anchorMin = rt.anchorMax = Rect.pivot;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2((Chara.Mirrored ? 90f : -90f) * s, 120f * s - bob);
            puchi.texture = texture;
            puchi.color = new Color(1, 1, 1, alpha);
            puchi.uvRect = new Rect(Chara.Mirrored ? 1 : 0, frame == 0 ? 0.5f : 0f, Chara.Mirrored ? -1 : 1, 0.5f);
            if (puchiMaterial != null)
            {
                puchiMaterial.SetColor("_TintColor", Chara.TintColor);
                puchiMaterial.SetFloat("_TintAmount", Chara.TintAmount);
            }
            puchi.enabled = true;
        }

        // An animated puchichara (.otanim): its view fitted into the same 150 px square, without
        // the bob (it moves by itself), mirrored with the Don, playing the clip the Don's motion
        // asks for (PuchiAnimGraphic.ClipsFor) on the Don's clock.
        void DrawPuchiAnim(Texture2D texture)
        {
            var anim = Chara.PuchiAnim;
            if (anim == null || texture == null)
            {
                if (puchiAnim != null) puchiAnim.enabled = false;
                return;
            }
            if (puchiAnim == null)
            {
                var go = new GameObject("PuchiAnim", typeof(RectTransform), typeof(CanvasRenderer), typeof(PuchiAnimGraphic));
                go.layer = gameObject.layer;
                go.transform.SetParent(transform, false);
                puchiAnim = go.GetComponent<PuchiAnimGraphic>();
                puchiAnim.raycastTarget = false;
            }
            float s = LayoutScale * drawScale;
            float size = 150f * s;
            var rt = (RectTransform)puchiAnim.transform;
            rt.anchorMin = rt.anchorMax = Rect.pivot;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2((Chara.Mirrored ? 90f : -90f) * s, 120f * s);
            puchiAnim.Show(anim, texture);
            puchiAnim.Mirrored = Chara.Mirrored;
            puchiAnim.color = new Color(1, 1, 1, alpha);
            puchiAnim.SetTint(Chara.TintColor, Chara.TintAmount);
            puchiAnim.Advance(Chara.Anim, Chara.PuchiMs);
            puchiAnim.enabled = true;
        }
    }
}
