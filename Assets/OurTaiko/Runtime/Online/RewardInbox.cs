using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OurTaiko.Online
{
    // One thing a player was given (OurTaikoNet's internal/rewards): a title, a 段位 badge, whatever
    // the site gives later. Kind names it; Data is the site's description of it, as it was when given.
    public sealed class Reward
    {
        public const string TitleKind = "title", DanKind = "dan";

        public long Id;
        public string Kind = "";
        public JObject Data = new JObject();
        // Whose it is: the seat it was heard for, and the player behind it (RewardInbox's owner key).
        public int Seat;
        public string Owner = "";

        // title: the title's name in a song language (en, ja, zh), else its own name.
        public string TitleName(string language)
        {
            string key = language == "zh" ? "zh-Hans" : language ?? "";
            var title = Data["title"] as JObject;
            var translated = title?["nameTranslations"]?[key];
            if (translated?.Type == JTokenType.String && ((string)translated).Length > 0) return (string)translated;
            return title?["name"]?.Type == JTokenType.String ? (string)title["name"] : "";
        }
        public string TitleStyle => Data["title"]?["style"]?.Type == JTokenType.String ? (string)Data["title"]["style"] : "";
        public string TitleDescription => Data["title"]?["description"]?.Type == JTokenType.String ? (string)Data["title"]["description"] : "";
        // What gave it: a 段位 course's name, or the site's service ("rule", "admin", a source).
        public string CourseTitle => Data["courseTitle"]?.Type == JTokenType.String ? (string)Data["courseTitle"] : "";
        public string Service => Data["service"]?.Type == JTokenType.String ? (string)Data["service"] : "";

        // dan: the badge as the emblem is drawn (PlayerInfo's dan, gold, DanComboTier); -1 without one.
        public int DanIndex => Data["index"]?.Type == JTokenType.Integer ? (int)Data["index"] : -1;
        public bool DanGold => Data["gold"]?.Type == JTokenType.Boolean && (bool)Data["gold"];
        public int DanCombo => Data["combo"]?.Type == JTokenType.Integer ? Math.Max(0, Math.Min(2, (int)Data["combo"])) : 0;

        public static Reward From(JToken value)
        {
            if (!(value is JObject o) || o["id"]?.Type != JTokenType.Integer || o["kind"]?.Type != JTokenType.String) return null;
            return new Reward { Id = (long)o["id"], Kind = (string)o["kind"], Data = o["data"] as JObject ?? new JObject() };
        }
    }

    // Where one player's rewards are fetched and marked seen: the site of a server, as a seat
    // signed in there (EndpointRewardSite). Tests give their own.
    public interface IRewardSite
    {
        Task<string> FetchAsync(CancellationToken cancel);
        Task MarkSeenAsync(IReadOnlyList<long> ids, CancellationToken cancel);
    }

    sealed class EndpointRewardSite : IRewardSite
    {
        public const string Path = "/api/v1/game/rewards";
        readonly FanmadeEndpoint endpoint;
        readonly int seat;
        public EndpointRewardSite(FanmadeEndpoint endpoint, int seat) { this.endpoint = endpoint; this.seat = seat; }
        public Task<string> FetchAsync(CancellationToken cancel) => endpoint.SiteRequestAsync(Path, cancel, endpoint.Seats[seat].Token);
        public Task MarkSeenAsync(IReadOnlyList<long> ids, CancellationToken cancel)
            => endpoint.SiteRequestAsync(Path + "/seen", cancel, endpoint.Seats[seat].Token, new JObject { ["ids"] = new JArray(ids) }.ToString(Newtonsoft.Json.Formatting.None));
    }

    // What the players at this game were given and have not been shown yet. Every answer of a site
    // says how many each player has waiting (FanmadeEndpoint.PendingRewardsHeard); the inbox fetches
    // a player's when there are some and none of theirs are queued already, keeps them for whoever
    // shows them (RewardOverlay), and marks them seen when that one says they were shown.
    //
    // It knows nothing of drawing or scenes. A scene that shows something itself (the 段位 result's
    // 昇段) claims it (Claim): what the claim matches is marked seen without being queued.
    //
    // Heard may come on any thread; everything else runs on the main thread (Pump from an Update).
    public sealed class RewardInbox
    {
        public static RewardInbox Shared { get; private set; } = new RewardInbox();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            FanmadeEndpoint.PendingRewardsHeard -= Listen;
            Shared = new RewardInbox();
            FanmadeEndpoint.PendingRewardsHeard += Listen;
        }

        // A site's player is one owner however many of the site's servers (sources) told of them.
        static void Listen(FanmadeEndpoint endpoint, int seat, int count)
        {
            string site = endpoint.Config.site;
            string user = endpoint.Seats[seat].UserId;
            if (site.Length == 0 || user.Length == 0) return;
            Shared.Heard(site + "\n" + user, seat, count, new EndpointRewardSite(endpoint, seat));
        }

        sealed class Owner
        {
            public int Seat, Waiting;
            public IRewardSite Site;
            public bool Fetching, Marking;
        }

        sealed class Claimed
        {
            public Func<Reward, bool> Match;
            public float Until;
        }

        readonly object gate = new object();
        readonly Dictionary<string, Owner> owners = new Dictionary<string, Owner>();
        readonly List<Reward> queue = new List<Reward>();
        readonly List<Claimed> claims = new List<Claimed>();

        // What is waiting to be shown, oldest first.
        public IReadOnlyList<Reward> Queue => queue;
        public event Action Changed;

        // An answer said an owner has count rewards waiting; site is where to fetch them.
        public void Heard(string owner, int seat, int count, IRewardSite site)
        {
            lock (gate)
            {
                if (!owners.TryGetValue(owner, out var o)) owners[owner] = o = new Owner();
                o.Seat = seat; o.Site = site; o.Waiting = Math.Max(0, count);
            }
        }

        // Fetches for every owner that has rewards waiting and none queued, fetched or being marked.
        public void Pump()
        {
            List<(string key, Owner owner)> due;
            lock (gate)
                due = owners.Where(p => p.Value.Waiting > 0 && !p.Value.Fetching && !p.Value.Marking && !queue.Any(r => r.Owner == p.Key))
                    .Select(p => (p.Key, p.Value)).ToList();
            foreach (var (key, owner) in due) _ = FetchAsync(key, owner);
        }

        async Task FetchAsync(string key, Owner owner)
        {
            IRewardSite site;
            int seat;
            lock (gate) { owner.Fetching = true; site = owner.Site; seat = owner.Seat; }
            try
            {
                string text = await site.FetchAsync(CancellationToken.None);
                var list = (JObject.Parse(text)["rewards"] as JArray ?? new JArray()).Select(Reward.From).Where(r => r != null).ToList();
                foreach (var r in list) { r.Seat = seat; r.Owner = key; }
                var claimed = list.Where(IsClaimed).ToList();
                lock (gate)
                {
                    owner.Waiting = 0;
                    queue.AddRange(list.Except(claimed).Where(r => !queue.Any(q => q.Owner == key && q.Id == r.Id)));
                }
                if (claimed.Count > 0) _ = MarkAsync(key, owner, claimed.Select(r => r.Id).ToList());
                if (list.Count > claimed.Count) Changed?.Invoke();
            }
            catch (Exception error)
            {
                // Told again by the next answer.
                Log.Info("Rewards", "Fetching failed: " + error.Message);
            }
            finally { lock (gate) owner.Fetching = false; }
        }

        // The overlay has shown these: they leave the queue and are marked seen.
        public void Shown(IEnumerable<Reward> shown)
        {
            var list = shown.ToList();
            lock (gate) queue.RemoveAll(r => list.Contains(r));
            foreach (var group in list.GroupBy(r => r.Owner))
            {
                Owner owner;
                lock (gate) owners.TryGetValue(group.Key, out owner);
                if (owner != null) _ = MarkAsync(group.Key, owner, group.Select(r => r.Id).ToList());
            }
            Changed?.Invoke();
        }

        async Task MarkAsync(string key, Owner owner, List<long> ids)
        {
            IRewardSite site;
            lock (gate) { owner.Marking = true; site = owner.Site; }
            try { await site.MarkSeenAsync(ids, CancellationToken.None); }
            catch (Exception error) { Log.Info("Rewards", "Marking seen failed (they come again): " + error.Message); }
            finally { lock (gate) owner.Marking = false; }
        }

        // Forgets everything: who has what waiting, the queue and the claims (a test's end).
        public void Clear()
        {
            lock (gate) { owners.Clear(); queue.Clear(); }
            claims.Clear();
            Changed?.Invoke();
        }

        // Something a scene shows by itself: rewards that match, queued now or fetched in the next
        // `seconds`, are marked seen and not shown.
        public void Claim(Func<Reward, bool> match, float seconds = 300)
        {
            claims.Add(new Claimed { Match = match, Until = Time.realtimeSinceStartup + seconds });
            List<Reward> taken;
            lock (gate) { taken = queue.Where(r => Safe(match, r)).ToList(); }
            if (taken.Count > 0) Shown(taken);
        }

        bool IsClaimed(Reward reward)
        {
            float now = Time.realtimeSinceStartup;
            claims.RemoveAll(c => c.Until < now);
            return claims.Any(c => Safe(c.Match, reward));
        }

        static bool Safe(Func<Reward, bool> match, Reward reward)
        {
            try { return match(reward); }
            catch (Exception) { return false; }
        }
    }
}
