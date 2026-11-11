using TMPro;
using UnityEngine;

namespace OurTaiko
{
    // Saved only in Play.Practice, which is copied from Play.Single by the Editor migration.
    public sealed class PracticeView : MonoBehaviour
    {
        public GameObject panel;
        public TMP_Text heading, value, hint;
        public UnityEngine.UI.Button previous, next, confirm;
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
