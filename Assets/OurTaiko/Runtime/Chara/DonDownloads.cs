using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OurTaiko.Chara
{
    // Each signed-in seat's Don as the player chose it on the website (net.ourtaiko.org,
    // GET /api/v2/users/{id}/don): the look, and its files (costume, or head and body, the
    // puchichara and the makeup), each named by its path under the distribution root
    // ("3dCharaPath"), downloaded from the player's root (or, when that fails, the canonical one;
    // Online.Distribution) into the shared object cache (objects/<sha256>, checked by size and
    // hash). The game itself carries only the plain Don (costume 0); a guest, or a seat whose look
    // cannot be had, wears it. The last look of each account is remembered with its files' hashes
    // (don-looks.json in the cache), so a player offline keeps theirs; a look remembered from
    // version 1 of the API (files by "url") is read by its hashes alone, the same.
    public static class DonDownloads
    {
        public const string StateFile = "don-looks.json";

        sealed class Seat
        {
            public string UserId = "";
            public DonLook Look;
            public bool Chosen; // false: the seat wears the default look
        }

        static readonly Seat[] seats = { new Seat(), new Seat() };

        // Each Play session starts with no looks, as a player does (the Editor here keeps statics
        // between Play sessions, and DonLibrary forgets the downloaded files then too); the sign-in
        // fetches them again.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            seats[0] = new Seat();
            seats[1] = new Seat();
            Received = 0;
            Expected = 0;
        }
        static readonly object gate = new object();

        // A seat's look changed (signed in, signed out, downloaded): its Dons redress.
        public static event Action<int> Changed;

        // Bytes received and expected of the download running now (0, 0 when none).
        public static long Received { get; private set; }
        public static long Expected { get; private set; }
        public static bool Downloading => Expected > 0;

        public static DonLook For(int seat)
        {
            seat = Math.Clamp(seat, 0, 1);
            var s = seats[seat];
            return s.Chosen ? s.Look : DonLook.Default(seat);
        }

        // The seat signed out (or was never in): the plain Don.
        public static void Forget(int seat)
        {
            seat = Math.Clamp(seat, 0, 1);
            if (!seats[seat].Chosen && seats[seat].UserId.Length == 0) return;
            seats[seat] = new Seat();
            Changed?.Invoke(seat);
        }

        // Reads the seat's look from the site and downloads what it needs, reporting progress; on
        // failure the account's last remembered look is worn when its files are cached, else the
        // plain Don. Never throws but for cancellation.
        public static async Task FetchAsync(int seat, string userId, string siteBase, string cacheRoot, CancellationToken cancel = default)
        {
            seat = Math.Clamp(seat, 0, 1);
            JObject answer = null;
            try
            {
                using var response = await Online.OurTaikoHttpClient.GetAsync(siteBase.TrimEnd('/') + "/api/v2/users/" + Uri.EscapeDataString(userId) + "/don",
                    TimeSpan.FromSeconds(30), cancel);
                if (!response.IsSuccessStatusCode) throw new HttpRequestException("HTTP " + (int)response.StatusCode);
                answer = JObject.Parse(await response.Content.ReadAsStringAsync());
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
            catch (Exception error) { Log.Warn("Don", "Could not read the Don of " + userId + ": " + error.Message); }

            if (answer != null)
            {
                try
                {
                    var look = LookFrom((JObject)answer["look"], seat);
                    var files = new List<(string kind, int id, string path, long size, string hash)>();
                    foreach (JObject f in (JArray)answer["files"] ?? new JArray())
                        files.Add(((string)f["kind"], (int)f["id"], (string)f["3dCharaPath"], (long)f["size"], (string)f["sha256"]));
                    await DownloadAsync(files, siteBase, cacheRoot, cancel);
                    foreach (var f in files) DonLibrary.UseFile(f.kind, f.id, Online.ObjectCache.PathOf(cacheRoot, f.hash));
                    Remember(cacheRoot, userId, answer);
                    Wear(seat, userId, look);
                    return;
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
                catch (Exception error) { Log.Warn("Don", "Could not download the Don of " + userId + ": " + error.Message); }
            }
            // Offline, or the download failed: the last look this account had, if its files are here.
            var remembered = Recall(cacheRoot, userId);
            if (remembered != null && TryUseCached(remembered, cacheRoot))
                Wear(seat, userId, LookFrom((JObject)remembered["look"], seat));
            else Wear(seat, userId, DonLook.Default(seat), chosen: false);
        }

        static void Wear(int seat, string userId, DonLook look, bool chosen = true)
        {
            seats[seat] = new Seat { UserId = userId, Look = look, Chosen = chosen };
            Changed?.Invoke(seat);
        }

        // The server's look: colours "#RRGGBB" or null (the seat's), the face of the sheet 0.
        public static DonLook LookFrom(JObject look, int seat)
        {
            var result = DonLook.Default(seat);
            if (look == null) return result;
            result.costume = (bool?)look["costume"] ?? true;
            result.cos = (int?)look["cos"] ?? 0;
            result.head = (int?)look["head"] ?? 0;
            result.body = (int?)look["body"] ?? 0;
            result.puchi = (int?)look["puchi"] ?? 0;
            result.paint = (int?)look["paint"] ?? 0;
            if (look["colors"] is JArray colors && colors.Count == 3)
            {
                Color32[] parsed = new Color32[3];
                for (int i = 0; i < 3; i++)
                {
                    if (!ColorUtility.TryParseHtmlString((string)colors[i], out var c)) return result;
                    parsed[i] = c;
                }
                result.color1 = parsed[0]; result.color2 = parsed[1]; result.color3 = parsed[2];
            }
            return result;
        }

        // The files the cache lacks, from the root the site names (asked only when one is missing).
        static async Task DownloadAsync(List<(string kind, int id, string path, long size, string hash)> files, string siteBase, string cacheRoot, CancellationToken cancel)
        {
            var lacking = new List<(string path, long size, string hash)>();
            foreach (var f in files)
            {
                if (f.hash == null || f.hash.Length != 64) throw new InvalidDataException("bad hash for " + f.kind + " " + f.id);
                if (!Online.Distribution.ValidPath(f.path)) throw new InvalidDataException("bad path for " + f.kind + " " + f.id);
                string path = Online.ObjectCache.PathOf(cacheRoot, f.hash);
                if (!await Task.Run(() => Online.ObjectCache.Matches(path, f.size, f.hash), cancel)) lacking.Add((f.path, f.size, f.hash));
            }
            if (lacking.Count == 0) return;
            var root = await Online.Distribution.RootAsync(siteBase, cancel);
            var missing = lacking.Select(f => (address: root.Locate(f.path), f.size, f.hash)).ToList();
            long total = 0;
            foreach (var m in missing) total += m.size;
            Received = 0; Expected = total;
            try
            {
                foreach (var (address, size, hash) in missing)
                {
                    long before = Received;
                    var data = await Online.Distribution.FetchAsync(address, url => Online.ObjectCache.FetchAsync(url, size, hash, got => Received = before + got, cancel), cancel);
                    await Task.Run(() => Online.ObjectCache.Store(Online.ObjectCache.PathOf(cacheRoot, hash), data), cancel);
                    Received = before + size;
                }
            }
            finally { Received = 0; Expected = 0; }
        }

        // ---- the remembered looks: {userId: <the site's answer>} ----

        static JObject ReadState(string cacheRoot)
        {
            try
            {
                string path = Path.Combine(cacheRoot, StateFile);
                return File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();
            }
            catch (Exception error)
            {
                Log.Warn("Don", "Remembered looks unreadable: " + error.Message);
                return new JObject();
            }
        }

        static void Remember(string cacheRoot, string userId, JObject answer)
        {
            lock (gate)
            {
                try
                {
                    var state = ReadState(cacheRoot);
                    state[userId] = answer;
                    Directory.CreateDirectory(cacheRoot);
                    File.WriteAllText(Path.Combine(cacheRoot, StateFile), state.ToString(Newtonsoft.Json.Formatting.None));
                }
                catch (Exception error) { Log.Warn("Don", "Could not remember the look: " + error.Message); }
            }
        }

        static JObject Recall(string cacheRoot, string userId) => ReadState(cacheRoot)[userId] as JObject;

        static bool TryUseCached(JObject answer, string cacheRoot)
        {
            var files = (JArray)answer["files"] ?? new JArray();
            foreach (JObject f in files)
            {
                string hash = (string)f["sha256"];
                if (hash == null || hash.Length != 64 || !Online.ObjectCache.Matches(Online.ObjectCache.PathOf(cacheRoot, hash), (long?)f["size"] ?? -1, hash)) return false;
            }
            foreach (JObject f in files) DonLibrary.UseFile((string)f["kind"], (int)f["id"], Online.ObjectCache.PathOf(cacheRoot, (string)f["sha256"]));
            return true;
        }
    }
}
