using System;
using System.Collections.Generic;
using System.Text;
using KksSceneConv;

namespace KksSceneConv.Tests
{
    public class PatchTests
    {
        // ---- PatchBlockVersions -------------------------------------------

        [Fact]
        public void Newer_block_version_is_lowered_to_what_KK_accepts()
        {
            var changed = new List<Tuple<string, string, string>>();

            var patched = Transcoder.PatchBlockVersions(SceneBuilder.BlockHeader("0.0.6"), changed);

            Assert.Equal(SceneBuilder.BlockHeader("0.0.5"), patched);
            Assert.Equal(new[] { Tuple.Create("Parameter", "0.0.6", "0.0.5") }, changed);
        }

        [Theory]
        [InlineData("0.0.5")]
        [InlineData("0.0.4")]
        public void Accepted_block_version_is_left_untouched(string version)
        {
            var header = SceneBuilder.BlockHeader(version);
            var changed = new List<Tuple<string, string, string>>();

            var patched = Transcoder.PatchBlockVersions(header, changed);

            Assert.Same(header, patched);
            Assert.Empty(changed);
        }

        [Fact]
        public void Header_without_known_blocks_is_left_untouched()
        {
            var header = new byte[] { 0x80 };  // empty msgpack map
            var changed = new List<Tuple<string, string, string>>();

            Assert.Same(header, Transcoder.PatchBlockVersions(header, changed));
        }

        // ---- PatchSceneTail -----------------------------------------------

        static byte[] Tail(byte[] kkex)
        {
            var w = new Writer();
            w.Str(Transcoder.TailMark);
            w.Str("KKEx"); w.I32(3); w.I32(kkex.Length); w.Raw(kkex);
            return w.ToArray();
        }

        [Fact]
        public void Timeline_owner_KKSPE_is_renamed_to_KKPE()
        {
            var tail = Tail(SceneBuilder.TimelineKkEx("<i owner=\"KKSPE\"/>"));
            var changes = new List<string>();

            var patched = Transcoder.PatchSceneTail(tail, s => { }, changes);

            Assert.Equal(Tail(SceneBuilder.TimelineKkEx("<i owner=\"KKPE\"/>")), patched);
            Assert.Equal(new[] { "owner=\"KKSPE\" x1" }, changes);
        }

        [Fact]
        public void Tail_without_KKSPE_is_returned_as_is()
        {
            var tail = Tail(SceneBuilder.TimelineKkEx("<i owner=\"KKPE\"/>"));
            var changes = new List<string>();

            Assert.Same(tail, Transcoder.PatchSceneTail(tail, s => { }, changes));
            Assert.Empty(changes);
        }

        [Fact]
        public void KKSPE_outside_the_timeline_entry_is_not_touched()
        {
            var other = new MpMap();
            other.Items.Add(new KeyValuePair<object, object>("otherPlugin", "owner=\"KKSPE\""));
            var tail = Tail(Msgpack.Encode(other));
            var changes = new List<string>();

            Assert.Same(tail, Transcoder.PatchSceneTail(tail, s => { }, changes));
            Assert.Empty(changes);
        }

        [Fact]
        public void Tail_without_KKEx_block_is_returned_as_is()
        {
            var w = new Writer();
            w.Str(Transcoder.TailMark);
            var tail = w.ToArray();

            Assert.Same(tail, Transcoder.PatchSceneTail(tail, s => { }, new List<string>()));
        }

        [Fact]
        public void Corrupt_KKEx_blob_is_copied_verbatim_with_a_warning()
        {
            // contains the needle, but is not valid msgpack (0xC1 is never used)
            var needle = Encoding.UTF8.GetBytes("owner=\"KKSPE\"");
            var blob = new byte[1 + needle.Length];
            blob[0] = 0xC1;
            needle.CopyTo(blob, 1);
            var tail = Tail(blob);
            var logs = new List<string>();
            var changes = new List<string>();

            var patched = Transcoder.PatchSceneTail(tail, logs.Add, changes);

            Assert.Same(tail, patched);
            Assert.Empty(changes);
            Assert.Contains(logs, l => l.StartsWith("warning:", StringComparison.Ordinal));
        }

        // ---- IndexOf ------------------------------------------------------

        [Theory]
        [InlineData(new byte[] { 1, 2, 3, 4 }, new byte[] { 3, 4 }, 0, 2)]
        [InlineData(new byte[] { 1, 2, 1, 2 }, new byte[] { 1, 2 }, 1, 2)]
        [InlineData(new byte[] { 1, 2, 3 }, new byte[] { 3, 4 }, 0, -1)]
        [InlineData(new byte[] { 1 }, new byte[] { 1, 2 }, 0, -1)]
        [InlineData(new byte[] { 1, 2 }, new byte[0], 1, 1)]
        public void IndexOf_finds_byte_sequences(byte[] hay, byte[] needle, int start, int expected)
        {
            Assert.Equal(expected, Transcoder.IndexOf(hay, needle, start));
        }
    }
}
