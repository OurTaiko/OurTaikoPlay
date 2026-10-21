using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The 段位道場 doors over the course select (YataiDON song_select/dan_transition.lua draw_open,
    // Nijiiro loading_dani_intro clip #15 as anim/dani_intro). Coming from Entry the whole intro
    // plays (frames 5..262): the shut doors under a black scrim that eases to half, 段 位 道 場
    // stamped in one by one, then the doors part, the scrim clears and the dojo picture pushes in
    // and fades. From anywhere else only the opening plays (182..262).
    public sealed class DanIntroView : MonoBehaviour
    {
        public const double FullFrom = 5, OpenFrom = 182, End = 262, FrameMs = 1000.0 / 60;
        const float DoorWidth = 1200, Kanji = 400, AtlasWidth = 2000, AtlasHeight = 1080;
        // The kanji cells of the atlas (段, 位, 道, 場) and the track that places each.
        static readonly (string Track, float X, float Y)[] Cells = { ("#7@4", 1200, 0), ("#9@5", 1600, 0), ("#11@6", 1200, 400), ("#13@7", 1600, 400) };

        [Tooltip("anim/dani_intro (Nijiiro).")]
        public TextAsset timeline;
        [Tooltip("dan_select/global/bg, pushed in under the doors.")]
        public RawImage background;
        [Tooltip("song_select/dan_transition/background: the door panel, the scrim cell and the four kanji.")]
        public RawImage doorLeft, doorRight, scrim;
        public RawImage[] kanji;

        LumenClip clip;
        LumenClip Clip => clip ??= timeline != null ? LumenClip.Parse(timeline.text) : LumenClip.Empty;

        public bool IsPlaying => gameObject.activeSelf;

        public void Hide() => gameObject.SetActive(false);

        // The frame `elapsedMs` into the intro; false once it is over.
        public static double Frame(bool full, double elapsedMs) => (full ? FullFrom : OpenFrom) + elapsedMs / FrameMs;

        public void Show(double f)
        {
            if (f >= End) { Hide(); return; }
            gameObject.SetActive(true);
            var c = Clip;

            // #3@0: the dojo picture, scaled about its centre.
            double ba = c.Get("#3@0", f, "a", 0);
            background.enabled = ba > 0;
            if (background.enabled)
            {
                double scale = c.Get("#3@0", f, "sx", 1);
                Place(background.rectTransform, c.Get("#3@0", f, "tx", 960), c.Get("#3@0", f, "ty", 540), 1920 * scale, 1080 * scale);
                background.color = new Color(1, 1, 1, (float)ba);
            }

            // #5@1 / #5@2: the right door's left edge and the left door (the same panel mirrored).
            double right = c.Get("#5@1", f, "tx", 960), sx = c.Get("#5@1", f, "sx", 1), ty = c.Get("#5@1", f, "ty", 540);
            double left = c.Get("#5@2", f, "tx", 960) + DoorWidth * c.Get("#5@2", f, "sx", -1);
            double width = DoorWidth * System.Math.Abs(sx), top = ty - 540;
            Corner(doorRight.rectTransform, right, top, width, 1080);
            Corner(doorLeft.rectTransform, left, top, width, 1080);
            doorRight.uvRect = new Rect(0, 0, DoorWidth / AtlasWidth, 1);
            doorLeft.uvRect = new Rect(DoorWidth / AtlasWidth, 0, -DoorWidth / AtlasWidth, 1);

            // #1@3: the full-screen black over both doors, under the kanji.
            float dark = (float)c.Get("#1@3", f, "a", 0.5);
            scrim.enabled = dark > 0;
            scrim.color = new Color(1, 1, 1, dark);
            scrim.uvRect = new Rect(1200 / AtlasWidth, 1 - 864 / AtlasHeight, 64 / AtlasWidth, 64 / AtlasHeight);

            for (int i = 0; i < kanji.Length && i < Cells.Length; i++)
            {
                var cell = Cells[i];
                var image = kanji[i];
                // Each glyph's track starts where it is placed.
                bool placed = c.Window(cell.Track, out double first, out _) && f >= first;
                double alpha = placed ? c.Get(cell.Track, f, "a", 0) : 0;
                image.enabled = alpha > 0;
                if (!image.enabled) continue;
                double size = Kanji * c.Get(cell.Track, f, "sx", 1);
                Place(image.rectTransform, c.Get(cell.Track, f, "tx", 0), c.Get(cell.Track, f, "ty", 0), size, size);
                image.uvRect = new Rect(cell.X / AtlasWidth, 1 - (cell.Y + Kanji) / AtlasHeight, Kanji / AtlasWidth, Kanji / AtlasHeight);
                image.color = new Color(1, 1, 1, (float)alpha);
            }
        }

        // Centre (x, y) in top-left screen pixels.
        static void Place(RectTransform rect, double x, double y, double width, double height)
        {
            rect.sizeDelta = new Vector2((float)width, (float)height);
            rect.anchoredPosition = new Vector2((float)(x - width / 2), (float)-(y - height / 2));
        }

        static void Corner(RectTransform rect, double x, double y, double width, double height)
        {
            rect.sizeDelta = new Vector2((float)width, (float)height);
            rect.anchoredPosition = new Vector2((float)x, (float)-y);
        }
    }
}
