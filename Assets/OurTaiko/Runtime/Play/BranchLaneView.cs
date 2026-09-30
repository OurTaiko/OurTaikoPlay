using UnityEngine;

namespace OurTaiko
{
    public sealed class BranchLaneView : MonoBehaviour
    {
        public UnityEngine.UI.Image background, previousLabel, currentLabel, levelChange;
        public Sprite normalLabel, expertLabel, masterLabel, expertBackground, masterBackground, levelUp, levelDown;

        BranchRoute route;
        Vector2 labelPosition;
        double changedAt;
        int direction;
        bool animating;

        void Awake() => labelPosition = currentLabel.rectTransform.anchoredPosition;

        public void Initialize(bool hasBranches)
        {
            gameObject.SetActive(hasBranches);
            if (!hasBranches) return;
            route = BranchRoute.Normal;
            animating = false;
            background.enabled = previousLabel.enabled = levelChange.enabled = false;
            currentLabel.sprite = normalLabel;
            currentLabel.color = Color.white;
            currentLabel.rectTransform.anchoredPosition = labelPosition;
        }

        public void Select(BranchRoute next, double time)
        {
            if (next == route) return;
            direction = next > route ? 1 : -1;
            previousLabel.sprite = currentLabel.sprite;
            previousLabel.enabled = levelChange.enabled = true;
            currentLabel.sprite = next == BranchRoute.Master ? masterLabel
                : next == BranchRoute.Expert ? expertLabel : normalLabel;
            background.sprite = next == BranchRoute.Master ? masterBackground : expertBackground;
            background.enabled = next != BranchRoute.Normal;
            levelChange.sprite = direction > 0 ? levelUp : levelDown;
            route = next;
            changedAt = time;
            animating = true;
            ShowTime(time);
        }

        public void ShowTime(double time)
        {
            if (!animating) return;
            float elapsed = (float)(time - changedAt);
            // PyTaikoGreen game/animation.json, IDs 41–45: 100 ms nudge,
            // then 133 ms slide/crossfade; the level badge pulses and fades out.
            float nudge = EaseOut(Progress(elapsed, 0, 0.100f)) * 20;
            float fade = Progress(elapsed, 0.100f, 0.133f);
            float slide = EaseOut(fade) * 70;
            previousLabel.rectTransform.anchoredPosition = labelPosition + Vector2.down * ((nudge - slide) * direction);
            currentLabel.rectTransform.anchoredPosition = labelPosition + Vector2.down * ((70 - slide) * direction);
            previousLabel.color = new Color(1, 1, 1, 1 - fade);
            currentLabel.color = new Color(1, 1, 1, fade);
            background.color = new Color(1, 1, 1, Mathf.Min(fade, 0.5f));
            float levelFade = Progress(elapsed, 0, 0.116f) - Progress(elapsed, 1.276f, 0.116f);
            float scale = 1 + 0.2f * (Progress(elapsed, 0, 0.116f) - Progress(elapsed, 0.116f, 0.116f));
            levelChange.color = new Color(1, 1, 1, levelFade);
            levelChange.rectTransform.localScale = new Vector3(scale, scale, 1);
            previousLabel.enabled = fade < 1;
            if (elapsed >= 1.392f)
            {
                levelChange.enabled = false;
                animating = false;
            }
        }

        static float Progress(float time, float delay, float duration) => Mathf.Clamp01((time - delay) / duration);
        static float EaseOut(float progress) => progress * (2 - progress);
    }
}
