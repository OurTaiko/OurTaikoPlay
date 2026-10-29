using MajdataPlay.FFmpeg;
using UnityEngine;

namespace OurTaiko
{
    // MoviePlayer on MajdataPlay's FFmpeg player: hardware decoding with native GPU textures where
    // the platform has them (VideoToolbox on Apple, D3D11 on Windows, MediaCodec on Android).
    [RequireComponent(typeof(FFmpegVideoPlayer))]
    public sealed class FFmpegMoviePlayer : MoviePlayer
    {
        public FFmpegVideoPlayer video;

        void Awake()
        {
            if (video == null) video = GetComponent<FFmpegVideoPlayer>();
            video.PreferredDecoderType = VideoDecoderType.Hardware;
            video.PreferNativeTextures = true;
            video.Loop = false;
            video.TextureChanged += OnTexture;
            video.ErrorReceived += OnError;
        }

        void OnDestroy()
        {
            if (video == null) return;
            video.TextureChanged -= OnTexture;
            video.ErrorReceived -= OnError;
            video.Close();
        }

        public override bool IsOpen => video.State != VideoPlaybackState.Idle;
        public override bool IsPrepared => video.IsPrepared;
        public override bool IsSeeking => video.State == VideoPlaybackState.Seeking;
        public override bool IsPlaying => video.State == VideoPlaybackState.Playing;
        public override bool IsEnded => video.State == VideoPlaybackState.Ended;
        public override bool CanSeek => video.IsSeekable;
        public override double Position => video.IsPrepared ? video.TimeSeconds : 0;
        public override double Length => video.LengthSeconds;
        public override float Rate { get => video.PlaybackRate; set => video.PlaybackRate = value; }

        public override void Open(string path) => video.Preload(path);
        public override void Close() => video.Close();
        public override void Play() => video.Play();
        public override void Pause() => video.Pause();
        public override void Seek(double seconds) => video.TimeSeconds = seconds;

        void OnTexture(FFmpegVideoPlayer _, Texture texture) => ShowTexture(texture);
        void OnError(FFmpegVideoPlayer _, string message) => Fail(message);
    }
}
