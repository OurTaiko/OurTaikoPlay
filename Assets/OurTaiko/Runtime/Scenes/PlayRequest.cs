using System;
using System.Collections.Generic;

namespace OurTaiko
{
    // One seat's part of a play: the TJA COURSE value chosen on the song list (null plays the
    // SongDefinition's own course) and オート. Its side (ChartSide: the block the seat plays, which its
    // result uploads to) is decided as the switcher takes the request, from the seats as they stand.
    public readonly struct SeatPlay
    {
        public readonly string Course;
        public readonly bool AutoPlay;
        // Null until the request is selected (SceneSwitcher.Select).
        public readonly string Side;

        public SeatPlay(string course, bool autoPlay) : this(course, autoPlay, null) { }

        SeatPlay(string course, bool autoPlay, string side)
        {
            Course = course;
            AutoPlay = autoPlay;
            Side = side;
        }

        internal SeatPlay On(string side) => new SeatPlay(Course, AutoPlay, side);
    }

    // What the play scenes play next, built in one step and handed to SceneSwitcher.Play (or Select,
    // for a play scene opened directly): the song, each seat's part, the backdrops the folders it was
    // opened from ask for, and the data of the mode it is played in, at most one: a recorded play to
    // watch (ReplayMode), a ghost battle (GhostMode), a room's round (MultiMode), or where normal play
    // left off for the practice it switches to (PracticeMode). It stays the switcher's request until
    // the next one: a restart plays it again, and the result screen and the ghost list read its mode.
    public sealed class PlayRequest
    {
        public SongDefinition Song { get; }
        // Always two: seat 0 is the first lane, played on the leading side (SceneSwitcher.Lead: 1P, or
        // a lone 2P's 2P); seat 1 is 2P's lane in Play.Double.
        public IReadOnlyList<SeatPlay> Seats => seats;
        readonly SeatPlay[] seats;
        readonly string[] folderScenePresets;
        // A recorded play for the practice scene to show instead of a play.
        public Online.PlayRecord Replay { get; }
        public GhostMatch Ghost { get; }
        public MultiMatch Multi { get; }
        // Where normal play left off and the route its lane showed there; SceneSwitcher hands it to
        // the practice scene once.
        internal (double Position, BranchRoute Branch)? PracticeEntry { get; }

        // One player's play (and the course 2P plays with them, without オート).
        public PlayRequest(SongDefinition song, string course = null, bool autoPlay = false)
            : this(song, new[] { new SeatPlay(course, autoPlay) }) { }

        // seats: 1P's part first. 2P plays 1P's course where it names none, and without オート where it
        // is not given. folderScenePresets: the backdrops the folders the song was opened from ask for,
        // the nearest first.
        public PlayRequest(SongDefinition song, IReadOnlyList<SeatPlay> seats, string[] folderScenePresets = null)
            : this(song, seats, folderScenePresets, null, null, null, null) { }

        PlayRequest(SongDefinition song, IReadOnlyList<SeatPlay> seats, string[] folderScenePresets, Online.PlayRecord replay,
            GhostMatch ghost, MultiMatch multi, (double, BranchRoute)? practiceEntry)
        {
            if (seats == null || seats.Count < 1 || seats.Count > 2) throw new ArgumentException("A play has one or two seats.", nameof(seats));
            Song = song;
            var first = seats[0];
            var second = seats.Count > 1 ? seats[1] : new SeatPlay(null, false);
            this.seats = new[] { first, new SeatPlay(second.Course ?? first.Course, second.AutoPlay) };
            this.folderScenePresets = folderScenePresets ?? Array.Empty<string>();
            Replay = replay;
            Ghost = ghost;
            Multi = multi;
            PracticeEntry = practiceEntry;
        }

        // Watching `record`, a recorded play of `course`, in the practice scene.
        public static PlayRequest Watch(SongDefinition song, string course, Online.PlayRecord record)
            => new PlayRequest(song, new[] { new SeatPlay(course, false) }, null, record, null, null, null);

        // Playing `song` against the recorded play: both seats on its block, neither on オート (GhostMode
        // gives each lane the recording's options). Not 2P's course selection.
        public static PlayRequest GhostBattle(SongDefinition song, GhostMatch match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            var seat = new SeatPlay(match.Course, false);
            return new PlayRequest(song, new[] { seat, seat }, null, null, match, null, null);
        }

        // A room's round: its song on the course this player took.
        public static PlayRequest MultiRound(SongDefinition song, string course, int round)
            => new PlayRequest(song, new[] { new SeatPlay(course, false) }, null, null, null, new MultiMatch { Round = round, Course = course }, null);

        // The same song in the other play mode (SceneSwitcher.SwitchToPractice / SwitchToNormalPlay):
        // 1P's course and オート and nothing else, with where normal play left off for practice.
        internal PlayRequest ForModeSwitch(double? entryPosition, BranchRoute entryBranch)
            => new PlayRequest(Song, new[] { new SeatPlay(seats[0].Course, seats[0].AutoPlay) }, null, null, null, null,
                entryPosition.HasValue ? (entryPosition.Value, entryBranch) : ((double, BranchRoute)?)null);

        // Each seat's side, decided as the switcher takes the request: the first seat reads its own
        // side's blocks (SeatBlocks of `lead`), alone or as one of two as the seats stand; 2P reads 2P's
        // as one of two. A ghost battle's lanes both play the recording's block.
        internal void DecideSides(Side lead, bool together)
        {
            if (Ghost != null)
            {
                seats[0] = seats[0].On(Ghost.Side);
                seats[1] = seats[1].On(Ghost.Side);
                return;
            }
            seats[0] = seats[0].On(Song != null ? Song.PlayedSide(seats[0].Course, SeatBlocks.Seats[lead.Index()], together) : "single");
            seats[1] = seats[1].On(Song != null ? Song.PlayedSide(seats[1].Course, SeatBlocks.Seats[1], true) : "p2");
        }

        // The play backdrops asked for the song, the one that counts most first: the chart's own
        // SCENEPRESET (`own`), then those of the folders it was opened from, from the nearest
        // outwards. A play that is not opened from a folder (a search's, a ghost battle's, a room's)
        // has only the chart's.
        public string[] ScenePresets(string own)
        {
            var asked = new List<string>();
            if (!string.IsNullOrWhiteSpace(own)) asked.Add(own);
            asked.AddRange(folderScenePresets);
            return asked.ToArray();
        }
    }
}
