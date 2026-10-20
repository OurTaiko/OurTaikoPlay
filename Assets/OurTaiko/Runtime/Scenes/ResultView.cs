using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The result screen's saved hierarchy. Layout, static art and sizes are authored in the scene;
    // ResultScene binds these references, fills in the run (title, difficulty, digits, gauge art,
    // message) and drives alpha, scale, frames and the animated offsets from the saved positions.
    // 1P's board is this view's own (ResultBoardView); a two-player result adds secondBoard.
    public sealed class ResultView : ResultBoardView
    {
        // One copy of the animated backdrop (Background under the board, FadeIn over it). Clouds
        // and the clear Fuji are saved at their frame-0 places; the drift is added to those.
        [Serializable]
        public sealed class BackgroundView
        {
            public CanvasGroup group;
            public Image[] sky, skyClear;
            public Image fuji, fujiClear;
            public Image[] clouds, cloudsClear;
            public Image header;
        }

        [Header("Backdrop")]
        public BackgroundView background;
        public BackgroundView fadeIn;

        [Header("Song")]
        public TextMeshProUGUI songTitle;
        public TextMeshProUGUI songNumber;

        [Header("Two players")]
        [Tooltip("2P's board on the right half (result_player.lua index 1). Empty in a single-player result.")]
        public ResultBoardView secondBoard;
        [Tooltip("2P's score / timing-detail pages and their navigation, on the right half.")]
        public ResultAnalysisView secondAnalysis;

        [Header("Overlays")]
        public TextMeshProUGUI freePlay;
        public ResultAnalysisView analysis;
    }
}
