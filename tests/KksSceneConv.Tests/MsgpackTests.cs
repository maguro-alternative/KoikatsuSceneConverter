using System;
using System.Collections.Generic;
using System.IO;
using KksSceneConv;

namespace KksSceneConv.Tests
{
    public class MsgpackTests
    {
        static byte[] Hex(string s) { return Convert.FromHexString(s); }

        static object Decode(byte[] b)
        {
            int pos = 0;
            var v = Msgpack.Decode(b, ref pos);
            Assert.Equal(b.Length, pos);
            return v;
        }

        /// <summary>The encoder must reproduce the decoder's input, including
        /// non-minimal int widths that MessagePack-CSharp relies on for typing.</summary>
        [Theory]
        [InlineData("c0")]                  // nil
        [InlineData("c2")]                  // false
        [InlineData("c3")]                  // true
        [InlineData("00")]                  // positive fixint
        [InlineData("7f")]
        [InlineData("e0")]                  // negative fixint
        [InlineData("ff")]
        [InlineData("cc05")]                // uint8 holding a fixint-sized value
        [InlineData("cd0102")]              // uint16
        [InlineData("ce01020304")]          // uint32
        [InlineData("cf0102030405060708")]  // uint64
        [InlineData("d0ff")]                // int8 -1
        [InlineData("d1fffe")]              // int16
        [InlineData("d2fffffffe")]          // int32
        [InlineData("d3fffffffffffffffe")]  // int64
        [InlineData("ca3f800000")]          // float32 1.0
        [InlineData("cb3ff0000000000000")]  // float64 1.0
        [InlineData("a3616263")]            // fixstr "abc"
        [InlineData("c403010203")]          // bin8
        [InlineData("d40102")]              // fixext1
        [InlineData("d8" + "05" + "000102030405060708090a0b0c0d0e0f")]  // fixext16
        [InlineData("c7030a010203")]        // ext8
        [InlineData("93010203")]            // fixarray
        [InlineData("82a16101a16292c0c3")]  // fixmap with nested array
        public void Decode_then_encode_is_byte_identical(string hex)
        {
            var input = Hex(hex);

            Assert.Equal(input, Msgpack.Encode(Decode(input)));
        }

        [Theory]
        [InlineData(31, 0xA0 | 31)]
        [InlineData(32, 0xD9)]
        [InlineData(255, 0xD9)]
        [InlineData(256, 0xDA)]
        [InlineData(70000, 0xDB)]
        public void String_length_selects_the_minimal_header(int length, int firstByte)
        {
            var s = new string('x', length);

            var b = Msgpack.Encode(s);

            Assert.Equal(firstByte, b[0]);
            Assert.Equal(s, Decode(b));
        }

        [Theory]
        [InlineData(15, 0x9F)]
        [InlineData(16, 0xDC)]
        [InlineData(70000, 0xDD)]
        public void Array_length_selects_the_minimal_header(int length, int firstByte)
        {
            var list = new List<object>();
            for (int i = 0; i < length; i++) list.Add(null);

            var b = Msgpack.Encode(list);

            Assert.Equal(firstByte, b[0]);
            Assert.Equal(b, Msgpack.Encode(Decode(b)));
        }

        [Theory]
        [InlineData(0L, "00")]
        [InlineData(127L, "7f")]
        [InlineData(128L, "cc80")]
        [InlineData(65536L, "ce00010000")]
        [InlineData(-1L, "ff")]
        [InlineData(-32L, "e0")]
        [InlineData(-33L, "d0df")]
        [InlineData(-129L, "d1ff7f")]
        public void Plain_integers_use_the_smallest_encoding(long value, string hex)
        {
            Assert.Equal(Hex(hex), Msgpack.Encode(value));
        }

        [Fact]
        public void Decoded_values_have_the_expected_shape()
        {
            var map = Assert.IsType<MpMap>(Decode(Hex("82a16101a162cd0102")));

            var a = Assert.IsType<MpInt>(map.Get("a"));
            Assert.Equal(1, a.Value);
            var b = Assert.IsType<MpInt>(map.Get("b"));
            Assert.Equal(0x0102, b.Value);
            Assert.Equal(0xCD, b.Code);
            Assert.Null(map.Get("missing"));
        }

        [Fact]
        public void Map_Get_returns_the_last_duplicate_key()
        {
            var map = Assert.IsType<MpMap>(Decode(Hex("82a16101a16102")));

            Assert.Equal(2, Assert.IsType<MpInt>(map.Get("a")).Value);
            Assert.Equal(Hex("82a16101a16102"), Msgpack.Encode(map));  // both kept on re-encode
        }

        [Fact]
        public void Unsupported_type_byte_is_rejected()
        {
            Assert.Throws<InvalidDataException>(() => Decode(Hex("c1")));
        }

        [Theory]
        [InlineData("")]
        [InlineData("cd01")]
        [InlineData("a36162")]
        [InlineData("92c0")]
        public void Truncated_input_throws_end_of_stream(string hex)
        {
            Assert.Throws<EndOfStreamException>(() => Decode(Hex(hex)));
        }

        [Fact]
        public void Unknown_CLR_type_cannot_be_encoded()
        {
            Assert.Throws<InvalidDataException>(() => Msgpack.Encode(new object()));
        }
    }
}
