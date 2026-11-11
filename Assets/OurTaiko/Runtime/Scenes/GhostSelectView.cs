using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // GhostSelect's screen, saved in the scene: a heading, three shelf columns (servers, categories,
    // plays) of entry rows, a status line and the controls hint. GhostSelectScene fills it.
    public sealed class GhostSelectView : MonoBehaviour
    {
        [Serializable]
        public sealed class RowView
        {
            public RectTransform root;
            public Image background;
            public TextMeshProUGUI title, detail, score;
            public PointerRelay relay;
        }

        [Serializable]
        public sealed class ColumnView
        {
            public RectTransform root;
            public RowView[] rows;
        }

        public RectTransform stage;
        public TextMeshProUGUI heading, status, hint;
        [Tooltip("Servers, categories, plays.")]
        public ColumnView[] columns;
        public Color rowColor = new Color(0, 0, 0, 0.55f), backColor = new Color(0.12f, 0.2f, 0.35f, 0.7f);
        // The selection of the focused column, and the highlight of the others.
        public Color selectedColor = new Color(0.93f, 0.33f, 0.16f, 0.92f), highlightColor = new Color(0.62f, 0.27f, 0.16f, 0.85f);
    }
}
