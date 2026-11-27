using TMPro;
using UnityEngine;

namespace OurTaiko
{
    // Saved only in Play.Practice, which is copied from Play.Single by the Editor migration: the practice
    // menu and the judgement panel in the corner the practice stage leaves empty.
    public sealed class PracticeView : MonoBehaviour
    {
        public GameObject panel;
        public TMP_Text heading, value, hint;
        public UnityEngine.UI.Button previous, next, confirm;
        [Tooltip("The bottom-left judgement panel (良率, 良 / 可 / 不可 with their shares, 連打数); the lane's HUD feeds it.")]
        public JudgeCounterView judgments;
        [Tooltip("The band under the lane, after YataiDON's practice HUD: 曲テンポ and 小節 values, then the progress bar.")]
        public TMP_Text tempoValue, measureValue;
        [Tooltip("The band's progress bar, after YataiDON's: the furthest any attempt has played since the last restart (gauge_max) and where the play or the cursor is now, each filling its track from the left.")]
        public RectTransform progressMax, progressNow;
        [Tooltip("Hidden template, copied once per GOGO start of the route; its anchor's x is set to the start.")]
        public RectTransform gogoMarker;
        int shownTempo = -1, shownMeasure = -1, shownCount = -1;
        readonly System.Collections.Generic.List<RectTransform> gogos = new System.Collections.Generic.List<RectTransform>();

        // The progress bar's fills, as fractions of the song (PracticeProgress.Fraction).
        public float ProgressNow => progressNow.anchorMax.x;
        public float ProgressMax => progressMax.anchorMax.x;
        public System.Collections.Generic.IReadOnlyList<RectTransform> GogoMarkers => gogos;

        public void ShowProgress(double now, double max)
        {
            Fill(progressNow, now);
            Fill(progressMax, max);
        }

        static void Fill(RectTransform fill, double fraction)
        {
            if (fill == null) return;
            fill.anchorMax = new Vector2((float)fraction, fill.anchorMax.y);
            fill.gameObject.SetActive(fraction > 0);
        }

        // One marker per GOGO start, at its fraction of the song.
        public void ShowGogos(System.Collections.Generic.IEnumerable<double> fractions)
        {
            if (gogoMarker == null) return;
            foreach (var marker in gogos) Destroy(marker.gameObject);
            gogos.Clear();
            foreach (double fraction in fractions)
            {
                var marker = Instantiate(gogoMarker, gogoMarker.parent);
                marker.name = "Gogo";
                marker.anchorMin = new Vector2((float)fraction, marker.anchorMin.y);
                marker.anchorMax = new Vector2((float)fraction, marker.anchorMax.y);
                marker.gameObject.SetActive(true);
                gogos.Add(marker);
            }
        }

        // The band's speed (tenths: 10 is 1.0×) and measure (0-based, of count).
        public void ShowStrip(int speedTenths, int measure, int count)
        {
            if (tempoValue != null && speedTenths != shownTempo)
            {
                shownTempo = speedTenths;
                tempoValue.text = speedTenths / 10 + "." + speedTenths % 10 + "×";
            }
            if (measureValue != null && (measure != shownMeasure || count != shownCount))
            {
                shownMeasure = measure; shownCount = count;
                measureValue.text = (measure + 1) + " / " + count;
            }
        }
        static readonly string[] BranchKeys = { "practice.branch.normal", "practice.branch.expert", "practice.branch.master" };
        public void Show(PracticeStage stage, PracticeProgress progress, BranchRoute branch)
        {
            panel.SetActive(true);
            switch (stage)
            {
                case PracticeStage.Branch:
                    heading.text = L10n.Get("Play", "practice.branch.heading");
                    value.text = L10n.Get("Play", BranchKeys[(int)branch]);
                    hint.text = L10n.Get("Play", "practice.branch.hint");
                    break;
                case PracticeStage.Speed:
                    heading.text = L10n.Get("Play", "practice.speed.heading");
                    value.text = progress.Speed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "x";
                    hint.text = L10n.Get("Play", "practice.speed.hint");
                    break;
                default:
                    heading.text = L10n.Get("Play", "practice.measure.heading");
                    value.text = (progress.Measure + 1) + " / " + progress.Count;
                    hint.text = L10n.Get("Play", "practice.measure.hint");
                    break;
            }
        }
    }
}
