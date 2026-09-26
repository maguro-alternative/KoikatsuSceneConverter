using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KksSceneConv;

namespace KksSceneConv.Tests
{
    /// <summary>Scenes saved by the real game: <c>KKS_SCENES_DIR</c> if set, otherwise
    /// the committed fixtures in <c>tests/fixtures/real/</c>. With no scenes present
    /// every case is skipped, unless <c>KKS_REQUIRE_REAL_SCENES=1</c> (set in CI) turns
    /// that into a failure so missing fixtures cannot pass silently.</summary>
    public static class RealScenes
    {
        public const string EnvVar = "KKS_SCENES_DIR";
        public const string RequireEnvVar = "KKS_REQUIRE_REAL_SCENES";

        public static bool Required { get { return Environment.GetEnvironmentVariable(RequireEnvVar) == "1"; } }

        public static string Dir
        {
            get
            {
                string env = Environment.GetEnvironmentVariable(EnvVar);
                if (!string.IsNullOrEmpty(env)) return env;
                // bin/<config>/<tfm>/ -> repository root (the folder holding the .sln)
                for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                    if (File.Exists(Path.Combine(d.FullName, "KksSceneConv.sln")))
                        return Path.Combine(d.FullName, "tests", "fixtures", "real");
                return null;
            }
        }

        public static string[] Files
        {
            get
            {
                string dir = Dir;
                if (dir == null || !Directory.Exists(dir)) return new string[0];
                var files = Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                return files;
            }
        }

        public static IEnumerable<object[]> Data
        {
            get { return Files.Select(f => new object[] { Path.GetFileName(f) }); }
        }

        public static byte[] Read(string name) { return File.ReadAllBytes(Path.Combine(Dir, name)); }
    }

    /// <summary>A Theory over <see cref="RealScenes.Data"/> that reports itself as
    /// skipped (instead of failing with "no data") when no scenes are available.</summary>
    public sealed class RealSceneTheoryAttribute : TheoryAttribute
    {
        public RealSceneTheoryAttribute()
        {
            if (RealScenes.Files.Length == 0)
                Skip = "No real scenes: put KKS scene .png files in tests/fixtures/real/ or set " + RealScenes.EnvVar;
        }
    }

    public class RealSceneTests
    {
        static readonly byte[] KkspeOwner = Encoding.UTF8.GetBytes("owner=\"KKSPE\"");

        /// <summary>Fails the run when scenes are required but absent (the theories
        /// below would otherwise just be reported as skipped).</summary>
        [Fact]
        public void Real_scenes_are_present_when_required()
        {
            if (!RealScenes.Required) return;
            Assert.True(RealScenes.Files.Length > 0,
                "No real scenes in " + (RealScenes.Dir ?? "(unknown)") + " but " + RealScenes.RequireEnvVar + "=1");
        }

        /// <summary>Converts <paramref name="name"/>; returns null for scenes that are
        /// already KK (after checking they are rejected as such).</summary>
        static byte[] ConvertOrNull(string name, out Transcoder t)
        {
            var src = RealScenes.Read(name);
            t = new Transcoder(src);
            var probe = new Transcoder(src);
            probe.Scene(false);
            if (!probe.IsKks)
            {
                Assert.Throws<AlreadyKkException>(() => new Transcoder(src).Scene(true));
                return null;
            }
            return t.Scene(true);
        }

        [RealSceneTheory]
        [MemberData(nameof(RealScenes.Data), MemberType = typeof(RealScenes))]
        public void Source_parses_cleanly_to_the_tail_marker(string name)
        {
            var t = new Transcoder(RealScenes.Read(name));
            t.Scene(false);

            Assert.Equal(Transcoder.TailMark, t.TailMarkFound);
        }

        [RealSceneTheory]
        [MemberData(nameof(RealScenes.Data), MemberType = typeof(RealScenes))]
        public void Converted_scene_reparses_as_KK_with_the_same_objects_minus_Text(string name)
        {
            var output = ConvertOrNull(name, out var t);
            if (output == null) return;

            var check = new Transcoder(output);
            check.Scene(false);

            Assert.False(check.IsKks);
            Assert.Equal(Transcoder.KkSceneVersion, check.SrcVersion);
            Assert.Equal(Transcoder.TailMark, check.TailMarkFound);
            Assert.Equal(t.Stats.Chars, check.Stats.Chars);
            Assert.Equal(t.Stats.Items, check.Stats.Items);
            var expectedKinds = t.Stats.Kinds.Where(kv => kv.Key != 7).ToList();
            Assert.Equal(expectedKinds, check.Stats.Kinds.ToList());
            Assert.Empty(check.Stats.BlockVersions);  // nothing left for KK to skip
        }

        [RealSceneTheory]
        [MemberData(nameof(RealScenes.Data), MemberType = typeof(RealScenes))]
        public void Thumbnail_is_copied_byte_for_byte(string name)
        {
            var output = ConvertOrNull(name, out var t);
            if (output == null) return;

            var src = RealScenes.Read(name);
            Assert.True(t.PngLength > 0);
            Assert.Equal(src.AsSpan(0, t.PngLength).ToArray(), output.AsSpan(0, t.PngLength).ToArray());
        }

        [RealSceneTheory]
        [MemberData(nameof(RealScenes.Data), MemberType = typeof(RealScenes))]
        public void Output_is_final_KK_and_stable(string name)
        {
            var output = ConvertOrNull(name, out _);
            if (output == null) return;

            var logs = new List<string>();
            var check = new Transcoder(output, logs.Add);

            // Validating KK output must pass it through unchanged: nothing left to rewrite.
            Assert.Equal(output, check.Scene(false));
            Assert.DoesNotContain(logs, l => l.StartsWith("background ", StringComparison.Ordinal));
            Assert.Empty(check.Stats.TimelineRenames);
            Assert.Throws<AlreadyKkException>(() => new Transcoder(output).Scene(true));
        }

        /// <summary>The scene-level tail (【KStudio】 + KKEx); chara cards embedded earlier
        /// in the file may carry their own plugin data and are deliberately excluded.</summary>
        static byte[] SceneTail(byte[] scene)
        {
            var t = new Transcoder(scene);
            t.Scene(false);
            return scene.AsSpan(scene.Length - t.TailLength).ToArray();
        }

        [RealSceneTheory]
        [MemberData(nameof(RealScenes.Data), MemberType = typeof(RealScenes))]
        public void KKSPE_timeline_owners_are_all_renamed(string name)
        {
            var output = ConvertOrNull(name, out var t);
            if (output == null) return;

            if (Transcoder.IndexOf(SceneTail(RealScenes.Read(name)), KkspeOwner) >= 0)
                Assert.NotEmpty(t.Stats.TimelineRenames);
            Assert.Equal(-1, Transcoder.IndexOf(SceneTail(output), KkspeOwner));
        }
    }
}
