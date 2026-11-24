using System;
using System.Collections.Generic;
using SQLite;
using UnityEngine;

namespace OurTaiko
{
    // Only local charts belong here. Connections are short-lived; the song wheel reads the cache.
    public sealed class ScoreStore
    {
        [UnityEngine.Scripting.Preserve, Table("BestScores")]
        public sealed class Record
        {
            [PrimaryKey] public string key { get; set; }
            [Column("score")] public int score { get; set; }
            [Column("good")] public int good { get; set; }
            [Column("ok")] public int ok { get; set; }
            [Column("bad")] public int bad { get; set; }
            [Column("maxCombo")] public int maxCombo { get; set; }
            [Column("rolls")] public int rolls { get; set; }
            [Column("crown")] public Crown crown { get; set; }
            // Added later: GameDatabase's first step (sqlite-net's CreateTable) adds the columns to an older file, rows reading 0.
            [Column("adLibGood")] public int adLibGood { get; set; }
            [Column("adLibOk")] public int adLibOk { get; set; }
            [Column("mines")] public int mines { get; set; }
        }

        static ScoreStore shared;
        readonly GameDatabase database;
        readonly Dictionary<string, Record> records = new Dictionary<string, Record>();
        public int Revision { get; private set; }
        public static ScoreStore Shared
        {
            get => shared ??= new ScoreStore(GameDatabase.DefaultPath);
            set => shared = value;
        }

        public ScoreStore(string path) : this(new GameDatabase(path)) { }

        public ScoreStore(GameDatabase database)
        {
            this.database = database;
            using var db = database.Open();
            foreach (var record in db.Table<Record>()) records[record.key] = record;
        }

        public static string Key(string chartKey, Difficulty difficulty) => chartKey + "/" + SongInfo.CourseName(difficulty);
        public Record Get(string chartKey, Difficulty difficulty)
            => !SongScores.IsOnlineKey(chartKey) && records.TryGetValue(Key(chartKey, difficulty), out var record) ? record : null;

        public void Save(PlayResult result)
        {
            if (!result.IsRecordable || SongScores.IsOnlineKey(result.ChartKey)) return;
            string key = Key(result.ChartKey, result.Difficulty);
            records.TryGetValue(key, out var best);
            result.PreviousBest = best?.score ?? 0;
            bool higher = best == null || result.Score > best.score;
            var next = new Record
            {
                key = key, score = higher ? result.Score : best.score, good = higher ? result.Good : best.good,
                ok = higher ? result.Ok : best.ok, bad = higher ? result.Bad : best.bad,
                maxCombo = higher ? result.MaxCombo : best.maxCombo, rolls = higher ? result.Rolls : best.rolls,
                adLibGood = higher ? result.AdLibGood : best.adLibGood, adLibOk = higher ? result.AdLibOk : best.adLibOk,
                mines = higher ? result.Mines : best.mines,
                crown = (Crown)Math.Max((int)(best?.crown ?? Crown.None), (int)result.StoredCrown),
            };
            using (var db = database.Open()) db.InsertOrReplace(next);
            records[key] = next;
            Revision++;
        }
    }
}
