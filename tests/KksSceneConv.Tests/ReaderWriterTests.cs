using System.IO;
using KksSceneConv;

namespace KksSceneConv.Tests
{
    public class ReaderWriterTests
    {
        [Fact]
        public void Primitives_round_trip()
        {
            var w = new Writer();
            w.I32(-123456); w.I64(long.MinValue); w.F32(3.5f); w.Bool(true); w.Bool(false);

            var r = new Reader(w.ToArray());

            Assert.Equal(-123456, r.I32());
            Assert.Equal(long.MinValue, r.I64());
            Assert.Equal(3.5f, r.F32());
            Assert.True(r.Bool());
            Assert.False(r.Bool());
            Assert.Equal(0, r.Remaining);
        }

        [Fact]
        public void Primitives_are_little_endian_like_BinaryWriter()
        {
            var w = new Writer();
            w.I32(0x01020304);

            Assert.Equal(new byte[] { 4, 3, 2, 1 }, w.ToArray());
        }

        [Theory]
        [InlineData(0, new byte[] { 0x00 })]
        [InlineData(127, new byte[] { 0x7F })]
        [InlineData(128, new byte[] { 0x80, 0x01 })]
        [InlineData(300, new byte[] { 0xAC, 0x02 })]
        [InlineData(16384, new byte[] { 0x80, 0x80, 0x01 })]
        public void String_length_prefix_is_7bit_encoded(int length, byte[] prefix)
        {
            var s = new string('a', length);
            var w = new Writer();
            w.Str(s);
            var bytes = w.ToArray();

            Assert.Equal(prefix, bytes[..prefix.Length]);
            Assert.Equal(prefix.Length + length, bytes.Length);
            Assert.Equal(s, new Reader(bytes).Str());
        }

        [Fact]
        public void String_length_counts_UTF8_bytes_not_chars()
        {
            var w = new Writer();
            w.Str("【KStudio】");  // 【 and 】 are 3 bytes each in UTF-8
            var bytes = w.ToArray();

            Assert.Equal(13, bytes[0]);
            Assert.Equal("【KStudio】", new Reader(bytes).Str());
        }

        [Fact]
        public void Matches_BinaryWriter_output()
        {
            var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(42); bw.Write(7L); bw.Write(1.25f); bw.Write(true); bw.Write(new string('日', 60));
            }
            var w = new Writer();
            w.I32(42); w.I64(7L); w.F32(1.25f); w.Bool(true); w.Str(new string('日', 60));

            Assert.Equal(ms.ToArray(), w.ToArray());
        }

        [Fact]
        public void Reading_past_the_end_throws()
        {
            var r = new Reader(new byte[] { 1, 2, 3 });

            Assert.Throws<EndOfStreamException>(() => r.I32());
            Assert.Equal(0, r.P);  // position unchanged on failure
        }

        [Fact]
        public void Overlong_string_length_prefix_is_rejected()
        {
            var r = new Reader(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });

            Assert.Throws<InvalidDataException>(() => r.Str());
        }

        [Fact]
        public void Pushed_buffer_is_isolated_until_popped()
        {
            var w = new Writer();
            w.I32(1);
            w.Push();
            w.I32(2);
            var inner = w.Pop();
            w.I32(3);

            Assert.Equal(new byte[] { 2, 0, 0, 0 }, inner);
            Assert.Equal(new byte[] { 1, 0, 0, 0, 3, 0, 0, 0 }, w.ToArray());
        }
    }
}
