using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KksSceneConv
{
    /// <summary>int that remembers its wire code (fixint / uint8 / int32 ...).
    /// MessagePack-CSharp's PrimitiveObjectFormatter picks the .NET type from the
    /// code, so the original code must be reproduced verbatim on re-encode.</summary>
    public sealed class MpInt
    {
        public long Value;
        public byte Code;
        public override string ToString() { return Value.ToString(); }
    }

    /// <summary>bytes that must be re-encoded as msgpack bin (not str).</summary>
    public sealed class MpBin
    {
        public byte[] Data;
    }

    public sealed class MpExt
    {
        public sbyte Code;
        public byte[] Data;
    }

    /// <summary>float that was stored as float32 (double is used for float64).</summary>
    public sealed class MpF32
    {
        public float Value;
    }

    /// <summary>Ordered map; keeps insertion order and duplicate keys so that an
    /// unchanged map re-encodes to the same bytes.</summary>
    public sealed class MpMap
    {
        public readonly List<KeyValuePair<object, object>> Items = new List<KeyValuePair<object, object>>();

        public object Get(string key)
        {
            object found = null;
            foreach (var kv in Items)
                if (kv.Key is string s && s == key) found = kv.Value; // last wins, like a dict
            return found;
        }
    }

    /// <summary>Minimal MessagePack codec (nil/bool/int/float/str/bin/array/map/ext).
    /// Only used to rewrite the Timeline XML inside the KKEx tail; the encoder
    /// reproduces the decoder's input byte-for-byte when nothing changed.</summary>
    public static class Msgpack
    {
        public static object Decode(byte[] buf, ref int pos)
        {
            if (pos >= buf.Length) throw new EndOfStreamException("msgpack: read past end");
            byte b = buf[pos++];
            if (b <= 0x7F) return new MpInt { Value = b, Code = b };
            if (b >= 0xE0) return new MpInt { Value = b - 0x100, Code = b };
            if (b <= 0x8F) return DecodeMap(buf, ref pos, b & 0x0F);
            if (b <= 0x9F) return DecodeArr(buf, ref pos, b & 0x0F);
            if (b <= 0xBF) return DecodeStr(buf, ref pos, b & 0x1F);
            switch (b)
            {
                case 0xC0: return null;
                case 0xC2: return false;
                case 0xC3: return true;
                case 0xC4: return DecodeBin(buf, ref pos, (int)ReadU(buf, ref pos, 1));
                case 0xC5: return DecodeBin(buf, ref pos, (int)ReadU(buf, ref pos, 2));
                case 0xC6: return DecodeBin(buf, ref pos, (int)ReadU(buf, ref pos, 4));
                case 0xC7: return DecodeExt(buf, ref pos, (int)ReadU(buf, ref pos, 1));
                case 0xC8: return DecodeExt(buf, ref pos, (int)ReadU(buf, ref pos, 2));
                case 0xC9: return DecodeExt(buf, ref pos, (int)ReadU(buf, ref pos, 4));
                case 0xCA:
                    {
                        var raw = Take(buf, ref pos, 4);
                        Array.Reverse(raw);
                        return new MpF32 { Value = BitConverter.ToSingle(raw, 0) };
                    }
                case 0xCB:
                    {
                        var raw = Take(buf, ref pos, 8);
                        Array.Reverse(raw);
                        return BitConverter.ToDouble(raw, 0);
                    }
                case 0xCC: case 0xCD: case 0xCE: case 0xCF:
                    {
                        int w = 1 << (b - 0xCC);
                        return new MpInt { Value = (long)ReadU(buf, ref pos, w), Code = b };
                    }
                case 0xD0: case 0xD1: case 0xD2: case 0xD3:
                    {
                        int w = 1 << (b - 0xD0);
                        return new MpInt { Value = ReadS(buf, ref pos, w), Code = b };
                    }
                case 0xD4: case 0xD5: case 0xD6: case 0xD7: case 0xD8:
                    return DecodeExt(buf, ref pos, 1 << (b - 0xD4));
                case 0xD9: return DecodeStr(buf, ref pos, (int)ReadU(buf, ref pos, 1));
                case 0xDA: return DecodeStr(buf, ref pos, (int)ReadU(buf, ref pos, 2));
                case 0xDB: return DecodeStr(buf, ref pos, (int)ReadU(buf, ref pos, 4));
                case 0xDC: return DecodeArr(buf, ref pos, (int)ReadU(buf, ref pos, 2));
                case 0xDD: return DecodeArr(buf, ref pos, (int)ReadU(buf, ref pos, 4));
                case 0xDE: return DecodeMap(buf, ref pos, (int)ReadU(buf, ref pos, 2));
                case 0xDF: return DecodeMap(buf, ref pos, (int)ReadU(buf, ref pos, 4));
            }
            throw new InvalidDataException(string.Format("msgpack: unsupported byte 0x{0:X2} at {1}", b, pos - 1));
        }

        static byte[] Take(byte[] buf, ref int pos, int n)
        {
            if (n < 0 || pos + n > buf.Length) throw new EndOfStreamException("msgpack: read past end at " + pos);
            var v = new byte[n];
            Buffer.BlockCopy(buf, pos, v, 0, n);
            pos += n;
            return v;
        }

        static ulong ReadU(byte[] buf, ref int pos, int w)
        {
            var raw = Take(buf, ref pos, w);
            ulong v = 0;
            foreach (var x in raw) v = (v << 8) | x;
            return v;
        }

        static long ReadS(byte[] buf, ref int pos, int w)
        {
            ulong u = ReadU(buf, ref pos, w);
            int shift = 64 - 8 * w;
            return ((long)(u << shift)) >> shift; // sign-extend
        }

        static string DecodeStr(byte[] buf, ref int pos, int n)
        {
            return Encoding.UTF8.GetString(Take(buf, ref pos, n));
        }

        static MpBin DecodeBin(byte[] buf, ref int pos, int n)
        {
            return new MpBin { Data = Take(buf, ref pos, n) };
        }

        static MpExt DecodeExt(byte[] buf, ref int pos, int n)
        {
            sbyte code = (sbyte)Take(buf, ref pos, 1)[0];
            return new MpExt { Code = code, Data = Take(buf, ref pos, n) };
        }

        static List<object> DecodeArr(byte[] buf, ref int pos, int n)
        {
            var list = new List<object>(n);
            for (int i = 0; i < n; i++) list.Add(Decode(buf, ref pos));
            return list;
        }

        static MpMap DecodeMap(byte[] buf, ref int pos, int n)
        {
            var m = new MpMap();
            for (int i = 0; i < n; i++)
            {
                var k = Decode(buf, ref pos);
                var v = Decode(buf, ref pos);
                m.Items.Add(new KeyValuePair<object, object>(k, v));
            }
            return m;
        }

        // -------------------------------------------------------------------

        public static byte[] Encode(object v)
        {
            var ms = new MemoryStream();
            Encode(v, ms);
            return ms.ToArray();
        }

        static void WriteBE(Stream o, ulong v, int w)
        {
            for (int i = w - 1; i >= 0; i--) o.WriteByte((byte)(v >> (8 * i)));
        }

        public static void Encode(object v, Stream o)
        {
            if (v == null) { o.WriteByte(0xC0); return; }
            if (v is bool bo) { o.WriteByte(bo ? (byte)0xC3 : (byte)0xC2); return; }
            if (v is MpInt mi)
            {
                byte c = mi.Code;
                if (c <= 0x7F || c >= 0xE0) { o.WriteByte(c); return; }
                if (c >= 0xCC && c <= 0xCF) { o.WriteByte(c); WriteBE(o, (ulong)mi.Value, 1 << (c - 0xCC)); return; }
                o.WriteByte(c); WriteBE(o, (ulong)mi.Value, 1 << (c - 0xD0)); return;
            }
            if (v is int || v is long)
            {
                long x = Convert.ToInt64(v);
                if (x >= 0 && x <= 0x7F) { o.WriteByte((byte)x); return; }
                if (x < 0 && x >= -32) { o.WriteByte((byte)(x & 0xFF)); return; }
                if (x >= 0)
                {
                    if (x < 0x100) { o.WriteByte(0xCC); WriteBE(o, (ulong)x, 1); }
                    else if (x < 0x10000) { o.WriteByte(0xCD); WriteBE(o, (ulong)x, 2); }
                    else if (x < 0x100000000L) { o.WriteByte(0xCE); WriteBE(o, (ulong)x, 4); }
                    else { o.WriteByte(0xCF); WriteBE(o, (ulong)x, 8); }
                }
                else
                {
                    if (x >= -0x80) { o.WriteByte(0xD0); WriteBE(o, (ulong)x, 1); }
                    else if (x >= -0x8000) { o.WriteByte(0xD1); WriteBE(o, (ulong)x, 2); }
                    else if (x >= -0x80000000L) { o.WriteByte(0xD2); WriteBE(o, (ulong)x, 4); }
                    else { o.WriteByte(0xD3); WriteBE(o, (ulong)x, 8); }
                }
                return;
            }
            if (v is MpF32 f32)
            {
                var raw = BitConverter.GetBytes(f32.Value);
                Array.Reverse(raw);
                o.WriteByte(0xCA); o.Write(raw, 0, 4); return;
            }
            if (v is double d)
            {
                var raw = BitConverter.GetBytes(d);
                Array.Reverse(raw);
                o.WriteByte(0xCB); o.Write(raw, 0, 8); return;
            }
            if (v is MpBin bin)
            {
                int n = bin.Data.Length;
                if (n < 0x100) { o.WriteByte(0xC4); WriteBE(o, (ulong)n, 1); }
                else if (n < 0x10000) { o.WriteByte(0xC5); WriteBE(o, (ulong)n, 2); }
                else { o.WriteByte(0xC6); WriteBE(o, (ulong)n, 4); }
                o.Write(bin.Data, 0, n); return;
            }
            if (v is string s)
            {
                var b = Encoding.UTF8.GetBytes(s);
                int n = b.Length;
                if (n < 32) o.WriteByte((byte)(0xA0 | n));
                else if (n < 0x100) { o.WriteByte(0xD9); WriteBE(o, (ulong)n, 1); }
                else if (n < 0x10000) { o.WriteByte(0xDA); WriteBE(o, (ulong)n, 2); }
                else { o.WriteByte(0xDB); WriteBE(o, (ulong)n, 4); }
                o.Write(b, 0, n); return;
            }
            if (v is MpExt ext)
            {
                int n = ext.Data.Length;
                switch (n)
                {
                    case 1: o.WriteByte(0xD4); break;
                    case 2: o.WriteByte(0xD5); break;
                    case 4: o.WriteByte(0xD6); break;
                    case 8: o.WriteByte(0xD7); break;
                    case 16: o.WriteByte(0xD8); break;
                    default:
                        if (n < 0x100) { o.WriteByte(0xC7); WriteBE(o, (ulong)n, 1); }
                        else if (n < 0x10000) { o.WriteByte(0xC8); WriteBE(o, (ulong)n, 2); }
                        else { o.WriteByte(0xC9); WriteBE(o, (ulong)n, 4); }
                        break;
                }
                o.WriteByte((byte)ext.Code);
                o.Write(ext.Data, 0, n); return;
            }
            if (v is List<object> list)
            {
                int n = list.Count;
                if (n < 16) o.WriteByte((byte)(0x90 | n));
                else if (n < 0x10000) { o.WriteByte(0xDC); WriteBE(o, (ulong)n, 2); }
                else { o.WriteByte(0xDD); WriteBE(o, (ulong)n, 4); }
                foreach (var x in list) Encode(x, o);
                return;
            }
            if (v is MpMap map)
            {
                int n = map.Items.Count;
                if (n < 16) o.WriteByte((byte)(0x80 | n));
                else if (n < 0x10000) { o.WriteByte(0xDE); WriteBE(o, (ulong)n, 2); }
                else { o.WriteByte(0xDF); WriteBE(o, (ulong)n, 4); }
                foreach (var kv in map.Items) { Encode(kv.Key, o); Encode(kv.Value, o); }
                return;
            }
            throw new InvalidDataException("msgpack: cannot encode " + v.GetType().Name);
        }
    }
}
