using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    public sealed class SongSelectView : MonoBehaviour
    {
        [Serializable]
        public sealed class CourseCardView
        {
            public Image board, crown, star, level, bar, branch;
            public ScoreRankView scoreRank;
            public Image[] dots;
            public TextMeshProUGUI name;
            public PointerRelay click;
        }

        public SongBoardView boardPrefab;
        public SongBoardView[] songBoards;
        [Tooltip("Pooled for the online category folders and their もどる boards.")]
        public FolderBoardView folderPrefab;
        public CanvasGroup courseGroup;
        public Image mark, backboard, back, option, auto, frame, glow, balloon, uraChange;
        [Tooltip("2P's course cursor: difficulty_select_bar2 under the cards and 2p_balloon above them.")]
        public Image frame2P, balloon2P;
        [Tooltip("2P's glow on もどる / オプション, like 1P's glow.")]
        public Image glow2P;
        public TextMeshProUGUI header, headerSub;
        public CourseCardView[] cards;
        public OptionPanelView options;
        [Tooltip("2P's 演奏オプション board: the same panel at option_p2 (1490 to the right) with the 2p badge.")]
        public OptionPanelView options2P;
        public NameplateView nameplate;
        [Tooltip("song_select_nameplate_2p, shown once 2P has joined.")]
        public NameplateView nameplate2P;
        public CanvasGroup nameplateGroup2P;
        public SongSelectOverlayView overlays;
        public SongBestScoreView bestScore;
        public SongSearchView search;
        [Header("Wheel animation layout")]
        public Vector2 wheelCentre = new Vector2(960, 540);
        public float rowPitch = 135, expandGap = 120, rowCurve = 40;
    }
}
