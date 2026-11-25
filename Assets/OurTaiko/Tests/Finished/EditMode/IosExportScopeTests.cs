using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OurTaiko.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // The Xcode rename is replaced by a plain directory move: the real one needs the iOS build module
    // and an exported project, and Unity's append check needs a real export.
    public sealed class IosExportScopeTests
    {
        const string Branded = "OurTaikoPlay.xcodeproj", Original = "Unity-iPhone.xcodeproj";
        string root;
        List<string> renames;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "OurTaikoIosExport-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            renames = new List<string>();
        }

        [TearDown]
        public void TearDown() => Directory.Delete(root, true);

        void Rename(string from, string to)
        {
            renames.Add(from + ">" + to);
            Directory.Move(Path.Combine(root, from + ".xcodeproj"), Path.Combine(root, to + ".xcodeproj"));
        }

        bool Exists(string project) => Directory.Exists(Path.Combine(root, project));

        [Test]
        public void AFreshExportIsNotAppendedOrChecked()
        {
            using var scope = new IosExportScope(root, Rename, () => throw new AssertionException("Checked a fresh export"));
            Assert.That(scope.Appending, Is.False);
            Assert.That(renames, Is.Empty);
        }

        [TestCase(CanAppendBuild.No)]
        [TestCase(CanAppendBuild.Unsupported)]
        public void AnExportUnityCannotAppendFailsEarlyAndKeepsItsName(CanAppendBuild answer)
        {
            Directory.CreateDirectory(Path.Combine(root, Branded));
            bool checkedUnbranded = false;
            var error = Assert.Throws<BuildFailedException>(() => new IosExportScope(root, Rename, () =>
            {
                checkedUnbranded = Exists(Original) && !Exists(Branded);
                return answer;
            }));
            Assert.That(checkedUnbranded, Is.True, "Unity checks the export under its own project name");
            Assert.That(error.Message, Does.Contain("cannot be appended").And.Contain("Builds/Backups"));
            Assert.That(Exists(Branded) && !Exists(Original), Is.True);
        }

        [Test]
        public void AFailingAppendCheckKeepsTheName()
        {
            Directory.CreateDirectory(Path.Combine(root, Branded));
            Assert.Throws<IOException>(() => new IosExportScope(root, Rename, () => throw new IOException("Expected test failure")));
            Assert.That(Exists(Branded) && !Exists(Original), Is.True);
        }

        [Test]
        public void AFailedAppendBuildGetsItsNameBack()
        {
            Directory.CreateDirectory(Path.Combine(root, Branded));
            Assert.Throws<IOException>(() =>
            {
                using var scope = new IosExportScope(root, Rename, () => CanAppendBuild.Yes);
                Assert.That(scope.Appending, Is.True);
                Assert.That(Exists(Original) && !Exists(Branded), Is.True);
                throw new IOException("Expected test failure");
            });
            Assert.That(Exists(Branded) && !Exists(Original), Is.True);
            Assert.That(renames, Is.EqualTo(new[] { "OurTaikoPlay>Unity-iPhone", "Unity-iPhone>OurTaikoPlay" }));
        }

        [Test]
        public void ASuccessfulAppendBuildIsLeftToThePostProcess()
        {
            Directory.CreateDirectory(Path.Combine(root, Branded));
            using (var scope = new IosExportScope(root, Rename, () => CanAppendBuild.Yes))
                Rename(IosExportScope.UnityName, PlayerBranding.ProductName); // What the post-process does.
            Assert.That(renames, Has.Count.EqualTo(2));
            Assert.That(Exists(Branded) && !Exists(Original), Is.True);
        }

        [Test]
        public void AFailedRenameBackIsLoggedWithoutHidingTheBuildError()
        {
            Directory.CreateDirectory(Path.Combine(root, Branded));
            bool failRename = false;
            void Fragile(string from, string to)
            {
                if (failRename) throw new UnauthorizedAccessException("Expected rename failure");
                Rename(from, to);
            }
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Could not rename Unity-iPhone.xcodeproj"));
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Expected rename failure"));
            Assert.Throws<IOException>(() =>
            {
                using var scope = new IosExportScope(root, Fragile, () => CanAppendBuild.Yes);
                failRename = true;
                throw new IOException("Expected test failure");
            });
        }

        [Test]
        public void BothProjectsPresentIsRefusedUntouched()
        {
            Directory.CreateDirectory(Path.Combine(root, Branded));
            Directory.CreateDirectory(Path.Combine(root, Original));
            Assert.Throws<BuildFailedException>(() => new IosExportScope(root, Rename, () => CanAppendBuild.Yes));
            Assert.That(renames, Is.Empty);
        }
    }
}
