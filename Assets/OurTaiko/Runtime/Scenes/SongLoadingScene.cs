using System;
using System.Collections;
using System.Threading;
using OurTaiko.Online;
using UnityEngine;

namespace OurTaiko
{
    // Shown behind the parked rainbow curtain (arcade loading_song frame 55): parses the TJA with
    // the play options applied and loads the song audio into memory, then hands both to
    // SinglePlayScene, whose load opens the curtain. An online song is first downloaded and
    // verified (fanmade.cpp prepare) with its progress on the curtain; Back cancels the download.
    public sealed class SongLoadingScene : MonoBehaviour
    {
        [Tooltip("The title and hints stay up at least this long, however fast the song loads.")]
        [Min(0)] public float minimumSeconds = 2;
        [Tooltip("How long a download error stays on the curtain before returning to the song list.")]
        [Min(0)] public float errorSeconds = 3;

        public bool IsLoaded { get; private set; }
        // Set when an online download failed or was cancelled; the scene then returns instead of playing.
        public string Error { get; private set; }
        CancellationTokenSource downloadCancellation;
        void OnDisable() => downloadCancellation?.Cancel();

        IEnumerator Start()
        {
            var switcher = SceneSwitcher.EnsureInstance();
            if (switcher.TakeDanLoad() is DanRun dan) { yield return LoadDan(switcher, dan); yield break; }
            var song = switcher.SelectedSong;
            string course = switcher.SelectedCourse;
            if (song == null)
            {
                Debug.LogWarning("No song is selected; returning to Entry.", this);
                switcher.SwitchScene(SceneSwitcher.MenuScene);
                yield break;
            }
            if (!switcher.IsCovered)
            {
                // Entered directly (Play mode on this scene): show the parked curtain at once.
                switcher.ShowSongOnCurtain(song);
                switcher.ParkCurtain();
            }
            double started = GameTimeline.FrameTime;

            var online = OnlineManager.EnsureInstance();
            if (online.IsOnline(song))
            {
                yield return Download(switcher, online, song);
                if (Error != null)
                {
                    switcher.SetCoverStatus(Error, new Color(1, 0.35f, 0.35f));
                    double failedAt = GameTimeline.FrameTime;
                    while (GameTimeline.FrameTime - failedAt < errorSeconds || switcher.IsSwitching) yield return null;
                    switcher.SwitchScene(switcher.ReturnScene);
                    yield break;
                }
            }

            // GameScreen::init_tja, moved behind the curtain: the parse and Player::reset_chart's options.
            try
            {
                switcher.SetPreparedChart(song, course, PlayScene.PrepareChart(song, course));
                // 2P reads its own block of its own course with its own options.
                if (switcher.PlaysTwo)
                    switcher.SetPreparedChart(song, switcher.SelectedCourse2P, PlayScene.PrepareChart(song, switcher.SelectedCourse2P, 1), 1);
            }
            catch (Exception error) { Debug.LogException(error, this); }  // The play scene parses again and shows it.
            yield return DecodeAudio(song);
            if (Error != null)
            {
                switcher.SetCoverStatus(Error, new Color(1, 0.35f, 0.35f));
                yield return new WaitForSecondsRealtime(errorSeconds);
                while (switcher.IsSwitching) yield return null;
                switcher.SwitchScene(switcher.ReturnScene);
                yield break;
            }
            IsLoaded = true;

            while (GameTimeline.FrameTime - started < minimumSeconds || switcher.IsSwitching) yield return null;
            switcher.SetCoverStatus("", Color.white);
            switcher.SwitchScene(switcher.SelectedPlayScene);
        }

        // Decodes the song's audio into memory for the play scene; Error is set when it cannot.
        IEnumerator DecodeAudio(SongDefinition song)
        {
            var engine = AudioEngine.EnsureInstance();
            // Without BASS the song plays silently; the chart clock does not need the audio.
            if (!engine.Available || song == null || (song.music == null && string.IsNullOrEmpty(song.audioPath))) yield break;
            byte[] encoded = null;
            try { if (string.IsNullOrEmpty(song.audioPath)) encoded = AudioAssetCatalog.Read(song.music); }
            catch (Exception error) { Error = "AUDIO_LOAD_FAILED"; Debug.LogException(error); }
            if (Error != null) yield break;
            string path = song.audioPath;
            // A cancelled scene still releases a completed native decode.
            using (var decode = new BackgroundAudioDecode(() => encoded ?? System.IO.File.ReadAllBytes(path), engine, true))
            {
                while (!decode.IsDone) yield return null;
                if (decode.Succeeded) song.SetPreparedAudio(decode.Take());
                else { Error = "AUDIO_DECODE_FAILED"; Debug.LogException(decode.Error); }
            }
        }

