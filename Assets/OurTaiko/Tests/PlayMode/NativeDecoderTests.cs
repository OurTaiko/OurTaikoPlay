using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace OurTaiko.Tests
{
    public sealed class NativeDecoderTests
    {
        [TestCase("ogg")]
        [TestCase("mp3")]
        [TestCase("m4a")]
        [TestCase("opus")]
        public void OriginalEncodedFilesDecodeSeekAndRelease(string format)
        {
            string path = "Assets/OurTaiko/Tests/Fixtures/Audio/tone." + format + ".bytes";
            var bytes = File.ReadAllBytes(path);
            int before = NativeAudioSample.LiveStreams;
            var engine = AudioEngine.EnsureInstance();
            Assert.That(engine.Native, Is.True, engine.Diagnostics);
            using (var sample = new NativeAudioSample(bytes, engine))
            {
                Assert.That(sample.Length, Is.InRange(1.9, 2.2), sample.Format);
                Assert.That(sample.EncodedBytes, Is.EqualTo(bytes.Length));
                Assert.That(sample.EncodedBytes, Is.LessThan(48000 * 2 * 2 * 4 / 2));
                Assert.That(sample.Gain, Is.GreaterThan(1));
                sample.Play(0.0001f, false, 1);
                Assert.That(sample.Position, Is.InRange(0.95, 1.1));
                sample.Stop();
            }
            Assert.That(NativeAudioSample.LiveStreams, Is.EqualTo(before));
        }
        [Test]
        public void MixedOutputResamplesPausesAndRoutesWithoutDeviceSpecificDrivers()
        {
            var engine = AudioEngine.EnsureInstance();
            var oldBackend = engine.Backend;
            int oldMixer = engine.Mixer;
            var oldMatrix = engine.MixingMatrix;
            int before = NativeAudioSample.LiveStreams;
            int mixer = ManagedBass.Mix.BassMix.CreateMixerStream(44100, 6,
                ManagedBass.BassFlags.Decode | ManagedBass.BassFlags.Float | ManagedBass.BassFlags.MixerNonStop);
            Assert.That(mixer, Is.Not.Zero);
            var type = typeof(AudioEngine);
            type.GetProperty("Backend").SetValue(engine, AudioBackend.Wasapi);
            type.GetProperty("Mixer").SetValue(engine, mixer);
            type.GetProperty("MixingMatrix").SetValue(engine, AudioEngine.CreateMixingMatrix(6));
            try
            {
                using var sample = new NativeAudioSample(File.ReadAllBytes("Assets/OurTaiko/Tests/Fixtures/Audio/tone.ogg.bytes"), engine, true, true);
                var output = new float[4410 * 6];
                ManagedBass.Bass.ChannelGetData(mixer, output, output.Length * 4);
                Assert.That(sample.Position, Is.LessThan(0.001), "Paused decode must not advance.");
                sample.Play(0.1f, true, 0, 1.25f);
                ManagedBass.Bass.ChannelGetData(mixer, output, output.Length * 4);
                Assert.That(System.Array.Exists(output, value => System.Math.Abs(value) > 0.001f), Is.True);
                Assert.That(sample.Position, Is.GreaterThan(0));
                sample.Stop();
                Assert.That(sample.Playing, Is.False);
            }
            finally
            {
                type.GetProperty("Backend").SetValue(engine, oldBackend);
                type.GetProperty("Mixer").SetValue(engine, oldMixer);
                type.GetProperty("MixingMatrix").SetValue(engine, oldMatrix);
                ManagedBass.Bass.StreamFree(mixer);
            }
            Assert.That(NativeAudioSample.LiveStreams, Is.EqualTo(before));
        }
        [Test]
        public void InvalidDataReleasesPartialStreams()
        {
            int before = NativeAudioSample.LiveStreams;
            Assert.Throws<System.InvalidOperationException>(() => new NativeAudioSample(new byte[1024], AudioEngine.EnsureInstance()));
            Assert.That(NativeAudioSample.LiveStreams, Is.EqualTo(before));
        }
    }
}
