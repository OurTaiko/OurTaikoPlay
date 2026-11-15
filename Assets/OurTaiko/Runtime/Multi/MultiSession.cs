using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OurTaiko.Multi.Proto;
using OurTaiko.Online;
using UnityEngine;

namespace OurTaiko.Multi
{
    // The player's connection to a マルチ演奏 room. It outlives scenes: the room screen opens it, the
    // play scene keeps using it during the song, and the room screen finds it again afterwards.
    // Everything here runs on the main thread (the calls are made from it and their awaits come
    // back to it), and so does every event.
    public static class MultiSession
    {
        // The newest version of the protocol this game speaks. 2 (2026-11-14): after a play the
        // game says when the player is back at the room's screen (`returned`), and the room
        // shows who is not (Member.Away): they are not in the room, and are left behind when
        // the host starts the next round (closed LEFT_BEHIND).
        public const int Protocol = 2;
        public const int StampCount = 16;
        // Development override of the room server, e.g. http://127.0.0.1:50061 (plain h2c).
        public const string ServerEnvironmentVariable = "OURTAIKO_MULTIPLAY_URL";

        const int ReconnectAttempts = 3;
        const double JoinSeconds = 20, PingSeconds = 15;

        static IMultiLink link;
        static CancellationTokenSource cancel;
        static readonly Queue<ClientMessage> outbox = new Queue<ClientMessage>();
        static bool writing, leaving;
        static int generation;
        static string address;

        // The room as the server last described it; null when not in a room.
        public static Room Room { get; private set; }
        public static string MyUserId { get; private set; } = "";
        public static bool IsConnected => link != null;
        public static bool InRoom => Room != null;
        // Server clock minus local clock, from the last room snapshot (ms).
        public static long ClockOffsetMs { get; private set; }
        // The latest statuses of the players in the song.
        public static PlayStatuses Statuses { get; private set; }
        // The newest result the room has sent; kept after the session ends.
        public static Proto.PlayResult LastResult { get; private set; }
        // The round the server last started (start_play), and the round this player last loaded.
        public static int StartedRound { get; private set; }
        public static int LaunchedRound { get; internal set; }
        // The round whose result the room screen has shown.
        public static int ShownResultRound { get; set; }
        // The version to join with: this game's, or the server's newest when that is older.
        public static int JoinProtocol { get; set; } = Protocol;
        // Why the last session ended, for the room screen to say once (null: nothing to say).
        public static string ClosedCode { get; set; }

        public static event Action<Room> RoomChanged;
        public static event Action<int> PlayStarted;
        public static event Action<PlayStatuses> StatusesChanged;
        public static event Action<StampSent> StampReceived;
        // A notice's code (NOT_HOST, RECONNECTED, …).
        public static event Action<string> NoticeReceived;
        // The session ended: left, kicked, the room closed, or the connection was lost for good.
        public static event Action<string> Closed;

        // Where the room server is, and the game session the stream signs in with (true: trade for
        // a new one first). The defaults ask the site and use the leading player's account; tests
        // replace them.
        public static Func<CancellationToken, Task<string>> ResolveAddress { get; set; } = ResolveSiteAddressAsync;
        public static Func<bool, CancellationToken, Task<string>> GameSession { get; set; } = SiteSessionAsync;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            CloseLink();
            generation++;
            leaving = false; address = null;
            Room = null; Statuses = null; LastResult = null;
            MyUserId = ""; ClosedCode = null;
            StartedRound = LaunchedRound = ShownResultRound = 0;
            RoomChanged = null; PlayStarted = null; StatusesChanged = null; StampReceived = null; NoticeReceived = null; Closed = null;
            ResolveAddress = ResolveSiteAddressAsync; GameSession = SiteSessionAsync;
            JoinProtocol = Protocol;
        }

        // Nothing of an earlier room is kept: its last result, what was said about its end, and
        // where its server was (asked for again on the next join).
        public static void Forget()
        {
            if (IsConnected) return;
            LastResult = null; ClosedCode = null; address = null;
            StartedRound = LaunchedRound = ShownResultRound = 0;
        }

        public static Member Me => Find(MyUserId);

        public static Member Find(string userId)
        {
            if (Room == null || string.IsNullOrEmpty(userId)) return null;
            foreach (var member in Room.Members) if (member.UserId == userId) return member;
            return null;
        }

        public static bool IsHost => Room != null && Room.HostId == MyUserId;

        // Milliseconds left in the current phase, or -1 when it has no deadline.
        public static long RemainingMs
        {
            get
            {
                if (Room == null || Room.DeadlineMs <= 0) return -1;
                long serverNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ClockOffsetMs;
                return Math.Max(0, Room.DeadlineMs - serverNow);
            }
        }

