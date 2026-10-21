using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The 段位道場 result's saved hierarchy (Nijiiro dan_result art, dan_result_draw.lua layout in
    // 1920×1080 pixels): page 1 holds a plate per song, page 2 the totals, the exams and the stamp,
    // and the 昇段 celebration / 祝 popup cover both.
    public sealed class DanResultView : MonoBehaviour
    {
        [Serializable]
        public sealed class SongPlateView
        {
            public RectTransform root;
            public CanvasGroup group;
            public Image banner, number, difficulty, unreached;
            public Image[] level;
            public TextMeshProUGUI title;
            [Tooltip("良, 可, 不可, 連打: each count's digits, right-aligned.")]
            public DigitRow[] counts;
        }

        [Serializable]
        public sealed class DigitRow
        {
            public Image[] digits;
        }

        // One finished song of a per-song exam (exam_sub_*), 465 px apart.
        [Serializable]
        public sealed class ExamSongView
        {
            public RectTransform root;
            public Image badge, bar;
            public RawImage rainbow;
            public TextMeshProUGUI caption;
            public Image[] digits;
        }

        [Serializable]
        public sealed class ExamRowView
        {
            public RectTransform root;
            public Image icon;
            public RectTransform all;
            public Image allBar;
            public RawImage allRainbow;
            public TextMeshProUGUI allCaption;
            public Image[] allDigits;
            public ExamSongView[] songs;
            public RectTransform gauge;
            public Image norma, fill, soul, marker;
            [Tooltip("tamashii/flame behind the soul and fever_glow over it, at a full gauge.")]
            public Image flame, feverGlow;
            public Image[] gaugeDigits;
            public TextMeshProUGUI gaugeCaption;
        }

        [Header("Page 1")]
        public CanvasGroup songsPage;
        public SongPlateView[] plates;
        public Sprite[] genreBanners, songNumbers, difficulties, levelDigits, countDigits;

        [Header("Plate (both pages)")]
        public Image emblem;
        public Sprite[] emblems;
        [Tooltip("The course's name top to bottom across the plate (the arcade's large name), on both pages.")]
        public TextMeshProUGUI danName;
        [Tooltip("The course's name across the plate's foot.")]
        public TextMeshProUGUI horiName;

        [Header("Page 2")]
        public CanvasGroup totalsPage;
        public Image[] scoreDigits;
        public Sprite[] scoreDigitSprites;
        [Tooltip("良, 可, 不可, 連打, 最大コンボ, 叩けた数.")]
        public DigitRow[] totals;
        public ExamRowView[] exams;
        [Tooltip("exam_gauge, exam_judgeperfect, exam_judgegood, exam_judgebad, exam_hit, exam_score, exam_combo, exam_roll (DanExamType order).")]
        public Sprite[] examIcons;
        public Sprite[] badges, valueDigits, valueDigitsMax, songDigits, songDigitsMax;
        public Sprite barRed, barGold, barMax, barDown80, songRed, songGold, songMax, songDown80;
        public Texture rainbow, songRainbow;
        public Sprite gaugeFill, gaugeFillClear, soulLit, soulDark;
        public Sprite[] gaugeNumbersNoClear, gaugeNumbersClear, gaugeNumbersMax;
        public Sprite percentNoClear, percentClear, percentMax;
        [Tooltip("tamashii/flame 0..7.")]
        public Sprite[] flames;
        public Image stamp;
        [Tooltip("fail, red_clear, red_clear_02, red_clear_03, gold_clear, gold_clear_02, gold_clear_03 (by verdict 0..6).")]
        public Sprite[] stamps;
        public RectTransform bestScore;
        public Image[] bestScoreDigits;
        public Sprite[] bestScoreDigitSprites;
        public Image bestScoreGlow;

        [Header("昇段 / 祝")]
        public RectTransform celebration;
        [Tooltip("shoudan/bg_w, bg_g (フルコンボ), bg_r (全良).")]
        public Image shoudanBackground;
        public Sprite shoudanWhite, shoudanGold, shoudanRainbow;
        [Tooltip("The four glyphs (rank left, rank right, 合, 格) and the ghost each throws.")]
        public Image[] glyphs, glyphGhosts;
        [Tooltip("shoudan/text_white and text_gold 0..19.")]
        public Sprite[] textWhite, textGold;
        public Image congratsBackground, congratsText;
        [Tooltip("congrats/text_01 and text_02 (達人) 0..3: ja, en, ko, zh.")]
        public Sprite[] congratsText1, congratsText2;

        [Header("Global chrome")]
        public RectTransform noSave;
        public NameplateView nameplate;
        public CanvasGroup fade;
    }
}
