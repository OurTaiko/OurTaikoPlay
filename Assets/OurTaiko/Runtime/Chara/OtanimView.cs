using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace OurTaiko.Chara
{
    // Shows an imported .otanim on a canvas, fitted into this rect: whoever owns it (the stage's
    // bands, a dancer) says which clip and how far into it. Parts blended as a screen need a blend
    // of their own, so the frame is drawn as runs of parts, each run by one child graphic, in order.
    [RequireComponent(typeof(RectTransform))]
    public sealed class OtanimView : MonoBehaviour
    {
        public OtanimAsset asset;
        [Tooltip("The clip Show(seconds) plays.")]
        public string clip;

        static Shader shader;
        Material normal, screen;
        Otanim.Clip shown;
        readonly List<Run> runs = new List<Run>();
        readonly List<UIVertex> vertices = new List<UIVertex>();
        readonly List<int> indices = new List<int>();

        // A child graphic drawing one run of the frame's parts.
        sealed class Run : MaskableGraphic
        {
            public Texture picture;
            public List<UIVertex> vertices = new List<UIVertex>();
            public List<int> indices = new List<int>();

            public override Texture mainTexture => picture != null ? picture : s_WhiteTexture;

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                vh.AddUIVertexStream(vertices, indices);
            }
        }

        public Otanim File => asset != null ? asset.File : null;
        public Otanim.Clip Clip => shown ??= File?.Find(clip);
        // How many graphics the last frame took: one unless it mixes screen parts with others.
        public int RunCount { get; private set; }

        // Another file, and the clip of it Show(seconds) plays.
        public void Use(OtanimAsset file, string clipName)
        {
            asset = file;
            clip = clipName;
            shown = null;
        }

        public void Show(double seconds, float alpha = 1, float shade = 1) => Show(Clip, seconds, alpha, shade);

        // Draws `which` at `seconds`, as see-through as `alpha` says and as dark as `shade` (1 is its
        // own colours, 0 black); null, or an alpha of 0, draws nothing.
        public void Show(Otanim.Clip which, double seconds, float alpha = 1, float shade = 1)
        {
            var file = File;
            int used = 0;
            if (file != null && which != null && alpha > 0)
            {
                shade = Mathf.Clamp01(shade);
                var tint = new Color(shade, shade, shade, Mathf.Clamp01(alpha));
                var draws = Otanim.DrawsAt(which, seconds);
                var box = ((RectTransform)transform).rect;
                for (int first = 0; first < draws.Length;)
                {
                    bool asScreen = file.Parts[draws[first].Part].Blend == Otanim.Blend.Screen;
                    int end = first + 1;
                    while (end < draws.Length && file.Parts[draws[end].Part].Blend == Otanim.Blend.Screen == asScreen) end++;
                    var run = RunAt(used++);
                    run.vertices.Clear();
                    run.indices.Clear();
                    file.Sample(which, seconds, box, false, tint, run.vertices, run.indices, first, end - first);
                    run.material = MaterialFor(asScreen, file);
                    run.picture = asset.texture;
                    run.SetVerticesDirty();
                    run.SetMaterialDirty();
                    first = end;
                }
            }
            RunCount = used;
            for (int i = 0; i < runs.Count; i++)
                if (runs[i].gameObject.activeSelf != i < used) runs[i].gameObject.SetActive(i < used);
        }

        Run RunAt(int index)
        {
            while (runs.Count <= index)
            {
                var child = new GameObject("Run" + runs.Count, typeof(RectTransform), typeof(CanvasRenderer)) { hideFlags = HideFlags.DontSave };
                var rect = (RectTransform)child.transform;
                rect.SetParent(transform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                var run = child.AddComponent<Run>();
                run.raycastTarget = false;
                runs.Add(run);
            }
            return runs[index];
        }

        Material MaterialFor(bool asScreen, Otanim file)
        {
            ref var material = ref asScreen ? ref screen : ref normal;
            if (material == null)
            {
                if (shader == null) shader = Resources.Load<Shader>(DonLibrary.Root + "Otanim");
                material = new Material(shader) { hideFlags = HideFlags.DontSave };
            }
            // Every time: Use may have brought a file that keeps its picture the other way.
            material.SetFloat("_Premultiplied", file.Premultiplied ? 1 : 0);
            // A screen leaves what is behind it by one minus its own colour, channel by channel.
            material.SetFloat("_DstBlend", (float)(asScreen ? BlendMode.OneMinusSrcColor : BlendMode.OneMinusSrcAlpha));
            return material;
        }

        void OnDestroy()
        {
            foreach (var material in new[] { normal, screen })
                if (material != null) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
            foreach (var run in runs)
                if (run != null) { if (Application.isPlaying) Destroy(run.gameObject); else DestroyImmediate(run.gameObject); }
        }
    }
}
