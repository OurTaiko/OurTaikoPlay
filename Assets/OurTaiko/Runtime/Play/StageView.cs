using System.Collections.Generic;
using OurTaiko.Chara;
using UnityEngine;

namespace OurTaiko
{
    // The play backdrop: it asks the catalog which sets this play gets (the chart's #SCENEPRESET, or
    // a draw), loads them and hands each to the view that draws it, then tells those views what the
    // players are doing. Drawn back to front: the scene, the headers, the drumroll effect, the
    // dancers, the stand, the crowd.
    // What the backdrop follows of one player. Gauge is the soul gauge's fill relative to the
    // player's clear line.
    public struct StagePlayer
    {
        public bool Clear, GaugeFull, MissStreak;
        public double Gauge;
    }

    public sealed class StageView : MonoBehaviour
    {
        public const string CrowdClip = "full";

        [Tooltip("A catalog of the scene's own, in place of the game's and the player's (tests). Left empty in the game.")]
        public StageCatalog catalog;
        public StageMode mode;
        [Tooltip("The band above each player's lane: 1P's, then 2P's in Play.Double.")]
        public StageBandView[] headers;
        [Tooltip("The scene below the lane, the stand in front of the dancers and the crowd of a full gauge. Play.Double has none of them.")]
        public StageBandView scene, footer, crowd;
        public DancerTroupeView dancers;
        [Tooltip("The drumroll effect over each player's header.")]
        public StageRendaView[] renda;

        StageRendaView characters;
        StagePlayer[] players = System.Array.Empty<StagePlayer>();

        public StageChoice Choice { get; private set; }
        // The characters a pack's hits bring out, over 1P's header; null until a pack has brought some.
        public StageRendaView Characters => characters;

        // Picks and loads this play's backdrop. random is given by tests; a play draws its own.
        public void Setup(string preset, System.Random random = null) => Setup(new[] { preset }, random);

        // The same, for a song asked a backdrop from several places (its chart, then its folders):
        // the first of `presets` the catalog has for this mode is the one.
        public void Setup(IReadOnlyList<string> presets, System.Random random = null) => Use(Catalog.Choose(presets, mode, random ?? new System.Random(), Has));

        // The game's catalog with the player's added (StageLibrary), unless the scene was given one.
        public StageCatalog Catalog => catalog != null ? catalog : StageLibrary.Catalog;

        // Only sets whose files are there are drawn at random; a given catalog's are taken as there.
        bool Has(string part, string set) => catalog != null || StageLibrary.Has(part, set);

        // Loads the backdrop `choice` names.
        public void Use(StageChoice choice)
        {
            Choice = choice;
            var catalog = Catalog;
            StageLibrary.Keep(catalog.Files(choice, headers.Length));
            var header = Load(StageCatalog.HeaderFolder, Choice.Header);
            bool alignClear = System.Array.Find(catalog.headers, set => set.name == Choice.Header)?.syncClear ?? false;
            for (int i = 0; i < headers.Length; i++)
            {
                // 2P's own header when the set has one.
                var own = i > 0 && header != null ? StageLibrary.Load(StageCatalog.HeaderFolder, Choice.Header + "_2p", quiet: true) : null;
                headers[i].Use(own != null ? own : header, alignClear: alignClear);
            }
            var rendaSet = Load(StageCatalog.RendaFolder, Choice.Renda);
            var effect = System.Array.Find(catalog.rendas, set => set.name == Choice.Renda);
            if (renda != null) foreach (var view in renda) view.Use(rendaSet, effect?.hitSpread);
            var chibi = Load(StageCatalog.ChibiFolder, catalog.Pack(Choice.Preset)?.chibi?.Trim());
            if (characters == null && chibi != null && headers.Length > 0)
                characters = headers[0].gameObject.AddComponent<StageRendaView>();
            if (characters != null) characters.UseCharacters(chibi);
            if (scene != null)
            {
                var file = Load(StageCatalog.SceneFolder, Choice.Scene);
                if (Choice.Clear == StagePack.None) scene.Use(file, null, null);
                else scene.Use(file, Load(StageCatalog.ClearFolder, Choice.Clear));
            }
            if (footer != null) footer.Use(Load(StageCatalog.FooterFolder, Choice.Footer));
            if (crowd != null) crowd.Use(null, Load(StageCatalog.CrowdFolder, Choice.Crowd), CrowdClip);
            if (dancers != null) dancers.Use(Members(catalog.Troupe(Choice.Troupe)), catalog.Troupe(Choice.Troupe));
        }

        static OtanimAsset Load(string folder, string name) => StageLibrary.Load(folder, name);

        static List<OtanimAsset> Members(StageTroupe troupe)
        {
            var members = new List<OtanimAsset>();
            if (troupe == null) return members;
            if (troupe.movie) { var movie = Load(StageCatalog.DancerFolder, troupe.name); if (movie != null) members.Add(movie); return members; }
            for (int i = 0; i < troupe.members; i++)
            {
                var member = Load(StageCatalog.DancerFolder, troupe.name + "_" + i);
                // They come in order, so a missing one ends the troupe there.
                if (member == null) break;
                members.Add(member);
            }
            return members;
        }

        // What the players are doing at chart time `time`: each header follows its own player, the
        // rest follow 1P.
        public void Show(double time, IReadOnlyList<PlayerLane> lanes)
        {
            if (players.Length != lanes.Count) players = new StagePlayer[lanes.Count];
            for (int i = 0; i < players.Length; i++)
            {
                var shown = lanes[i].Shown;
                players[i] = new StagePlayer
                {
                    Clear = shown.IsClear,
                    GaugeFull = shown.IsGaugeFull,
                    MissStreak = lanes[i].MissStreak >= PlayerLane.MissStreakDark,
                    Gauge = shown.GaugePoints / (SoulGauge.MaximumPoints * shown.ClearThreshold),
                };
            }
            Show(time, lanes[0].Shown.BpmAt(time), players);
        }

        // The same, from what the backdrop follows of each player; bpm is the chart's at `time`.
        public void Show(double time, double bpm, IReadOnlyList<StagePlayer> shown)
        {
            for (int i = 0; i < headers.Length && i < shown.Count; i++)
            {
                headers[i].Cleared = shown[i].Clear;
                headers[i].Dark = shown[i].MissStreak;
            }
            var first = shown[0];
            if (scene != null) scene.Cleared = first.Clear;
            if (crowd != null)
            {
                crowd.Cleared = first.GaugeFull;
                crowd.Bpm = bpm > 0 ? bpm : 120;
            }
            if (dancers != null) dancers.ShowTime(time, bpm, first.Gauge, first.Clear);
        }

        // Draws every part at wall-clock `now`. In a play each part's own Update does this; a
        // preview outside play has no Update and calls it instead.
        public void Draw(double now)
        {
            foreach (var header in headers) header.Show(now);
            if (renda != null) foreach (var view in renda) view.Show(now);
            if (characters != null) characters.Show(now);
            if (scene != null) scene.Show(now);
            if (footer != null) footer.Show(now);
            if (crowd != null) crowd.Show(now);
        }

        public void Judged(int player, bool missed, double bpm)
        {
            if (player == 0 && characters != null) characters.Judged(missed, bpm);
        }

        // A player's drumroll was hit.
        public void DrumrollHit(int player)
        {
            if (renda == null) return;
            foreach (var view in renda) if (view.player == player) view.Hit();
        }
    }
}
