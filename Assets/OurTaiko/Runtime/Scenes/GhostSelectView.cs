using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // GhostSelect's screen, saved in the scene: a heading, a window of replay rows (the selected one
    // lit), a status line and the controls hint. GhostSelectScene fills it.
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

        public RectTransform stage;
        public TextMeshProUGUI heading, status, hint;
        public RowView[] rows;
        public Color rowColor = new Color(0, 0, 0, 0.55f), selectedColor = new Color(0.93f, 0.33f, 0.16f, 0.92f);
    }
}
