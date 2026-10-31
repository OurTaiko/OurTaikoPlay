using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class LogTests
    {
        string folder;
        LogLevel previous;

        [SetUp]
        public void SetUp()
        {
            previous = Log.MinimumLevel;
            LogFile.Stop();
            folder = Path.Combine(Path.GetTempPath(), "ourtaiko-log-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            LogFile.Stop();
            Log.MinimumLevel = previous;
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        [Test]
        public void MessagesBelowTheMinimumLevelAreDropped()
        {
            Log.MinimumLevel = LogLevel.Warning;
            Log.Debug("Test", "debug");
            Log.Info("Test", "info");
            LogAssert.Expect(LogType.Warning, "[Test] warn");
            Log.Warn("Test", "warn");
            LogAssert.Expect(LogType.Error, "[Test] error");
            Log.Error("Test", "error");
            LogAssert.NoUnexpectedReceived();

            Log.MinimumLevel = LogLevel.Off;
            Log.Error("Test", "silent");
            Log.Exception("Test", new InvalidOperationException("silent"));
            LogAssert.NoUnexpectedReceived();

            Log.MinimumLevel = LogLevel.Debug;
            LogAssert.Expect(LogType.Log, "[Test] debug");
            Log.Debug("Test", "debug");
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("InvalidOperationException: thrown"));
            Log.Exception("Test", new InvalidOperationException("thrown"));
        }

        [Test]
        public void TheFileHasEachLevelAndUnitysOwnMessagesOnce()
        {
            Log.MinimumLevel = LogLevel.Debug;
            LogFile.Start(folder);
            Assert.That(LogFile.IsRunning, Is.True);
            string path = LogFile.Path;
            LogAssert.Expect(LogType.Log, "[Test] a debug line");
            Log.Debug("Test", "a debug line");
            LogAssert.Expect(LogType.Warning, "[Test] a warning");
            Log.Warn("Test", "a warning");
            LogAssert.Expect(LogType.Warning, "a plugin's warning");
            Debug.LogWarning("a plugin's warning");
            LogFile.Stop();

            string[] lines = File.ReadAllLines(path);
            Assert.That(lines.Count(l => l.Contains(" DEBUG ") && l.EndsWith("Test: a debug line")), Is.EqualTo(1));
            Assert.That(lines.Count(l => l.Contains(" WARN  ") && l.EndsWith("Test: a warning")), Is.EqualTo(1), "Forwarded to Unity, not written twice.");
            Assert.That(lines.Count(l => l.Contains(" WARN  ") && l.EndsWith("Unity: a plugin's warning")), Is.EqualTo(1));
            Assert.That(lines.First(), Does.Contain("Log: OurTaiko"));
        }

        [Test]
        public void OnlyTheNewestSessionsAreKept()
        {
            Directory.CreateDirectory(folder);
            for (int i = 0; i < 12; i++) File.WriteAllText(Path.Combine(folder, $"ourtaiko-20261031-1200{i:00}.log"), "");
            File.WriteAllText(Path.Combine(folder, "other.txt"), "");
            LogFile.Prune(folder, 3);
            Assert.That(Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal),
                Is.EqualTo(new[] { "other.txt", "ourtaiko-20261031-120009.log", "ourtaiko-20261031-120010.log", "ourtaiko-20261031-120011.log" }));
        }

        [Test]
        public void TheSettingNamesALevelOrFallsBackToTheDefault()
        {
            Assert.That(new GeneralSettings().LogLevel, Is.EqualTo(Log.DefaultLevel));
            Assert.That(new GeneralSettings { logLevel = "warning" }.LogLevel, Is.EqualTo(LogLevel.Warning));
            Assert.That(new GeneralSettings { logLevel = "Off" }.LogLevel, Is.EqualTo(Log.DefaultLevel), "The menu has no Off.");
            Assert.That(new GeneralSettings { logLevel = "loud" }.LogLevel, Is.EqualTo(Log.DefaultLevel));

            var menu = new SettingsMenu(SettingsMenu.Catalog(), new GameSettings());
            var item = menu.Types[0].Items.Single(i => i.Label == "Log Level");
            Assert.That(item.Choices, Is.EqualTo(new[] { "Debug", "Info", "Warning", "Error" }));
        }
    }
}
