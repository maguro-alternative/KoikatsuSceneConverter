using System;
using System.IO;
using System.Linq;
using KksSceneConv;

namespace KksSceneConv.Tests
{
    /// <summary>File-system tests; each instance works in its own temp folder.</summary>
    public sealed class JobsTests : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "kks-tests-" + Guid.NewGuid().ToString("N"));

        public JobsTests() { Directory.CreateDirectory(root); }

        public void Dispose()
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }

        string Touch(string rel)
        {
            string p = Path.Combine(root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllBytes(p, new byte[] { 0 });
            return p;
        }

        string Rel(string p) { return Path.GetRelativePath(root, p).Replace('\\', '/'); }

        // ---- Jobs.Build ---------------------------------------------------

        [Fact]
        public void Top_level_only_skips_already_converted_and_non_png()
        {
            Touch("b.png"); Touch("a.png"); Touch("a_kk.png"); Touch("c_KK.png");
            Touch("notes.txt"); Touch("sub/d.png");

            var jobs = Jobs.Build(root, Path.Combine(root, "out"), "_kk", false);

            Assert.Equal(new[] { "a.png", "b.png" }, jobs.Select(j => Rel(j.Key)));
            Assert.Equal(new[] { "out/a_kk.png", "out/b_kk.png" }, jobs.Select(j => Rel(j.Value)));
        }

        [Fact]
        public void Recursion_mirrors_the_folder_structure()
        {
            Touch("a.png"); Touch("sub/deep/d.png");

            var jobs = Jobs.Build(root, Path.Combine(root, "out"), "_kk", true);

            Assert.Equal(new[] { "out/a_kk.png", "out/sub/deep/d_kk.png" }, jobs.Select(j => Rel(j.Value)).OrderBy(s => s));
        }

        [Fact]
        public void Output_folder_inside_the_input_is_not_rescanned()
        {
            Touch("a.png"); Touch("out/old.png");

            var jobs = Jobs.Build(root, Path.Combine(root, "out"), "_kk", true);

            Assert.Equal(new[] { "a.png" }, jobs.Select(j => Rel(j.Key)));
        }

        [Fact]
        public void Empty_suffix_converts_every_png()
        {
            Touch("a.png"); Touch("a_kk.png");

            var jobs = Jobs.Build(root, Path.Combine(root, "out"), "", false);

            Assert.Equal(new[] { "a.png", "a_kk.png" }, jobs.Select(j => Rel(j.Key)));
            Assert.Equal("out/a.png", Rel(jobs[0].Value));
        }

        [Fact]
        public void Missing_input_folder_throws()
        {
            Assert.Throws<DirectoryNotFoundException>(() => Jobs.Build(Path.Combine(root, "nope"), root, "_kk", false));
        }

        // ---- Program.WriteFileAtomic --------------------------------------

        [Fact]
        public void WriteFileAtomic_creates_folders_and_leaves_no_temp_file()
        {
            string dst = Path.Combine(root, "x", "y", "scene.png");

            Program.WriteFileAtomic(dst, new byte[] { 1, 2, 3 });

            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(dst));
            Assert.Equal(new[] { "scene.png" }, Directory.GetFiles(Path.GetDirectoryName(dst)).Select(Path.GetFileName));
        }

        [Fact]
        public void WriteFileAtomic_overwrites_an_existing_file()
        {
            string dst = Touch("scene.png");

            Program.WriteFileAtomic(dst, new byte[] { 7, 7 });

            Assert.Equal(new byte[] { 7, 7 }, File.ReadAllBytes(dst));
        }

        [Fact]
        public void WriteFileAtomic_failure_keeps_the_old_file_and_cleans_up()
        {
            string dst = Touch("scene.png");
            File.SetAttributes(dst, FileAttributes.ReadOnly);
            try
            {
                Assert.Throws<UnauthorizedAccessException>(() => Program.WriteFileAtomic(dst, new byte[] { 9 }));

                Assert.Equal(new byte[] { 0 }, File.ReadAllBytes(dst));
                Assert.False(File.Exists(dst + ".tmp"));
            }
            finally
            {
                File.SetAttributes(dst, FileAttributes.Normal);
            }
        }
    }
}
