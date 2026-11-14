using System;
using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using OurTaiko.Multi;
using OurTaiko.Multi.Proto;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // マルチ演奏 over real gRPC, against an OurTaikoNet running on this machine: the one test that
    // goes through MultiTransport's HTTP/2 handler and the server's own room. It is skipped unless
    // the environment names the server and a game session:
    //
    //   OURTAIKO_MULTIPLAY_URL    e.g. http://127.0.0.1:50061 (see OurTaikoNet's README, "Local
    //                             development"; start it with MULTIPLAY_MIN_PLAYERS=1)
    //   OURTAIKO_MULTIPLAY_TOKEN  the development game session (go run ./tools/multiplay-smoke -do token)
    //
    // The player is alone in a private room and goes through a whole round by the protocol; no
    // song is downloaded or played.
    public sealed class MultiLiveServerTests
    {
        static IEnumerator Until(Func<bool> condition, string what, float seconds = 15)
        {
            yield return Waits.Until(condition, () => what + " (room: " + (MultiSession.Room?.Phase.ToString() ?? "none") + ")", seconds);
        }

        [UnityTest]
        public IEnumerator AWholeRoundAgainstALocalServer()
        {
            string url = Environment.GetEnvironmentVariable(MultiSession.ServerEnvironmentVariable);
            string token = Environment.GetEnvironmentVariable("OURTAIKO_MULTIPLAY_TOKEN");
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(token)) Assert.Ignore("No local room server is named (OURTAIKO_MULTIPLAY_URL, OURTAIKO_MULTIPLAY_TOKEN).");
            MultiSession.Forget();
            MultiTransport.Open = MultiTransport.OpenGrpc;
            MultiSession.ResolveAddress = MultiSession.ResolveSiteAddressAsync;
            MultiSession.GameSession = (renew, _) => Task.FromResult(token.Trim());
            int started = 0;
            Action<int> onStart = round => started = round;
            MultiSession.PlayStarted += onStart;
            try
            {
                var join = MultiSession.CreatePrivateAsync();
                yield return Waits.Until(() => join.IsCompleted, seconds: 30);
                Assert.That(join.IsCompletedSuccessfully, Is.True, join.Exception?.GetBaseException().ToString());
                var room = MultiSession.Room;
                Assert.That(room.Code, Has.Length.EqualTo(5));
                Assert.That(room.Phase, Is.EqualTo(Phase.Lobby));
                Assert.That(MultiSession.IsHost, Is.True);
                Assert.That(MultiSession.Me.UserId, Is.EqualTo(MultiSession.MyUserId).And.Not.Empty);
                Assert.That(room.MinPlayers, Is.EqualTo(1), "Start the server with MULTIPLAY_MIN_PLAYERS=1 for this test.");

                MultiSession.SendStamp(2);
                MultiSession.LockMembers();
                yield return Until(() => MultiSession.Room.Phase == Phase.SongSelect, "The room did not ask for songs");
                Assert.That(MultiSession.RemainingMs, Is.InRange(1, 120000));

                // おまかせ: the server draws any song and sends it with its chart.
                MultiSession.PickSong("", "", "Oni");
                yield return Until(() => MultiSession.Room.Phase == Phase.Course, "The room did not reach the courses", 30);
                var song = MultiSession.Room.Song;
                Assert.That(song.Source, Is.EqualTo("fanmade").Or.EqualTo("official"));
                Assert.That(song.ChartId, Has.Length.EqualTo(32));
                Assert.That(song.Courses, Is.Not.Empty);
                Assert.That(song.ChartJson.ToStringUtf8(), Does.Contain(song.ChartId), "The chart comes with the song.");
                Assert.That(MultiSession.Me.Course, Is.Not.Empty);

                MultiSession.SetReady(true);
                yield return Until(() => MultiSession.Room.Phase == Phase.Loading && MultiSession.Me.Playing, "The room did not start loading");
                MultiSession.Loaded(true);
                yield return Until(() => started == MultiSession.Room.Round && MultiSession.Room.Phase == Phase.Playing, "The room did not start the round");
                Assert.That(MultiSession.StartedRound, Is.EqualTo(1));

                MultiSession.SendStatus(new PlayerStatus { Score = 4560, Combo = 12, MaxCombo = 12, Good = 10, Ok = 2, Gauge = 1200, MusicTimeMs = 9000 });
                yield return Until(() => MultiSession.Statuses != null && MultiSession.Statuses.Members.Count == 1 && MultiSession.Statuses.Members[0].Status.Score == 4560,
                    "The score did not come back");
                MultiSession.Finish(new PlayerStatus { Score = 987650, MaxCombo = 300, Good = 290, Ok = 10, Gauge = 10000, MusicTimeMs = 120000 }, true);
                yield return Until(() => MultiSession.LastResult != null, "No result came");
                var result = MultiSession.LastResult;
                Assert.That(result.Round, Is.EqualTo(1));
                Assert.That(result.TeamScore, Is.EqualTo(987650));
                Assert.That(result.Players, Has.Count.EqualTo(1));
                Assert.That(result.Players[0].Rank, Is.EqualTo(1));
                Assert.That(result.Players[0].FullCombo, Is.True);
                Assert.That(result.Players[0].AllGood, Is.False);
                Assert.That(result.Players[0].Cleared, Is.True);
                Assert.That(result.Title, Is.EqualTo(song.Title));

                var leave = MultiSession.LeaveAsync();
                yield return Waits.Until(() => leave.IsCompleted, seconds: 10);
                Assert.That(MultiSession.InRoom, Is.False);
                Assert.That(MultiSession.IsConnected, Is.False);
            }
            finally
            {
                MultiSession.PlayStarted -= onStart;
                MultiSession.GameSession = MultiSession.SiteSessionAsync;
                _ = MultiSession.LeaveAsync();
            }
        }
    }
}
