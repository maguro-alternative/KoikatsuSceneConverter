using System.Collections.Generic;
using System.IO;
using KksSceneConv;
using static KksSceneConv.Tests.SceneBuilder;

namespace KksSceneConv.Tests
{
    public class TranscoderTests
    {
        const string KksTimeline = "<root><i owner=\"KKSPE\"/><i owner=\"KKSPE\"/><i owner=\"Timeline\"/></root>";
        const string KkTimeline = "<root><i owner=\"KKPE\"/><i owner=\"KKPE\"/><i owner=\"Timeline\"/></root>";

        /// <summary>A KKS scene exercising every difference the converter handles.</summary>
        static Options FullKks()
        {
            return new Options
            {
                Kks = true,
                Roots = new List<Obj>
                {
                    Character(Transcoder.MarkKks, "0.0.6"),
                    Folder("group", Item(), Text(), Light()),
                    Text(),
                    Camera(),
                },
                Background = @"bg\sub/sky.png",
                KkEx = TimelineKkEx(KksTimeline),
            };
        }

        /// <summary>What <see cref="FullKks"/> should look like after conversion.</summary>
        static Options FullKkExpected()
        {
            return new Options
            {
                Kks = false,
                Roots = new List<Obj>
                {
                    Character(Transcoder.MarkKk, "0.0.5"),
                    Folder("group", Item(), Light()),
                    Camera().WithKey(4),  // keys are preserved; the dropped Text had key 3
                },
                Background = "sky.png",
                KkEx = TimelineKkEx(KkTimeline),
            };
        }

        [Fact]
        public void Converting_a_KKS_scene_yields_exactly_the_equivalent_KK_scene()
        {
            var output = new Transcoder(Build(FullKks())).Scene(true);

            Assert.Equal(Build(FullKkExpected()), output);
        }

        [Fact]
        public void Conversion_reports_what_it_changed()
        {
            var t = new Transcoder(Build(FullKks()));
            t.Scene(true);

            Assert.True(t.IsKks);
            Assert.Equal(KksVersion, t.SrcVersion);
            Assert.Equal(KkVersion, t.OutVersion);
            Assert.Equal(Transcoder.TailMark, t.TailMarkFound);
            Assert.Equal(2, t.Stats.TextsDropped);
            Assert.Equal(1, t.Stats.Chars);
            Assert.Equal(1, t.Stats.Items);
            Assert.Equal(2, t.Stats.Kinds[7]);
            Assert.Equal("0.0.6->0.0.5", t.Stats.BlockVersions["Parameter"]);
            Assert.Equal(new[] { "owner=\"KKSPE\" x2" }, t.Stats.TimelineRenames);
        }

        [Fact]
        public void Converted_output_reparses_cleanly_as_KK()
        {
            var output = new Transcoder(Build(FullKks())).Scene(true);

            var check = new Transcoder(output);
            check.Scene(false);

            Assert.False(check.IsKks);
            Assert.Equal(KkVersion, check.SrcVersion);
            Assert.Equal(Transcoder.TailMark, check.TailMarkFound);
            Assert.False(check.Stats.Kinds.ContainsKey(7));
        }

        [Fact]
        public void PngLength_covers_the_thumbnail_up_to_IEND()
        {
            var t = new Transcoder(Build(FullKks()));
            t.Scene(false);

            Assert.Equal(8 + 12, t.PngLength);
        }

        [Fact]
        public void Scene_without_KKEx_block_is_converted_and_tail_copied()
        {
            var o = FullKks();
            o.KkEx = null;
            var e = FullKkExpected();
            e.KkEx = null;

            Assert.Equal(Build(e), new Transcoder(Build(o)).Scene(true));
        }

        [Theory]
        [InlineData("", "")]
        [InlineData("sky.png", "sky.png")]
        [InlineData("a/b/sky.png", "sky.png")]
        [InlineData(@"a\b\sky.png", "sky.png")]
        public void Background_is_reduced_to_the_file_name(string kks, string kk)
        {
            var o = new Options { Kks = true, Background = kks };
            var e = new Options { Kks = false, Background = kk };

            Assert.Equal(Build(e), new Transcoder(Build(o)).Scene(true));
        }

        [Fact]
        public void KK_scene_is_rejected_when_KKS_is_required()
        {
            var kk = Build(FullKkExpected());

            Assert.Throws<AlreadyKkException>(() => new Transcoder(kk).Scene(true));
        }

        [Fact]
        public void KK_scene_passes_through_unchanged_in_validation_mode()
        {
            var kk = Build(FullKkExpected());
            var t = new Transcoder(kk);

            Assert.Equal(kk, t.Scene(false));
            Assert.False(t.IsKks);
            Assert.Empty(t.Stats.TimelineRenames);
        }

        [Fact]
        public void Non_png_input_is_rejected()
        {
            var data = Build(FullKks());
            data[1] = (byte)'X';

            Assert.Throws<InvalidDataException>(() => new Transcoder(data).Scene(true));
        }

        [Fact]
        public void Truncated_input_throws_end_of_stream()
        {
            var full = Build(FullKks());
            var truncated = new byte[full.Length / 2];
            System.Array.Copy(full, truncated, truncated.Length);

            Assert.Throws<EndOfStreamException>(() => new Transcoder(truncated).Scene(true));
        }

        [Fact]
        public void Unknown_object_kind_is_rejected()
        {
            var o = new Options { Kks = true, Roots = { new Obj { Kind = 6, Body = (w, kks) => { } } } };

            Assert.Throws<InvalidDataException>(() => new Transcoder(Build(o)).Scene(true));
        }

        [Fact]
        public void Unknown_chara_mark_is_rejected()
        {
            var o = new Options { Kks = true, Roots = { Character("【SomethingElse】", "0.0.5") } };

            Assert.Throws<InvalidDataException>(() => new Transcoder(Build(o)).Scene(true));
        }

        [Fact]
        public void Missing_tail_marker_is_reported_not_thrown()
        {
            var data = Build(new Options { Kks = true });
            var cut = new byte[data.Length - 1];  // corrupt the last byte of 【KStudio】
            System.Array.Copy(data, cut, cut.Length);

            var t = new Transcoder(cut);
            t.Scene(false);

            Assert.NotEqual(Transcoder.TailMark, t.TailMarkFound);
        }
    }
}
