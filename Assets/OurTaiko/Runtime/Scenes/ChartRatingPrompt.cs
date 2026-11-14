using System;
using OurTaiko.Multi;
using UnityEngine;

namespace OurTaiko
{
    // The result screen's question before it is left: "how was this chart?", over everything else
    // on the stage. Three plates from left to right, each under its face: liked it (where the
    // focus starts), not now, did not like it. Ka moves the focus, don presses the focused plate,
    // a tap or click presses a plate directly (PlateUi). The answer goes to `chosen` once.
    public sealed class ChartRatingPrompt
    {
        public const string Good = "good", Skip = "skip", Bad = "bad";

        public bool IsOpen { get; private set; }
        public string Focused => IsOpen ? ui.Focused?.Key : null;
        // The texts shown, for tests.
        public string AllText => IsOpen ? ui.AllText : "";

        readonly PlateUi ui;
        readonly RectTransform root;
        readonly Action<string> chosen;

        public ChartRatingPrompt(RectTransform stage, Action<string> chosen)
        {
            this.chosen = chosen;
            root = SkinUi.Rect("ChartRating", stage, 1920, 1080);
            root.TopLeft(0, 0);
            ui = new PlateUi(root, MultiArt.Load(), Choose);
        }

        public void Open(string songTitle)
        {
            if (IsOpen) return;
            IsOpen = true;
            root.SetAsLastSibling();
            var faces = RatingArt.Load();
            ui.Begin();
            var content = ui.Content;
            // The shade also keeps taps from what is under the prompt, on any screen shape.
            ui.Picture(content, null, 6000, 4000, 960, 540, new Color(0, 0, 0, 0.62f)).raycastTarget = true;
            ui.Picture(content, ui.Art.panel, 1120, 610, 960, 540);
            ui.Label(content, L10n.Get("Result", "rating.title"), 60, 960, 316, 1000);
            ui.Label(content, songTitle, 34, 960, 388, 1000).color = PlateUi.Gold;
            Answer(Good, 620, PlateUi.Orange, faces.good, "rating.good");
            Answer(Skip, 960, PlateUi.Slate, faces.skip, "rating.skip");
            Answer(Bad, 1300, PlateUi.Violet, faces.bad, "rating.bad");
            ui.Settle("rating", Good);
        }

        void Answer(string key, float x, Color color, Sprite face, string text)
        {
            const float width = 300, height = 330;
            var plate = ui.Button(key, ui.Content, width, height, x, 622, color, () => Choose(key));
            ui.Picture(plate, face, 204, 204, width / 2, 126);
            ui.Label(plate, L10n.Get("Result", text), 44, width / 2, 270, width - 40, true);
        }

        // Ka: the focus to the plate on that side, round the ends.
        public void Move(int delta) { if (IsOpen) ui.Move(delta); }
        // Don: the focused plate.
        public void Press() { if (IsOpen) ui.Focused?.Press?.Invoke(); }

        public void Choose(string key)
        {
            if (!IsOpen) return;
            IsOpen = false;
            UnityEngine.Object.Destroy(ui.Content.gameObject);
            chosen?.Invoke(key);
        }
    }
}