        // The built-in servers that are sources of the site: the site's sign-in is theirs.
        static List<FanmadeEndpoint> SiteEndpoints()
        {
            var client = OnlineManager.Instance != null ? OnlineManager.Instance.Client : null;
            return client == null ? new List<FanmadeEndpoint>() : client.Endpoints.Where(e => e.Config.site.Length > 0).ToList();
        }

        // The site's source a server is ("fanmade", "official"), and the server of a source.
        public static string SourceOf(string server)
            => SiteEndpoints().FirstOrDefault(e => e.Id == server)?.Config.SourceId ?? "";
        public static FanmadeEndpoint EndpointOf(string source)
            => string.IsNullOrEmpty(source) ? null : SiteEndpoints().FirstOrDefault(e => e.Config.SourceId == source);

        // Whether the leading player is signed in to the site (rooms need an account).
        public static bool IsSignedIn
        {
            get
            {
                int seat = SceneSwitcher.EnsureInstance().Lead.Index();
                return SiteEndpoints().Any(e => e.Seats[seat].IsAuthenticated);
            }
        }

        public static async Task<string> SiteSessionAsync(bool renew, CancellationToken cancel)
        {
            int index = SceneSwitcher.EnsureInstance().Lead.Index();
            var site = SiteEndpoints();
            var seat = site.Select(e => e.Seats[index]).FirstOrDefault(s => s.IsAuthenticated);
            if (seat == null) throw new MultiException("SIGN_IN_REQUIRED");
            if (renew)
            {
                if (seat.Renew == null) throw new MultiException("AUTH_REQUIRED");
                string renewed;
                try { renewed = await seat.Renew(cancel); }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { throw new MultiException("AUTH_REQUIRED", error); }
                if (string.IsNullOrEmpty(renewed)) throw new MultiException("AUTH_REQUIRED");
                // One session is good for every source of the site.
                foreach (var e in site) if (e.Seats[index].IsAuthenticated) e.Seats[index].Token = renewed;
            }
            return seat.Token;
        }

