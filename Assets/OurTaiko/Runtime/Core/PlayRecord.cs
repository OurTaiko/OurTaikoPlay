using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace OurTaiko.Online
{
    // Recorded input sent with every score. Version 2 adds the play options, so the chart the inputs
    // were played on can be rebuilt: ランダム is stored as the notes it flipped, not as a seed.
    // Servers that only know version 1 drop a version 2 record and keep the score.
    // Types: 0 left ka, 1 left don, 2 right don, 3 right ka; times are judged game time in ms.
    public sealed class PlayRecord
    {
        public const int MaxInputs = 100000, MaxFlipped = 100000;
        public const double MaxTimeMs = 86400000;
        public int AudioOffsetMs, VisualOffsetMs;
        public readonly List<(double Ms, int Type)> Inputs = new List<(double, int)>();
        // PlayOptions as played: speed in tenths, あべこべ, ドロン, ランダム (RandomMode as its number).
        public int Speed = PlayOptions.DefaultSpeed, Random;
        public bool Inverse, Display;
        // Indexes into the chart's Notes of the notes ランダム changed the colour of, ascending.
        public readonly List<int> Flipped = new List<int>();

        public static int TypeOf(bool isKa, bool right) => isKa ? (right ? 3 : 0) : (right ? 2 : 1);
        public static bool IsKa(int type) => type == 0 || type == 3;
        public static bool IsRight(int type) => type >= 2;

        // The options `chart` was prepared with (ChartModifiers.Apply).
        public void SetOptions(PlayOptions options, TaikoChart chart)
        {
            Speed = options.speed; Inverse = options.inverse; Display = options.display; Random = (int)options.random;
            Flipped.Clear();
            Flipped.AddRange(ChartModifiers.FlippedIn(chart));
        }

        public PlayRecord Copy()
        {
            var copy = new PlayRecord { AudioOffsetMs = AudioOffsetMs, VisualOffsetMs = VisualOffsetMs, Speed = Speed, Random = Random, Inverse = Inverse, Display = Display };
            copy.Inputs.AddRange(Inputs);
            copy.Flipped.AddRange(Flipped);
            return copy;
        }

        // null (sent as JSON null) when the record is too long or holds an invalid event.
        public JToken ToJson()
        {
            if (Inputs.Count > MaxInputs || Flipped.Count > MaxFlipped) return JValue.CreateNull();
            var inputs = new JArray();
            foreach (var (ms, type) in Inputs)
            {
                if (double.IsNaN(ms) || double.IsInfinity(ms) || Math.Abs(ms) > MaxTimeMs || type < 0 || type > 3) return JValue.CreateNull();
                inputs.Add(new JArray(ms, type));
            }
            return new JObject
            {
                ["version"] = 2, ["audio_offset_ms"] = AudioOffsetMs, ["visual_offset_ms"] = VisualOffsetMs, ["inputs"] = inputs,
                ["options"] = new JObject
                {
                    ["speed"] = Speed, ["inverse"] = Inverse, ["display"] = Display, ["random"] = Random, ["flipped"] = new JArray(Flipped),
                },
            };
        }

        // A stored record of version 1 (no options: played as the chart is) or 2; null when it is neither
        // or holds anything out of range.
        public static PlayRecord FromJson(JToken json)
        {
            if (!(json is JObject o) || !Int(o["version"], 1, 2, out int version)
                || !Int(o["audio_offset_ms"], int.MinValue, int.MaxValue, out int audio)
                || !Int(o["visual_offset_ms"], int.MinValue, int.MaxValue, out int visual)
                || !(o["inputs"] is JArray inputs) || inputs.Count > MaxInputs) return null;
            var record = new PlayRecord { AudioOffsetMs = audio, VisualOffsetMs = visual };
            foreach (var item in inputs)
            {
                if (!(item is JArray pair) || pair.Count != 2 || (pair[0].Type != JTokenType.Float && pair[0].Type != JTokenType.Integer)
                    || !Int(pair[1], 0, 3, out int type)) return null;
                double ms = (double)pair[0];
                if (double.IsNaN(ms) || double.IsInfinity(ms) || Math.Abs(ms) > MaxTimeMs) return null;
                record.Inputs.Add((ms, type));
            }
            if (version == 1) return record;
            if (!(o["options"] is JObject options) || !Int(options["speed"], PlayOptions.MinSpeed, PlayOptions.MaxSpeed, out record.Speed)
                || !Int(options["random"], 0, (int)RandomMode.Detarame, out record.Random)
                || options["inverse"]?.Type != JTokenType.Boolean || options["display"]?.Type != JTokenType.Boolean
                || !(options["flipped"] is JArray flipped) || flipped.Count > MaxFlipped) return null;
            record.Inverse = (bool)options["inverse"]; record.Display = (bool)options["display"];
            int last = -1;
            foreach (var item in flipped)
            {
                if (!Int(item, last + 1, int.MaxValue, out int index)) return null;
                record.Flipped.Add(last = index);
            }
            return record;
        }

        static bool Int(JToken token, int minimum, int maximum, out int value)
        {
            value = 0;
            if (token == null || token.Type != JTokenType.Integer) return false;
            long number = (long)token;
            if (number < minimum || number > maximum) return false;
            value = (int)number;
            return true;
        }
    }
}
