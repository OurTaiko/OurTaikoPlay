using System;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace OurTaiko
{
    // Saved only in PracticeScene, which is copied from SinglePlayScene by the Editor migration.
    public sealed class PracticeView : MonoBehaviour
    {
        public GameObject panel;
        public TMP_Text heading, value, hint;
        public UnityEngine.UI.Button previous, next, confirm;
        static readonly string[] Branches = { "普通譜面", "玄人譜面", "達人譜面" };
        public void Show(PracticeStage stage, PracticeProgress progress, BranchRoute branch, string course)
        {
            panel.SetActive(true);
            switch (stage)
            {
                case PracticeStage.Branch:
                    heading.text = "谱面分支";
                    value.text = Branches[(int)branch];
                    hint.text = "咔：切换分支    咚：选择小节";
                    break;
                case PracticeStage.Speed:
                    heading.text = "播放速度";
                    value.text = progress.Speed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "x";
                    hint.text = "咔：调整速度    咚：调整判定范围";
                    break;
                case PracticeStage.Judge:
                    heading.text = "判定范围";
                    value.text = JudgeText(progress.JudgePercent, course);
                    hint.text = "咔：调整判定范围    咚：开始练习";
                    break;
                default:
                    heading.text = "小节进度";
                    value.text = (progress.Measure + 1) + " / " + progress.Count;
                    hint.text = "咔：前后小节    咚：调整播放速度";
                    break;
            }
        }
        // "120%  良 30 / 可 90 / 不可 130 ms" on the value's single line: each window's half width in real
        // milliseconds at this size, whatever the speed. Plain ASCII separators: the font is a dynamic atlas.
        public static string JudgeText(int percent, string course)
        {
            var windows = PlaySession.StandardWindows(course);
            string Ms(double seconds) => Math.Round(seconds * percent * 10).ToString("0", CultureInfo.InvariantCulture);
            return percent + "%  <size=70%>良 " + Ms(windows.Good) + " / 可 " + Ms(windows.Ok) + " / 不可 " + Ms(windows.Bad) + " ms</size>";
        }
    }
}
