using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OurTaiko.Online;

namespace OurTaiko.Tests
{
    public sealed class AvatarResourcesTests
    {
        const string Original = "https://distrib.ourtaiko.org/sso/avatars/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp";
        const string Mirror = "https://static.sakura-bot.cn/ourtaiko/sso/avatars/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp";
        static readonly MethodInfo Fetch = typeof(FanmadeEndpoint).Assembly.GetType("OurTaiko.Online.AvatarResources").GetMethod("FetchAsync");
        static readonly byte[] Bytes = Encoding.UTF8.GetBytes("avatar fixture");

        static Task<byte[]> Read(HttpClient http, string original = Original, CancellationToken cancel = default) =>
            (Task<byte[]>)Fetch.Invoke(null, new object[] { http, original, "https://net.example", 32L, cancel });
        static HttpResponseMessage Reply(byte[] data, HttpStatusCode status = HttpStatusCode.OK) =>
            new HttpResponseMessage(status) { Content = new ByteArrayContent(data) };
        static HttpResponseMessage Selection(string url) => Reply(Encoding.UTF8.GetBytes(new JObject { ["url"] = url }.ToString()));

        [Test] public void MainlandSelectionUsesMirrorAndRetainsCanonicalFallback() => Run(async () =>
        {
            using var handler = new Handler { Response = request =>
                request.RequestUri.Host == "net.example" ? Selection(Mirror) :
                request.RequestUri.Host == "static.sakura-bot.cn" ? Reply(Bytes, HttpStatusCode.ServiceUnavailable) : Reply(Bytes) };
            using var http = new HttpClient(handler);
            Assert.That(await Read(http), Is.EqualTo(Bytes));
            Assert.That(handler.Requests, Is.EqualTo(new[] { "net.example", "static.sakura-bot.cn", "distrib.ourtaiko.org" }));
        });

        [Test] public void OverseasAndUnavailableLookupUseCanonical() => Run(async () =>
        {
            foreach (bool unavailable in new[] { false, true })
            {
                using var handler = new Handler { Response = request => request.RequestUri.Host == "net.example"
                    ? (unavailable ? Reply(Bytes, HttpStatusCode.ServiceUnavailable) : Selection(Original)) : Reply(Bytes) };
                using var http = new HttpClient(handler);
                Assert.That(await Read(http), Is.EqualTo(Bytes));
                Assert.That(handler.Requests, Is.EqualTo(new[] { "net.example", "distrib.ourtaiko.org" }));
            }
        });

        [Test] public void SignedAndCustomAddressesSkipRegionalLookup() => Run(async () =>
        {
            foreach (string original in new[] { Original + "?signature=x", "https://custom.example/avatar.webp" })
            {
                using var handler = new Handler { Response = request => Reply(Bytes) };
                using var http = new HttpClient(handler);
                Assert.That(await Read(http, original), Is.EqualTo(Bytes));
                Assert.That(handler.Requests.Count, Is.EqualTo(1));
            }
        });

        [Test] public void OversizedMirrorFallsBackWithinByteLimit() => Run(async () =>
        {
            using var handler = new Handler { Response = request => request.RequestUri.Host == "net.example" ? Selection(Mirror)
                : Reply(request.RequestUri.Host == "static.sakura-bot.cn" ? new byte[33] : Bytes) };
            using var http = new HttpClient(handler);
            Assert.That(await Read(http), Is.EqualTo(Bytes));
            Assert.That(handler.Requests.Count, Is.EqualTo(3));
        });

        [Test] public void CancellationDoesNotStartCanonicalFallback() => Run(async () =>
        {
            using var cancel = new CancellationTokenSource();
            using var handler = new Handler { Response = request => {
                if (request.RequestUri.Host == "net.example") return Selection(Mirror);
                cancel.Cancel(); throw new OperationCanceledException(cancel.Token);
            } };
            using var http = new HttpClient(handler);
            try { await Read(http, cancel: cancel.Token); Assert.Fail("Cancellation must propagate"); }
            catch (OperationCanceledException) { }
            Assert.That(handler.Requests, Is.EqualTo(new[] { "net.example", "static.sakura-bot.cn" }));
        });

        static void Run(Func<Task> action) => Task.Run(action).GetAwaiter().GetResult();

        sealed class Handler : HttpMessageHandler
        {
            public Func<HttpRequestMessage, HttpResponseMessage> Response;
            public readonly List<string> Requests = new List<string>();
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
            {
                cancel.ThrowIfCancellationRequested();
                Assert.That(request.Headers.Authorization, Is.Null);
                Assert.That(request.Headers.Contains("Cookie"), Is.False);
                Requests.Add(request.RequestUri.Host);
                return Task.FromResult(Response(request));
            }
        }
    }
}
