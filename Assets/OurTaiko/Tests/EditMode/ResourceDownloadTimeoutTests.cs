using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using OurTaiko.Online;

namespace OurTaiko.Tests
{
    public sealed class ResourceDownloadTimeoutTests
    {
        static readonly Type Download = typeof(FanmadeEndpoint).Assembly.GetType("OurTaiko.Online.ResourceDownload");
        static readonly FieldInfo Timeout = Download.GetField("StallTimeout", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly byte[] Bytes = Encoding.UTF8.GetBytes("slow and steady verified download");
        TimeSpan previous;

        [SetUp] public void Setup()
        {
            previous = (TimeSpan)Timeout.GetValue(null);
            Timeout.SetValue(null, TimeSpan.FromMilliseconds(400));
        }
        [TearDown] public void Cleanup() => Timeout.SetValue(null, previous);

        static FanmadeResource Resource(string fallback = null) => new FanmadeResource {
            Url = "https://mirror.example/file", FallbackUrl = fallback,
            Size = Bytes.Length, Hash = FanmadeFixture.Sha(Bytes)
        };
        static Task<byte[]> Fetch(HttpClient http, FanmadeResource resource, CancellationToken cancel) =>
            (Task<byte[]>)Download.GetMethod("FetchAsync").Invoke(null, new object[] { http, resource, cancel, null, null });
        static HttpResponseMessage Reply(Stream stream) => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StreamContent(stream) { Headers = { ContentLength = Bytes.Length } }
        };

        [Test] public void MirrorHeadersWithoutBodyFallBackToVerifiedCanonical()
        {
            using var handler = new Handler();
            handler.Response = n => Reply(n == 1 ? (Stream)new TimedStream(stall: true) : new MemoryStream(Bytes));
            using var http = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var bytes = Task.Run(() => Fetch(http, Resource("https://origin.example/file"), limit.Token)).GetAwaiter().GetResult();
            Assert.That(bytes.SequenceEqual(Bytes), Is.True, "Only verified fallback bytes are returned.");
            Assert.That(handler.Requests, Is.EqualTo(new[] { "mirror.example", "origin.example" }));
        }

        [Test] public void CanonicalSteadyChunksOutliveSeveralStallIntervals()
        {
            using var handler = new Handler();
            handler.Response = _ => Reply(new TimedStream(stall: false));
            using var http = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var resource = Resource(); resource.Url = "https://origin.example/file";
            // One byte each 60 ms: total transfer > four 400 ms stall intervals.
            var bytes = Task.Run(() => Fetch(http, resource, limit.Token)).GetAwaiter().GetResult();
            Assert.That(bytes.SequenceEqual(Bytes), Is.True);
            Assert.That(handler.Requests, Is.EqualTo(new[] { "origin.example" }));
        }

        [Test] public void CanonicalStallTimesOutWithoutPlayerCancellation()
        {
            using var handler = new Handler { Response = _ => Reply(new TimedStream(stall: true)) };
            using var http = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Assert.Catch<OperationCanceledException>(() => Task.Run(() => Fetch(http, Resource(), limit.Token)).GetAwaiter().GetResult());
            Assert.That(limit.IsCancellationRequested, Is.False, "This is the stall timer, not the player's cancellation.");
            Assert.That(handler.Requests.Count, Is.EqualTo(1));
        }

        [Test] public void PlayerCancellationDoesNotStartFallback()
        {
            using var handler = new Handler { Response = _ => Reply(new TimedStream(stall: true)) };
            using var http = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            Assert.Catch<OperationCanceledException>(() => Task.Run(() => Fetch(http, Resource("https://origin.example/file"), cancel.Token)).GetAwaiter().GetResult());
            Assert.That(cancel.IsCancellationRequested, Is.True);
            Assert.That(handler.Requests.Count, Is.EqualTo(1));
        }

        sealed class Handler : HttpMessageHandler
        {
            public readonly System.Collections.Generic.List<string> Requests = new System.Collections.Generic.List<string>();
            public Func<int, HttpResponseMessage> Response;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
            {
                cancel.ThrowIfCancellationRequested();
                Requests.Add(request.RequestUri.Host);
                // The headers arrive immediately; the content stream controls byte arrival.
                return Task.FromResult(Response(Requests.Count));
            }
        }
        sealed class TimedStream : Stream
        {
            readonly bool stall;
            int position;
            public TimedStream(bool stall) { this.stall = stall; }
            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancel)
            {
                if (position == Bytes.Length) return 0;
                await Task.Delay(stall ? System.Threading.Timeout.Infinite : 60, cancel);
                buffer[offset] = Bytes[position++];
                return 1;
            }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => Bytes.Length;
            public override long Position { get => position; set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
