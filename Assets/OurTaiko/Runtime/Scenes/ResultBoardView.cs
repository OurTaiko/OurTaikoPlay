using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // One player's result board (result_player.lua): success backdrop, judge rows, score, high
    // score, rank, crown, message and gauge. ResultView derives from it, so Result keeps 1P's board on
    // the view as before; a two-player result adds a second board for 2P (ResultView.secondBoard).
    public class ResultBoardView : MonoBehaviour
    {
        [Serializable]
        public sealed class DigitRow
        {
            [Tooltip("Ones digit first.")]
            public Image[] digits;
        }

        public Image success;
        [Tooltip("footer_2p: this board's half of the clear backdrop when two played (result_player.lua t_success).")]
        public Image successTwoPlayers;

        [Header("Board")]
        public Image difficulty;
        [Tooltip("良, 可, 不可, 連打数, 最大コンボ.")]
        public DigitRow[] judgeRows;
        [Tooltip("Ones digit first.")]
        public Image[] scoreOutline, scoreFill;

        [Header("High score")]
        public RectTransform highScore;
        public CanvasGroup highScoreGroup;
        [Tooltip("Ones digit first.")]
        public Image[] highScoreDigits;

        public ScoreRankView scoreRank;

        [Header("Crown and message")]
        public Image crown;
        public Image crownFade, burstA, burstB, stars, shine, message;
        public TextMeshProUGUI messageText;

        [Header("Gauge")]
        [Tooltip("Gauge art (0 easy, 1 normal, 2 hard/oni) the clear marks are placed for; other arts shift them by their clear cell.")]
        public int gaugeArt = 2;
        public Image unfilled;
        public Image bar, clearTransition, clearTop, clearBottom;
        public Image[] rainbow;
        public Image overlay, clearCaption, soulFire, soul, soulSheen;

        public NameplateView nameplate;
    }
}
