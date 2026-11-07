using System;
using System.Globalization;
using UnityEngine;

namespace OurTaiko
{
    // How an early/late offset reads and is coloured wherever it is shown (the hit feedback, the
    // practice timing review).
    public static class TimingOffsetText
    {
        public static readonly Color EarlyColor = new Color32(55, 155, 255, 255);
        public static readonly Color LateColor = new Color32(255, 75, 65, 255);

        // Keep the raw sign even when a sub-ms value rounds to zero.
        public static string Format(double offsetMs) => (offsetMs < 0 ? "-" : "+")
            + Math.Round(Math.Abs(offsetMs), MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        public static Color ColorOf(double offsetMs) => offsetMs < 0 ? EarlyColor : offsetMs > 0 ? LateColor : Color.white;
    }
}
