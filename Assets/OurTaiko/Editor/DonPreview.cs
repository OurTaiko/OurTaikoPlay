using System;
using System.IO;
using OurTaiko.Chara;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OurTaiko.Editor
{
    // Renders 3D Dons to PNG files without a scene, through the same model and outline passes the
    // game uses (DonView). Menu OurTaiko/Render Don Previews, or batch:
    // -executeMethod OurTaiko.Editor.DonPreview.RenderBatch with DON_PREVIEW_DIR set. The costumes,
    // heads, bodies, puchichara and makeup are not in the game: DON_ASSETS_DIR names a local copy of the
    // bucket's 3dchara/ folder to read them from (without it only the plain Don is drawn).
    public static class DonPreview
    {
        public readonly struct Shot
        {
            public readonly string Name;
            public readonly DonLook Look;
            public readonly DonAnim Anim;
            public readonly float Progress;
            public readonly bool Mirror;
            public Shot(string name, DonLook look, DonAnim anim, float progress = 0.25f, bool mirror = false)
            {
                Name = name; Look = look; Anim = anim; Progress = progress; Mirror = mirror;
            }
        }

        public static Shot[] Shots()
        {
            var basic = DonLook.Default();
            var p2 = DonLook.Default(1);
            var mochi = DonLook.Default(); mochi.cos = 30;
            var mixed = DonLook.Default(); mixed.costume = false; mixed.head = 3; mixed.body = 4;
            var puchi = DonLook.Default(); puchi.puchi = 1;
            var paint = DonLook.Default(); paint.paint = 1;
            var mochiPaint = mochi; mochiPaint.paint = 2;
            return new[]
            {
                new Shot("normal", basic, DonAnim.Normal),
                new Shot("normal_2p_mirror", p2, DonAnim.Normal, 0.25f, true),
                new Shot("combo", basic, DonAnim.Combo, 0.4f),
                new Shot("sabi", basic, DonAnim.Sabi, 0.3f),
                new Shot("miss", basic, DonAnim.Miss, 0.5f),
                new Shot("select_loop", basic, DonAnim.SelectLoop, 0.2f),
                new Shot("cos30", mochi, DonAnim.Normal),
                new Shot("head3_body4", mixed, DonAnim.Normal),
                new Shot("puchi1", puchi, DonAnim.Normal),
                new Shot("paint1", paint, DonAnim.Normal),
                new Shot("cos30_paint2", mochiPaint, DonAnim.Normal),
            };
        }

        [MenuItem("OurTaiko/Render Don Previews")]
        static void RenderMenu()
        {
            string assets = EditorUtility.OpenFolderPanel("A local copy of the bucket's 3dchara/ folder (cancel: the plain Don only)", "", "");
            UseAssets(assets);
            string dir = EditorUtility.SaveFolderPanel("Don previews", "", "");
            if (!string.IsNullOrEmpty(dir)) RenderAll(dir);
        }

        public static void RenderBatch()
        {
            string dir = Environment.GetEnvironmentVariable("DON_PREVIEW_DIR");
            if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException("Set DON_PREVIEW_DIR");
            UseAssets(Environment.GetEnvironmentVariable("DON_ASSETS_DIR"));
            RenderAll(dir);
        }

        // Names every model, puchichara and makeup of a local 3dchara folder to DonLibrary.
        public static int UseAssets(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return 0;
            int count = 0;
            foreach (var (kind, ext) in new[] { ("cos", ".glb"), ("head", ".glb"), ("body", ".glb"), ("puchi", ".png"), ("puchi", ".otanim"), ("paint", ".png") })
            {
                string sub = Path.Combine(folder, kind);
                if (!Directory.Exists(sub)) continue;
                foreach (var file in Directory.GetFiles(sub, "*" + ext))
                    if (int.TryParse(Path.GetFileNameWithoutExtension(file), out int id)) { DonLibrary.UseFile(kind, id, file); count++; }
            }
            return count;
        }

        public static void RenderAll(string dir)
        {
            Directory.CreateDirectory(dir);
            foreach (var shot in Shots())
                File.WriteAllBytes(Path.Combine(dir, shot.Name + ".png"), Render(shot, 480, 560, 0.5f, 0.15f).EncodeToPNG());
            // As a screen shows it (the target shrunk to screen pixels): the old way (2x, one tap,
            // no MSAA) against the game's, at 1080 and 720 lines.
            var shots = Shots();
            foreach (var (name, index) in new[] { ("normal", 0), ("cos30", 6), ("head3_body4", 7) })
            foreach (float screen in new[] { 1f, 2f / 3f })
            {
                string lines = screen == 1 ? "1080" : "720";
                File.WriteAllBytes(Path.Combine(dir, $"screen{lines}-{name}-old.png"),
                    Render(shots[index], 480, 560, 0.5f, 0.15f, new Options { Screen = screen, Msaa = 1, Footprint = false }).EncodeToPNG());
                File.WriteAllBytes(Path.Combine(dir, $"screen{lines}-{name}-new.png"),
                    Render(shots[index], 480, 560, 0.5f, 0.15f, new Options { Screen = screen }).EncodeToPNG());
            }
            Debug.Log("Don previews written to " + dir);
        }

        // How the picture is made: screen pixels per layout unit (0 keeps the supersampled target
        // as it is), the supersampling, MSAA, and the composite's footprint filter.
        public struct Options
        {
            public float Screen;
            public float Supersample;
            public int Msaa;
            public bool Footprint;
            public static Options Target => new Options { Screen = 0, Supersample = 2, Msaa = DonView.Msaa, Footprint = true };
        }

        // One Don in a width × height layout-unit area standing at the pivot, composited onto grey
        // (so the dark rim shows); by default the supersampled target itself.
        public static Texture2D Render(Shot shot, int width, int height, float pivotX, float pivotY, Options? options = null)
        {
            var chara = new Chara3D(shot.Look, shot.Mirror);
            try
            {
                chara.SetAnim(shot.Anim);
                chara.SetFrozen(true);
                chara.SetAnimProgress(shot.Progress);
                return Render(chara, width, height, pivotX, pivotY, 1, options ?? Options.Target);
            }
            finally { chara.Dispose(); }
        }

        public static Texture2D Render(Chara3D chara, int width, int height, float pivotX, float pivotY, float drawScale, Options options)
        {
            if (options.Supersample <= 0) options.Supersample = 2;
            if (options.Msaa <= 0) options.Msaa = DonView.Msaa;
            float screen = options.Screen > 0 ? options.Screen : options.Supersample;
            float ppu = screen * (options.Screen > 0 ? options.Supersample : 1);
            int w = Mathf.CeilToInt(width * ppu), h = Mathf.CeilToInt(height * ppu);
            int sw = Mathf.CeilToInt(width * screen), sh = Mathf.CeilToInt(height * screen);
            var target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
                { antiAliasing = DonView.SupportedMsaa(w, h, options.Msaa), filterMode = FilterMode.Bilinear };
            var shown = new RenderTexture(sw, sh, 0, RenderTextureFormat.ARGB32);
            var cmd = new CommandBuffer { name = "Don preview" };
            var composite = new Material(Resources.Load<Shader>(DonLibrary.Root + "DonComposite"));
            try
            {
                float size = Chara3D.Scale720 * DonView.LayoutScale * drawScale;
                float outline = 2.5f * drawScale * (1080f * ppu) / 720f;
                chara.Render(cmd, target, width, height, pivotX * width, (1 - pivotY) * height, size, outline);
                Graphics.ExecuteCommandBuffer(cmd);
                composite.SetFloat("_Thickness", 1.5f * drawScale * ppu);
                composite.SetColor("_TintColor", chara.TintColor);
                composite.SetFloat("_TintAmount", chara.TintAmount);
                composite.SetFloat("_Footprint", options.Footprint ? 1 : 0);
                var old = RenderTexture.active;
                RenderTexture.active = shown;
                GL.Clear(true, true, new Color(0.5f, 0.5f, 0.5f, 1));
                RenderTexture.active = old;
                Graphics.Blit(target, shown, composite);
                RenderTexture.active = shown;
                var result = new Texture2D(sw, sh, TextureFormat.RGBA32, false);
                result.ReadPixels(new Rect(0, 0, sw, sh), 0, 0);
                result.Apply();
                RenderTexture.active = old;
                return result;
            }
            finally
            {
                cmd.Release();
                UnityEngine.Object.DestroyImmediate(composite);
                target.Release(); UnityEngine.Object.DestroyImmediate(target);
                shown.Release(); UnityEngine.Object.DestroyImmediate(shown);
            }
        }
    }
}
