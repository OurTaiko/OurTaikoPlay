using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OurTaiko
{
    // Local best records per chart and course, the role scores_manager plays for song select and
    // the result screen. Stored as JSON in the persistent data folder.
    public sealed class ScoreStore
    {
        [Serializable]
        public sealed class Record
        {
            public string key;
            public int score, good, ok, bad, maxCombo, rolls;
            public Crown crown;
        }

        [Serializable]
        sealed class Document { public List<Record> records = new List<Record>(); }

        static ScoreStore shared;
        readonly string path;
        readonly Dictionary<string, Record> records = new Dictionary<string, Record>();

        // Tests replace it with a temporary file so they never touch the player's records.
        public static ScoreStore Shared
        {
            get => shared ??= new ScoreStore(Path.Combine(Application.persistentDataPath, "scores.json"));
            set => shared = value;
        }

        public ScoreStore(string path)
        {
            this.path = path;
            if (!File.Exists(path)) return;
            try
            {
                var document = JsonUtility.FromJson<Document>(File.ReadAllText(path));
                if (document?.records != null)
                    foreach (var record in document.records) records[record.key] = record;
            }
            catch (Exception error) { Debug.LogWarning("Ignoring unreadable score file: " + error.Message); }
        }

        public static string Key(string chartKey, Difficulty difficulty) => chartKey + "/" + SongInfo.CourseName(difficulty);

        public Record Get(string chartKey, Difficulty difficulty)
            => records.TryGetValue(Key(chartKey, difficulty), out var record) ? record : null;

        // Fills PreviousBest, then keeps the higher score and the better crown. Auto play is not saved.
        public void Save(PlayResult result)
        {
            if (result.AutoPlay) return;
            string key = Key(result.ChartKey, result.Difficulty);
            records.TryGetValue(key, out var best);
            result.PreviousBest = best?.score ?? 0;
            if (best == null) records[key] = best = new Record { key = key, crown = Crown.None };
            if (result.Score > best.score || best.score == 0)
            {
                best.score = result.Score; best.good = result.Good; best.ok = result.Ok; best.bad = result.Bad;
                best.maxCombo = result.MaxCombo; best.rolls = result.Rolls;
            }
            if (result.StoredCrown > best.crown) best.crown = result.StoredCrown;
            Write();
        }

        void Write()
        {
            var document = new Document();
            document.records.AddRange(records.Values);
            document.records.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(document, true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }
    }
}
