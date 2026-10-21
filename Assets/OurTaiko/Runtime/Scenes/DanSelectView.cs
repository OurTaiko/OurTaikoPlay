using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The 段位道場 select screen's saved hierarchy (Nijiiro dan_select art, PyTaikoGreen
    // dan_select.lua layout in 1920×1080 top-left pixels). DanSelectScene binds these references and
    // drives positions, frames, texts and alpha; the layout itself is authored in the scene.
    public sealed class DanSelectView : MonoBehaviour
    {
        // One course chip of the ribbon (box/folder, 88×184, its name written top to bottom).
        [Serializable]
        public sealed class ChipView
        {
            public RectTransform root;
            public Image folder;
            public TextMeshProUGUI name;
            public PointerRelay hit;
        }

        // One song row of the details board, dan_yellow_box_offset (110 px) below the one above.
        [Serializable]
        public sealed class SongRowView
        {
            public RectTransform root;
            public Image banner, label, difficulty, star, times;
            [Tooltip("difficulty_num digits, centred on the row's level position.")]
            public Image[] level;
            public TextMeshProUGUI title, subtitle;
        }

        // One right-hand exam row (judge_box): its icon and one segment per song, or one for the course.
        [Serializable]
        public sealed class ExamRowView
        {
            public RectTransform root;
            public Image icon;
            public Image[] segments;
            public TextMeshProUGUI[] captions;
        }

        [Header("Ribbon")]
        public RectTransform ribbon;
        public ChipView[] chips;
        [Tooltip("box/folder/0..6, by the course's color.")]
        public Sprite[] folders;
        public Image cursor, arrowLeft, arrowRight;
        public SwipeRelay ribbonSwipe;

        [Header("Details board")]
        public RectTransform board;
        [Tooltip("yellow_box 9-slice: top left, top, top right, left, centre, right, bottom left, bottom, bottom right.")]
        public Image[] yellowBox;
        public CanvasGroup contents;
        public Image frame;
        [Tooltip("yellow_box/frame/0..6, by the course's color.")]
        public Sprite[] frames;
        public TextMeshProUGUI horiName;
        [Tooltip("The course's best local record (DanRecordStore), under its plate.")]
        public TextMeshProUGUI record;
        public SongRowView[] songs;
        public Sprite[] genreBanners, songLabels, difficulties, levelDigits;
        public TextMeshProUGUI error;

        [Header("Exams")]
        public RectTransform exams;
        public RectTransform gauge;
        public Image gaugeIcon;
        public TextMeshProUGUI gaugeCaption;
        public ExamRowView[] examRows;
        [Tooltip("exam_gauge, exam_judgeperfect, exam_judgegood, exam_judgebad, exam_hit, exam_score, exam_combo, exam_roll (DanExamType order).")]
        public Sprite[] examIcons;
        [Tooltip("exam_seg_label/0 (the course) and 1..3 (each song).")]
        public Sprite[] segmentLabels;

        [Header("Empty")]
        public TextMeshProUGUI empty;

        [Header("Confirmation")]
        public CanvasGroup confirm;
        public Image yesHighlight, noHighlight, yesOutline, noOutline, optionHighlight;
        public PointerRelay shade, yesHit, noHit, optionHit;

        [Header("Global chrome")]
        public NameplateView nameplate;
        public OptionPanelView options;
    }
}
