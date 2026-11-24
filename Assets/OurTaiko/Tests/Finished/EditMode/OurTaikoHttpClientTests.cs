using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using OurTaiko.Online;
using UnityEngine;

namespace OurTaiko.Tests
{
    // The game's one HttpClient (OurTaikoHttpClient): what it says it is, that its requests say so,
    // and the timeout each request is given.
    public sealed class OurTaikoHttpClientTests
    {
        OnlineFixture site;

        static T Run<T>(Func<Task<T>> body) => Task.Run(body).GetAwaiter().GetResult();

        [SetUp] public void SetUp() => site = new OnlineFixture();

        [TearDown]
        public void TearDown()
        {
            site.Dispose();
            Distribution.Forget();
        }

        [Test]
        public void TheUserAgentNamesTheGameItsVersionAndPlatform()
        {
            Assert.That(OurTaikoHttpClient.Describe("1.0.2", RuntimePlatform.WindowsPlayer, "Windows 11  (10.0.26100) 64bit", "X64", false),
                Is.EqualTo("OurTaikoPlay/1.0.2 (Windows; Windows 11 10.0.26100 64bit; X64)"));
            Assert.That(OurTaikoHttpClient.Describe("1.0.2", RuntimePlatform.Android, "Android OS 16 / API-36 (BP2A.250605.031.A1/123)", "Arm64", false),
                Is.EqualTo("OurTaikoPlay/1.0.2 (Android; Android OS 16 / API-36 BP2A.250605.031.A1/123; Arm64)"));
            Assert.That(OurTaikoHttpClient.Describe("1.0.2", RuntimePlatform.OSXEditor, "Mac OS X 15.5.0", "Arm64", true),
                Is.EqualTo("OurTaikoPlay/1.0.2 (macOS; Mac OS X 15.5.0; Arm64; Editor)"));
            Assert.That(OurTaikoHttpClient.Describe("1.0.2", RuntimePlatform.IPhonePlayer, "iOS 18.5", "Arm64", false),
                Is.EqualTo("OurTaikoPlay/1.0.2 (iOS; iOS 18.5; Arm64)"));
        }

        [Test]
        public void WhatASystemCallsItselfCannotBreakTheHeader()
        {
            string agent = OurTaikoHttpClient.Describe("1.0 beta(2)", RuntimePlatform.LinuxPlayer, "Linux 6.8 (Ubuntu; 专业版) \\x\r\n", "", false);
            Assert.That(agent, Is.EqualTo("OurTaikoPlay/1.0beta2 (Linux; Linux 6.8 Ubuntu x)"));
            Assert.That(OurTaikoHttpClient.Describe("", RuntimePlatform.LinuxPlayer, null, null, false), Is.EqualTo("OurTaikoPlay/0 (Linux)"));
            Assert.That(OurTaikoHttpClient.Describe("1", RuntimePlatform.LinuxPlayer, new string('a', 300), "X64", false).Length, Is.LessThan(120));
            using var request = new HttpRequestMessage();
            Assert.That(request.Headers.UserAgent.TryParseAdd(agent), Is.True);
        }

        [Test]
        public void EveryRequestSaysWhatAsks()
        {
            string heard = null;
            site.CustomRequest = context => { heard = context.Request.UserAgent; return false; };
            Assert.That(OurTaikoHttpClient.UserAgent, Does.StartWith("OurTaikoPlay/" + Application.version + " ("));
            Run(() => Distribution.RootAsync(site.BaseUrl));
            Assert.That(heard, Is.EqualTo(OurTaikoHttpClient.UserAgent));
            heard = null;
            Run(() => new ProfileCenter(site.BaseUrl).NameplateAsync(new string('a', 32), "en"));
            Assert.That(heard, Is.EqualTo(OurTaikoHttpClient.UserAgent));
        }

        [Test]
        public void ARequestThatRunsOutOfTimeIsNotTakenForTheCallersCancellation()
        {
            site.CustomRequest = context =>
            {
                if (context.Request.Url.AbsolutePath != "/slow") return false;
                Thread.Sleep(1500);
                // The first asker has gone by now; writing to it may fail, and that is the point.
                try { site.Reply(context, 200, new Newtonsoft.Json.Linq.JObject()); }
                catch (Exception) { }
                return true;
            };
            using var cancel = new CancellationTokenSource();
            Assert.That(() => Run(() => OurTaikoHttpClient.GetAsync(site.BaseUrl + "/slow", TimeSpan.FromMilliseconds(200), cancel.Token)),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(cancel.IsCancellationRequested, Is.False);
            using var answer = Run(() => OurTaikoHttpClient.GetAsync(site.BaseUrl + "/slow", TimeSpan.FromSeconds(10)));
            Assert.That((int)answer.StatusCode, Is.EqualTo(200));
        }

        [Test]
        public void AnotherClientsHandlerSaysTheGamesUserAgentInsteadOfItsOwn()
        {
            var inner = new Recorder();
            using var client = new HttpClient(new OurTaikoHttpClient.UserAgentHandler(inner));
            using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:9/ourtaiko.multiplay.v1.MultiEnso/Connect");
            request.Headers.TryAddWithoutValidation("User-Agent", "grpc-dotnet/2.67.0");
            Run(() => client.SendAsync(request)).Dispose();
            Assert.That(inner.Agent, Is.EqualTo(OurTaikoHttpClient.UserAgent));
        }

        sealed class Recorder : HttpMessageHandler
        {
            public string Agent;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
            {
                Agent = string.Join(" ", request.Headers.GetValues("User-Agent"));
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
            }
        }
    }
}
