using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // DanPlayScene's own HUD (Nijiiro game/dan_info, gauge_dan and lane/dan_between art, YataiDON's
    // draw_exam_row / draw_dan_info layout in 1920×1080 pixels): up to three exam rows under the
    // lane, the course plate on the right, the soul gauge's border plate, and the doors between
    // songs. Saved in DanPlayScene; DanPlayScene drives it.
    public sealed class DanPlayView : MonoBehaviour
    {
        // One small row under a per-song exam: a finished song's own bar and count.
        [Serializable]
        public sealed class SubRowView
        {
            public RectTransform root;
            public Image track, bar, front, chip;
            public RawImage rainbow;
            public Image[] digits;
        }

        // One exam row (exam_bg); course-wide rows use the long `_all` frame and bar.
        [Serializable]
        public sealed class ExamRowView
        {
            public RectTransform root;
            public Image badge, frameBackAll, overlay1, bar, frameFrontAll, overlay2, icon, failed, percent;
            public RawImage rainbow;
            public TextMeshProUGUI caption;
            public Image[] digits;
            public SubRowView[] songs;
        }

        [Header("Exams")]
        public ExamRowView[] rows;
        [Tooltip("exam_badge/0 (the course) and 1..3 (the song being played).")]
        public Sprite[] badges;
        [Tooltip("exam_gauge, exam_judgeperfect, exam_judgegood, exam_judgebad, exam_hit, exam_score, exam_combo, exam_roll (DanExamType order).")]
        public Sprite[] icons;
        public Sprite[] valueDigits, songDigits, songChips;
        public Sprite barRed, barGold, barMax, barDown80, barFail, songRed, songGold, songMax, songDown80;
        public Texture rainbow, rainbowAll, songRainbow;

        [Header("Course plate")]
        public Image frame;
        [Tooltip("dan_info/frame/0..6, by the course's color.")]
        public Sprite[] frames;
        public TextMeshProUGUI horiName;

        [Header("Gauge border plate")]
        public RectTransform gaugePlate;
        public TextMeshProUGUI gaugeName, gaugeValue;

        [Header("Between songs")]
        public RectTransform between;
        public RectTransform doorRight, doorLeft;
        public TextMeshProUGUI betweenTitle, betweenSubtitle;
    }
}
