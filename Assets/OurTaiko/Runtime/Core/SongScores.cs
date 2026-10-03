using System;

namespace OurTaiko
{
    // Server bests are session data loaded at login, never a fallback to the local database.
    public static class SongScores
    {
        public static bool IsOnlineKey(string key) => key != null && key.StartsWith("fanmade/", StringComparison.Ordinal);
        public static bool IsOnline(SongDefinition song) => song != null &&
            (IsOnlineKey(song.name) || (Online.OnlineManager.Instance != null && Online.OnlineManager.Instance.IsOnline(song)));

        public static ScoreStore.Record Get(SongDefinition song, Difficulty difficulty)
        {
            if (song == null || difficulty < Difficulty.Easy || difficulty > Difficulty.Ura) return null;
            if (!IsOnline(song)) return ScoreStore.Shared.Get(song.name, difficulty);
            var manager = Online.OnlineManager.Instance;
            var best = manager?.Client.Best(manager.ChartOf(song), (int)difficulty);
            if (best == null) return null;
            // The API has no clear/gauge flag. Only FC/DFC can be established from judgments.
            var crown = best.Good + best.Ok + best.Bad == 0 || best.Bad > 0 ? Crown.None
                : best.Ok > 0 ? Crown.FullCombo : Crown.DonderfulCombo;
            return new ScoreStore.Record { key = ScoreStore.Key(song.name, difficulty), score = Clamp(best.Score),
                good = Clamp(best.Good), ok = Clamp(best.Ok), bad = Clamp(best.Bad), maxCombo = Clamp(best.MaxCombo),
                rolls = Clamp(best.Drumroll), crown = crown };
        }

        static int Clamp(long value) => (int)Math.Clamp(value, 0, int.MaxValue);
    }
}
