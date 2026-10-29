using MajdataPlay.FFmpeg;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The chart's background movie (BGMOVIE) behind the lanes, decoded by MajdataPlay's FFmpeg
    // player. As in OpenTaiko, while a song has a movie the stage art (sky, header, festival,
    // dancers, footer) is hidden, the screen is black until the movie starts and keeps its last
    // frame after it ends, the movie fills the height (a 4:3 one shows black at the sides) and the
    // lanes' backgrounds turn see-through (Settings > Display). A movie that cannot be opened
    // gives the stage art back. The scene drives it every frame with Show (MovieSync).
    public sealed class BackgroundMovieView : MonoBehaviour
    {
        public FFmpegVideoPlayer player;
        [Tooltip("The movie, sized to the 1080 px height and centred.")]
        public RawImage screen;
        [Tooltip("Black behind the movie, over the hidden stage art.")]
        public Graphic blackout;
        [Tooltip("Hidden while a movie is loaded: the play backdrop, header, festival, dancers, footer.")]
        public CanvasGroup[] stageArt;
        [Tooltip("Each lane's background, branch colour and go-go lane, drawn at the lane opacity setting.")]
        public CanvasGroup[] laneBackgrounds;

        public const float Height = 1080;
        public bool HasMovie { get; private set; }
        public bool IsShowing => screen != null && screen.enabled;
        string loaded;

        void Awake()
        {
            player.PreferredDecoderType = VideoDecoderType.Hardware;
            player.PreferNativeTextures = true;
            player.Loop = false;
            player.TextureChanged += OnTexture;
            player.ErrorReceived += OnError;
            Present(false);
        }

        void OnDestroy()
        {
            if (player == null) return;
            player.TextureChanged -= OnTexture;
            player.ErrorReceived -= OnError;
            player.Close();
        }

        // Opens a song's movie (null or "" for none). A song without one, or with the setting off,
        // keeps the stage art.
        public void Load(string path)
        {
            if (!SettingManager.EnsureInstance().Settings.display.backgroundMovie) path = null;
            if (string.IsNullOrEmpty(path)) { Unload(); return; }
            if (path == loaded && HasMovie) return;
            loaded = path;
            Present(true);
            screen.enabled = false;
            player.Preload(path);
        }

        public void Unload()
        {
            loaded = null;
            if (player.State != VideoPlaybackState.Idle) player.Close();
            Present(false);
        }

        // target: the movie position the song is at (song time - MOVIEOFFSET); running: whether
        // the song clock moves; rate: its playback rate.
        public void Show(double target, double rate, bool running)
        {
            if (!HasMovie) return;
            var state = player.State;
            var plan = MovieSync.Decide(target, rate, running, new MovieSync.Player
            {
                Prepared = player.IsPrepared, Seeking = state == VideoPlaybackState.Seeking,
                Playing = state == VideoPlaybackState.Playing, Ended = state == VideoPlaybackState.Ended,
                Position = player.IsPrepared ? player.TimeSeconds : 0, Length = player.LengthSeconds,
            });
            if (plan.Pause) player.Pause();
            if (plan.SeekTo is double position && player.IsSeekable) player.TimeSeconds = position;
            if (System.Math.Abs(player.PlaybackRate - plan.Rate) > 1e-4) player.PlaybackRate = (float)plan.Rate;
            if (plan.Play) player.Play();
            screen.enabled = plan.Visible && screen.texture != null;
        }

        void OnTexture(FFmpegVideoPlayer _, Texture texture)
        {
            screen.texture = texture;
            if (texture != null && texture.height > 0)
                screen.rectTransform.sizeDelta = new Vector2(Height * texture.width / texture.height, Height);
        }

        void OnError(FFmpegVideoPlayer _, string message)
        {
            Debug.LogWarning("Background movie " + loaded + " cannot play: " + message);
            loaded = null;
            Present(false);
        }

        // With a movie: the stage art hidden, black behind, the lanes see-through.
        void Present(bool movie)
        {
            HasMovie = movie;
            float lanes = movie ? SettingManager.EnsureInstance().Settings.display.MovieLaneAlpha : 1;
            foreach (var art in stageArt) if (art != null) art.alpha = movie ? 0 : 1;
            foreach (var lane in laneBackgrounds) if (lane != null) lane.alpha = lanes;
            if (blackout != null) blackout.enabled = movie;
            if (!movie && screen != null) { screen.enabled = false; screen.texture = null; }
        }
    }
}
