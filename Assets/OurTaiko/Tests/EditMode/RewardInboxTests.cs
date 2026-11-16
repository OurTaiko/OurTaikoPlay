using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OurTaiko.Online;

namespace OurTaiko.Tests
{
    // RewardInbox without a server: what answers said is waiting, what it fetches, what it queues,
    // what it marks seen, and what a scene claims.
    public sealed class RewardInboxTests
    {
        // A player's rewards as a site keeps them; it answers at once.
        sealed class Site : IRewardSite
        {
            public readonly List<JObject> Waiting = new List<JObject>();
            public readonly List<long> Seen = new List<long>();
            public int Fetches;
            public bool Fail;

            public JObject Give(long id, string kind, JObject data)
            {
                var reward = new JObject { ["id"] = id, ["kind"] = kind, ["data"] = data };
                Waiting.Add(reward);
                return reward;
            }

            public Task<string> FetchAsync(CancellationToken cancel)
            {
                Fetches++;
                if (Fail) return Task.FromException<string>(new FanmadeException("NETWORK_ERROR"));
                return Task.FromResult(new JObject { ["rewards"] = new JArray(Waiting.Select(r => r.DeepClone())) }.ToString());
            }

            public Task MarkSeenAsync(IReadOnlyList<long> ids, CancellationToken cancel)
            {
                Seen.AddRange(ids);
                Waiting.RemoveAll(r => ids.Contains((long)r["id"]));
                return Task.CompletedTask;
            }
        }

        static JObject Title(string name, string style = "gold") => new JObject
        {
            ["title"] = new JObject { ["id"] = "t", ["name"] = name, ["style"] = style, ["nameTranslations"] = new JObject { ["zh-Hans"] = name + "（中文）" } },
            ["service"] = "rule",
        };

        static JObject Dan(int index, int verdict) => new JObject { ["index"] = index, ["verdict"] = verdict, ["gold"] = verdict >= 4, ["combo"] = (verdict - 1) % 3 };

        [Test]
        public void FetchesOnlyWhenSomethingWaitsAndQueuesIt()
        {
            var inbox = new RewardInbox();
            var site = new Site();
            site.Give(1, Reward.TitleKind, Title("はじめまして"));
            site.Give(2, Reward.DanKind, Dan(20, 5));

            inbox.Heard("site\nplayer", 0, 0, site);
            inbox.Pump();
            Assert.That(site.Fetches, Is.Zero, "Nothing waits: nothing is fetched.");

            inbox.Heard("site\nplayer", 0, 2, site);
            inbox.Pump();
            Assert.That(site.Fetches, Is.EqualTo(1));
            Assert.That(inbox.Queue.Select(r => r.Id), Is.EqualTo(new long[] { 1, 2 }));
            var title = inbox.Queue[0];
            Assert.That(title.TitleName("ja"), Is.EqualTo("はじめまして"));
            Assert.That(title.TitleName("zh"), Is.EqualTo("はじめまして（中文）"), "The song language zh is the title's zh-Hans.");
            Assert.That(title.TitleStyle, Is.EqualTo("gold"));
            Assert.That(title.Service, Is.EqualTo("rule"));
            var dan = inbox.Queue[1];
            Assert.That((dan.DanIndex, dan.DanGold, dan.DanCombo), Is.EqualTo((20, true, 1)));

            // While they are queued, more answers fetch nothing more.
            inbox.Heard("site\nplayer", 0, 2, site);
            inbox.Pump();
            Assert.That(site.Fetches, Is.EqualTo(1));
        }

        [Test]
        public void ShownRewardsAreMarkedSeenForTheirOwnerOnly()
        {
            var inbox = new RewardInbox();
            Site first = new Site(), second = new Site();
            first.Give(1, Reward.TitleKind, Title("one"));
            second.Give(7, Reward.TitleKind, Title("seven"));
            inbox.Heard("site\na", 0, 1, first);
            inbox.Heard("site\nb", 1, 1, second);
            inbox.Pump();
            Assert.That(inbox.Queue.Select(r => (r.Id, r.Seat)), Is.EqualTo(new[] { (1L, 0), (7L, 1) }));

            inbox.Shown(new[] { inbox.Queue[0] });
            Assert.That(first.Seen, Is.EqualTo(new long[] { 1 }));
            Assert.That(second.Seen, Is.Empty);
            Assert.That(inbox.Queue.Select(r => r.Id), Is.EqualTo(new long[] { 7 }));

            // The marking's own answer says nothing is left: nothing is fetched again.
            inbox.Heard("site\na", 0, 0, first);
            inbox.Pump();
            Assert.That(first.Fetches, Is.EqualTo(1));
        }

        [Test]
        public void AClaimTakesWhatAScreenShowsItself()
        {
            var inbox = new RewardInbox();
            var site = new Site();
            site.Give(1, Reward.DanKind, Dan(20, 4));
            site.Give(2, Reward.TitleKind, Title("十段の証"));

            // Claimed before it comes: it is marked seen and never queued; the title still is.
            inbox.Claim(r => r.Kind == Reward.DanKind && r.DanIndex == 20);
            inbox.Heard("site\nplayer", 0, 2, site);
            inbox.Pump();
            Assert.That(inbox.Queue.Select(r => r.Id), Is.EqualTo(new long[] { 2 }));
            Assert.That(site.Seen, Is.EqualTo(new long[] { 1 }));

            // Claimed after it came: it leaves the queue.
            var other = new RewardInbox();
            var later = new Site();
            later.Give(3, Reward.DanKind, Dan(21, 1));
            other.Heard("site\nplayer", 0, 1, later);
            other.Pump();
            Assert.That(other.Queue, Has.Count.EqualTo(1));
            other.Claim(r => r.Kind == Reward.DanKind && r.DanIndex == 21);
            Assert.That(other.Queue, Is.Empty);
            Assert.That(later.Seen, Is.EqualTo(new long[] { 3 }));

            // A claim on another rank takes nothing.
            var third = new RewardInbox();
            var rank = new Site();
            rank.Give(4, Reward.DanKind, Dan(19, 1));
            third.Claim(r => r.Kind == Reward.DanKind && r.DanIndex == 20);
            third.Heard("site\nplayer", 0, 1, rank);
            third.Pump();
            Assert.That(third.Queue.Select(r => r.Id), Is.EqualTo(new long[] { 4 }));
        }

        [Test]
        public void AFailedFetchIsTriedAgainOnTheNextAnswer()
        {
            var inbox = new RewardInbox();
            var site = new Site { Fail = true };
            site.Give(1, Reward.TitleKind, Title("one"));
            inbox.Heard("site\nplayer", 0, 1, site);
            inbox.Pump();
            Assert.That(inbox.Queue, Is.Empty);
            site.Fail = false;
            inbox.Heard("site\nplayer", 0, 1, site);
            inbox.Pump();
            Assert.That(site.Fetches, Is.EqualTo(2));
            Assert.That(inbox.Queue, Has.Count.EqualTo(1));
        }

        [Test]
        public void ARewardTheSiteDescribesBadlyIsLeftOut()
        {
            Assert.That(Reward.From(JObject.Parse("{\"kind\":\"title\"}")), Is.Null, "No id.");
            Assert.That(Reward.From(JObject.Parse("{\"id\":\"x\",\"kind\":\"title\"}")), Is.Null);
            var bare = Reward.From(JObject.Parse("{\"id\":3,\"kind\":\"dan\"}"));
            Assert.That((bare.DanIndex, bare.DanGold, bare.DanCombo, bare.TitleName("en")), Is.EqualTo((-1, false, 0, "")));
        }
    }
}
