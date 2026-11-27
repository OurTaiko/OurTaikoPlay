using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // A rectangle shaded top to bottom by a Gradient, drawn as one mesh with no texture: one band per
    // pair of neighbouring keys, each vertex coloured from the gradient (and tinted by `color`), so it
    // stays sharp at any size. The practice progress bar's fill is one (after YataiDON's progress_bar).
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class VerticalGradientGraphic : MaskableGraphic
    {
        [Tooltip("Time 0 is the top edge, 1 the bottom; the colour between keys is blended linearly.")]
        public Gradient gradient = new Gradient();

        protected override void OnPopulateMesh(VertexHelper mesh) => Fill(mesh, rectTransform.rect, gradient, color);

        // The bands for rect: an edge at each gradient key (and at the rect's top and bottom).
        public static void Fill(VertexHelper mesh, Rect rect, Gradient gradient, Color tint)
        {
            mesh.Clear();
            var times = new System.Collections.Generic.SortedSet<float> { 0, 1 };
            foreach (var key in gradient.colorKeys) times.Add(Mathf.Clamp01(key.time));
            foreach (var key in gradient.alphaKeys) times.Add(Mathf.Clamp01(key.time));
            float? above = null;
            foreach (float t in times)
            {
                float y = rect.yMax - t * rect.height;
                Color32 shade = gradient.Evaluate(t) * tint;
                int i = mesh.currentVertCount;
                mesh.AddVert(new Vector3(rect.xMin, y), shade, Vector2.zero);
                mesh.AddVert(new Vector3(rect.xMax, y), shade, Vector2.zero);
                if (above.HasValue) { mesh.AddTriangle(i - 2, i - 1, i + 1); mesh.AddTriangle(i - 2, i + 1, i); }
                above = y;
            }
        }
    }
}