        // The environment override, else what the site says (GET /api/v1/game/multiplay).
        public static async Task<string> ResolveSiteAddressAsync(CancellationToken cancel)
        {
            JoinProtocol = Protocol;
            string fromEnvironment = Environment.GetEnvironmentVariable(ServerEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment.Trim().TrimEnd('/');
            var endpoint = SiteEndpoints().FirstOrDefault() ?? throw new MultiException("NOT_AVAILABLE");
            Newtonsoft.Json.Linq.JObject reply;
            try { reply = Json.Parse(await endpoint.SiteRequestAsync("/api/v1/game/multiplay", cancel)); }
            catch (HttpStatusException error) when (error.Status == 404) { throw new MultiException("NOT_AVAILABLE", error); }
            catch (OperationCanceledException) { throw; }
            catch (FanmadeException error) { throw new MultiException("SERVER_UNREACHABLE", error); }
            string url = reply["url"]?.Type == Newtonsoft.Json.Linq.JTokenType.String ? ((string)reply["url"]).Trim().TrimEnd('/') : "";
            if (url.Length == 0) throw new MultiException("NOT_AVAILABLE");
            // protocol is the oldest version a game may speak, maxProtocol the newest the server
            // does (a server from before version 2 names only the one it speaks).
            int oldest = reply["protocol"]?.Type == Newtonsoft.Json.Linq.JTokenType.Integer ? (int)reply["protocol"] : 1;
            if (oldest > Protocol) throw new MultiException("UPDATE_REQUIRED");
            int newest = reply["maxProtocol"]?.Type == Newtonsoft.Json.Linq.JTokenType.Integer ? (int)reply["maxProtocol"] : oldest;
            JoinProtocol = Math.Max(1, Math.Min(Protocol, newest));
            return url;
        }

        // A waiting public room, or a new one.
        public static Task QuickMatchAsync(CancellationToken cancel = default)
            => ConnectAsync(new Join { QuickMatch = new QuickMatch() }, cancel);

        // A new private room (5-digit number).
        public static Task CreatePrivateAsync(CancellationToken cancel = default)
            => ConnectAsync(new Join { CreatePrivate = new CreatePrivate() }, cancel);

        public static Task JoinAsync(string roomCode, CancellationToken cancel = default)
            => ConnectAsync(new Join { RoomCode = roomCode }, cancel);

        // Opens the stream and joins; returns once the server has put the player in a room, and
        // throws a MultiException when it did not.
        public static async Task ConnectAsync(Join join, CancellationToken cancel = default)
        {
            if (IsConnected) throw new MultiException("ALREADY_IN_ROOM");
            // Another room counts its rounds from one again.
            StartedRound = LaunchedRound = ShownResultRound = 0;
            LastResult = null;
            address ??= await ResolveAddress(cancel);
            join.Protocol = JoinProtocol;
            leaving = false;
            ClosedCode = null;
            try { await OpenAsync(join, false, cancel); }
            // The saved game session may have been revoked: trade for a new one once.
            catch (MultiException error) when (error.Code == "AUTH_REQUIRED") { await OpenAsync(join, true, cancel); }
        }

        static async Task OpenAsync(Join join, bool renew, CancellationToken caller)
        {
            string session = await GameSession(renew, caller);
            if (string.IsNullOrEmpty(session)) throw new MultiException("SIGN_IN_REQUIRED");
            CloseLink();
            int mine = ++generation;
            cancel = new CancellationTokenSource();
            try { link = MultiTransport.Open(address, session, cancel.Token); }
            catch (Exception error) when (!(error is MultiException)) { CloseLink(); throw new MultiException("CONNECTION_LOST", error); }
            Send(new ClientMessage { Join = join });

            // The first answers are welcome and the room (or closed, or an error status).
            var joined = new TaskCompletionSource<bool>();
            _ = ReadLoopAsync(mine, joined);
            using (caller.Register(() => joined.TrySetCanceled()))
            {
                var first = await Task.WhenAny(joined.Task, Task.Delay(TimeSpan.FromSeconds(JoinSeconds)));
                if (first != joined.Task)
                {
                    if (mine == generation) { generation++; CloseLink(); }
                    throw new MultiException("TIMEOUT");
                }
                try { await joined.Task; }
                catch
                {
                    if (mine == generation) { generation++; CloseLink(); }
                    throw;
                }
            }
            _ = PingLoopAsync(mine);
        }

        static async Task ReadLoopAsync(int mine, TaskCompletionSource<bool> joined)
        {
            var reading = link;
            var token = cancel.Token;
            Exception failure = null;
            try
            {
                while (true)
                {
                    var message = await reading.ReceiveAsync(token);
                    if (mine != generation) return;
                    if (message == null) break;
                    if (message.KindCase == ServerMessage.KindOneofCase.Closed)
                    {
                        // Refused while joining (or joining again): whoever asked reports it.
                        if (joined.TrySetException(new MultiException(message.Closed.Code))) { generation++; CloseLink(); }
                        else EndSession(message.Closed.Code);
                        return;
                    }
                    Dispatch(message);
                    if (message.KindCase == ServerMessage.KindOneofCase.Room) joined.TrySetResult(true);
                }
            }
            catch (Exception error) { failure = error; }

            if (mine != generation) return;
            // Failed while joining: ConnectAsync reports it.
            if (joined.TrySetException(failure is MultiException ? failure : new MultiException("CONNECTION_LOST", failure))) return;
            if (leaving) { EndSession("LEFT"); return; }
            Log.Warn("Multi", "Room connection lost: " + (failure?.Message ?? "the stream ended"));
            await ReconnectAsync();
        }

        // The stream dropped: the server keeps the player's place for 10 s, so join the room again.
        static async Task ReconnectAsync()
        {
            string code = Room?.Code;
            CloseLink();
            int mine = generation;
            for (int attempt = 1; attempt <= ReconnectAttempts && !string.IsNullOrEmpty(code) && !leaving; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt == 1 ? 0.5 : 2));
                if (mine != generation || leaving) return;
                try
                {
                    await OpenAsync(new Join { RoomCode = code, Protocol = JoinProtocol }, false, CancellationToken.None);
                    NoticeReceived?.Invoke("RECONNECTED");
                    return;
                }
                catch (MultiException error) when (error.Code == "ROOM_NOT_FOUND" || error.Code == "ROOM_BUSY" || error.Code == "ROOM_CLOSED" || error.Code == "ROOM_FULL")
                {
                    break;
                }
                catch (Exception error) { Log.Warn("Multi", "Reconnect " + attempt + " failed: " + error.Message); }
                mine = generation;
            }
            EndSession("CONNECTION_LOST");
        }

        static async Task PingLoopAsync(int mine)
        {
            while (mine == generation && IsConnected)
            {
                await Task.Delay(TimeSpan.FromSeconds(PingSeconds));
                if (mine != generation || !IsConnected) return;
                Send(new ClientMessage { Ping = new Proto.Ping { ClientTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() } });
            }
        }

        static void Dispatch(ServerMessage message)
        {
            switch (message.KindCase)
            {
                case ServerMessage.KindOneofCase.Welcome:
                    MyUserId = message.Welcome.UserId;
                    break;
                case ServerMessage.KindOneofCase.Room:
                    Room = message.Room;
                    if (Room.Result != null && Room.Result.Round > 0) LastResult = Room.Result;
                    ClockOffsetMs = Room.ServerTimeMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    RoomChanged?.Invoke(Room);
                    break;
                case ServerMessage.KindOneofCase.StartPlay:
                    StartedRound = message.StartPlay.Round;
                    PlayStarted?.Invoke(message.StartPlay.Round);
                    break;
                case ServerMessage.KindOneofCase.Statuses:
                    Statuses = message.Statuses;
                    StatusesChanged?.Invoke(Statuses);
                    break;
                case ServerMessage.KindOneofCase.Stamp:
                    StampReceived?.Invoke(message.Stamp);
                    break;
                case ServerMessage.KindOneofCase.Notice:
                    NoticeReceived?.Invoke(message.Notice.Code);
                    break;
            }
        }

        // Queues a message; they are written one at a time, in order.
        public static void Send(ClientMessage message)
        {
            if (link == null) return;
            outbox.Enqueue(message);
            if (!writing) _ = WriteLoopAsync(generation);
        }

        static async Task WriteLoopAsync(int mine)
        {
            writing = true;
            try
            {
                while (outbox.Count > 0 && mine == generation && link != null)
                    await link.SendAsync(outbox.Dequeue());
            }
            catch (Exception error)
            {
                // The read loop notices the broken stream and reconnects.
                Log.Warn("Multi", "Send failed: " + error.Message);
                if (mine == generation) outbox.Clear();
            }
            finally { if (mine == generation) writing = false; }
        }

        public static void LockMembers() => Send(new ClientMessage { LockMembers = new LockMembers() });
        public static void OpenRoom() => Send(new ClientMessage { OpenRoom = new OpenRoom() });
        public static void Kick(string userId) => Send(new ClientMessage { Kick = new Kick { UserId = userId } });
        // source and chartId empty: おまかせ. course is the one the player would play either way.
        public static void PickSong(string source, string chartId, string course)
            => Send(new ClientMessage { PickSong = new PickSong { Source = source ?? "", ChartId = chartId ?? "", Course = course ?? "" } });
        public static void PickCourse(string course) => Send(new ClientMessage { PickCourse = new PickCourse { Course = course } });
        public static void SetReady(bool ready) => Send(new ClientMessage { SetReady = new SetReady { Ready = ready } });
        public static void Loaded(bool ok, string error = "") => Send(new ClientMessage { Loaded = new Loaded { Ok = ok, Error = error ?? "" } });
        public static void SendStatus(PlayerStatus status) => Send(new ClientMessage { Status = status });
        public static void Finish(PlayerStatus final, bool cleared) => Send(new ClientMessage { Finish = new FinishPlay { Final = final, Cleared = cleared } });
        public static void SendStamp(int stampId) => Send(new ClientMessage { Stamp = new SendStamp { StampId = stampId } });
        // The player is back at the room's screen after a play.
        public static void Returned() => Send(new ClientMessage { Returned = new Returned() });

        // Leaves the room and closes the connection.
        public static async Task LeaveAsync()
        {
            if (!IsConnected) { EndSession("LEFT"); return; }
            leaving = true;
            Send(new ClientMessage { Leave = new Leave() });
            int mine = generation;
            // The server answers with closed; do not wait for long.
            double until = Time.realtimeSinceStartupAsDouble + 2;
            while (mine == generation && IsConnected && Time.realtimeSinceStartupAsDouble < until) await Task.Delay(50);
            if (mine == generation) EndSession("LEFT");
        }

        static void EndSession(string code)
        {
            generation++;
            CloseLink();
            bool hadRoom = Room != null;
            Room = null;
            Statuses = null;
            // A join that failed is reported to its caller (ConnectAsync throws); only a room the
            // player was in ends with an event.
            if (!hadRoom) return;
            // Leaving is the player's own doing and needs no message.
            ClosedCode = code == "LEFT" ? null : code;
            Closed?.Invoke(code);
        }

        static void CloseLink()
        {
            try { cancel?.Cancel(); } catch (ObjectDisposedException) { }
            try { link?.Dispose(); } catch (Exception error) { Log.Warn("Multi", "Closing the room connection: " + error.Message); }
            cancel?.Dispose();
            cancel = null; link = null;
            outbox.Clear();
            writing = false;
        }
    }
}