        // 段位道場 (under its own loading cover): every song of the course, in order: a server's song is downloaded first, then
        // each chart is parsed with the play options and its audio decoded. Any failure returns to
        // the course select with the reason on the curtain.
        IEnumerator LoadDan(SceneSwitcher switcher, DanRun dan)
        {
            if (!switcher.IsCovered) switcher.ParkDanCover();
            double started = GameTimeline.FrameTime;
            var online = OnlineManager.EnsureInstance();
            for (int i = 0; i < dan.Count && Error == null; i++)
            {
                var song = dan.Song(i);
                bool repeat = System.Array.IndexOf(Songs(dan), song) < i;
                if (online.IsOnline(song) && !repeat) yield return Download(switcher, online, song, false);
                if (Error != null) break;
                try { dan.Charts[i] = PlayScene.PrepareChart(dan.Resolved(i)); }
                catch (Exception error) { Error = "CHART_LOAD_FAILED"; Debug.LogException(error, this); break; }
                // Each entry keeps its own decoded audio, so a song played twice starts at once both times.
                yield return DecodeAudio(song);
                dan.Audio[i] = song.TakePreparedAudio();
            }
            if (Error != null)
            {
                dan.Release();
                switcher.SetCoverStatus(Error, new Color(1, 0.35f, 0.35f));
                double failedAt = GameTimeline.FrameTime;
                while (GameTimeline.FrameTime - failedAt < errorSeconds || switcher.IsSwitching) yield return null;
                switcher.SwitchScene(SceneSwitcher.DanSelectScene);
                yield break;
            }
            IsLoaded = true;
            while (GameTimeline.FrameTime - started < minimumSeconds || switcher.IsSwitching) yield return null;
            switcher.SetCoverStatus("", Color.white);
            switcher.SwitchScene(SceneSwitcher.DanPlayScene);
        }

        static SongDefinition[] Songs(DanRun dan)
        {
            var songs = new SongDefinition[dan.Count];
            for (int i = 0; i < songs.Length; i++) songs[i] = dan.Song(i);
            return songs;
        }

        // titleSong: the curtain shows the downloaded song's title (a course keeps its own).
        IEnumerator Download(SceneSwitcher switcher, OnlineManager online, SongDefinition song, bool titleSong = true)
        {
            var cancel = downloadCancellation = new CancellationTokenSource();
            DownloadProgress latest = null;
            bool movie = SettingManager.EnsureInstance().Settings.display.backgroundMovie;
            var task = online.Client.PrepareAsync(online.ChartOf(song), cancel.Token, progress => Volatile.Write(ref latest, progress), movie);
            while (!task.IsCompleted)
            {
                // The parked curtain blocks scene input, so Back is read directly.
                if (InputManager.GetKeyDown(InputKey.Back)) cancel.Cancel();
                var progress = Volatile.Read(ref latest);
                if (progress != null) switcher.SetCoverStatus(Describe(progress), Color.white);
                yield return null;
            }
            downloadCancellation = null;
            cancel.Dispose();
            if (!task.IsCompletedSuccessfully)
            {
                var error = task.Exception?.GetBaseException();
                Debug.LogWarning("Online chart download failed: " + error?.Message);
                Error = error is FanmadeException ? error.Message : "DOWNLOAD_FAILED";
                yield break;
            }
            var (text, audio, chart) = task.Result;
            switcher.SetCoverStatus("音源を読み込み中…", Color.white);
            online.SetPrepared(song, chart, text, null);
            song.audioPath = audio;
            song.moviePath = chart.MoviePath;
            if (titleSong) switcher.ShowSongOnCurtain(song);
            switcher.SetCoverStatus("", Color.white);
        }

        // The download card's lines (chart / audio, and the movie when one is fetched) as one status text.
        public static string Describe(DownloadProgress progress)
        {
            if (progress.Step == DownloadProgress.Stage.Checking) return "サーバーを確認中…";
            if (progress.Step == DownloadProgress.Stage.Preparing || progress.Step == DownloadProgress.Stage.Ready) return "譜面を準備中…";
            string text = "譜面 " + Describe(progress.Chart) + "　音源 " + Describe(progress.Audio);
            return progress.Movie == null ? text : text + "　映像 " + Describe(progress.Movie);
        }

        static string Describe(FileProgress file)
        {
            switch (file.Status)
            {
                case FileProgress.State.Waiting: return "待機中";
                case FileProgress.State.Verifying: return "確認中";
                case FileProgress.State.Cached: return "キャッシュ済み";
                case FileProgress.State.Complete: return "完了";
                default:
                    return file.Total > 0
                        ? $"{file.Received * 100 / file.Total}% ({Size(file.Received)}/{Size(file.Total)})"
                        : Size(file.Received);
            }
        }

        static string Size(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1048576.0:0.0} MB" : $"{bytes / 1024.0:0} KB";
    }
}
