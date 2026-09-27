using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// Down-converts a Koikatsu Sunshine (KKS) Studio scene .png so that Koikatsu
// (KK) CharaStudio can load it.
//
// Why KK cannot read KKS scenes (verified by decompiling both Assembly-CSharp):
//   * KK SceneInfo.Load has no version guard; it reads the stream with the KK
//     layout (1.0.4.2) while KKS writes 1.1.2.1 with a few extra fields.
//   * Embedded chara cards carry the mark "【KoiKatuCharaSun】"; KK rejects it.
//   * OIItemInfo gained `animePattern` (int) between `no` and `animeSpeed`.
//   * SceneInfo gained `shaderType` (int) and a MessagePack `SkyInfo` blob.
//   * Object kind 7 (OITextInfo) does not exist in KK.
//   * KKS stores `background` as a path, KK wants a bare file name.
//   * KK skips chara-card blocks whose BlockHeader version is newer than it
//     knows (KKS Parameter 0.0.6 > KK 0.0.5) -> name / personality vanish.
//   * KKSPE registers Timeline interpolables with owner="KKSPE"; KK's Timeline
//     only knows "KKPE" and silently drops them.
// Everything else is byte-identical between the two games and is copied verbatim.

namespace KksSceneConv
{
    /// <summary>System.Version-like value that compares like C# (missing parts = 0).</summary>
    public readonly struct Ver : IComparable<Ver>, IEquatable<Ver>
    {
        readonly int p0, p1, p2, p3;

        public Ver(string s)
        {
            var parts = s.Split('.');
            var v = new int[4];
            for (int i = 0; i < parts.Length; i++)
            {
                int x = int.Parse(parts[i], CultureInfo.InvariantCulture);
                if (i < 4) v[i] = x;
            }
            p0 = v[0]; p1 = v[1]; p2 = v[2]; p3 = v[3];
        }

        public int CompareTo(Ver o)
        {
            int c = p0.CompareTo(o.p0); if (c != 0) return c;
            c = p1.CompareTo(o.p1); if (c != 0) return c;
            c = p2.CompareTo(o.p2); if (c != 0) return c;
            return p3.CompareTo(o.p3);
        }

        public bool Equals(Ver o) { return CompareTo(o) == 0; }
        public override bool Equals(object obj) { return obj is Ver v && Equals(v); }
        public override int GetHashCode() { return HashCode.Combine(p0, p1, p2, p3); }
        public static bool operator >=(Ver a, Ver b) { return a.CompareTo(b) >= 0; }
        public static bool operator <=(Ver a, Ver b) { return a.CompareTo(b) <= 0; }
        public static bool operator >(Ver a, Ver b) { return a.CompareTo(b) > 0; }
        public static bool operator <(Ver a, Ver b) { return a.CompareTo(b) < 0; }
        public static bool operator ==(Ver a, Ver b) { return a.CompareTo(b) == 0; }
        public static bool operator !=(Ver a, Ver b) { return a.CompareTo(b) != 0; }
        public override string ToString() { return p0 + "." + p1 + "." + p2 + "." + p3; }
    }

    /// <summary>BinaryReader-compatible reader over a byte array.</summary>
    public sealed class Reader
    {
        public readonly byte[] B;
        public int P;

        public Reader(byte[] data) { B = data; }

        public int Remaining { get { return B.Length - P; } }

        public byte[] Raw(int n)
        {
            if (n < 0 || P + n > B.Length)
                throw new EndOfStreamException("read past end at " + P + " (+" + n + ")");
            var v = new byte[n];
            Buffer.BlockCopy(B, P, v, 0, n);
            P += n;
            return v;
        }

        public int I32() { return BinaryPrimitives.ReadInt32LittleEndian(Raw(4)); }
        public long I64() { return BinaryPrimitives.ReadInt64LittleEndian(Raw(8)); }
        public float F32() { return BinaryPrimitives.ReadSingleLittleEndian(Raw(4)); }
        public bool Bool() { return Raw(1)[0] != 0; }

        /// <summary>BinaryReader.ReadString: 7-bit encoded byte length + UTF-8.</summary>
        public string Str()
        {
            int n = 0, shift = 0;
            while (true)
            {
                int c = Raw(1)[0];
                n |= (c & 0x7F) << shift;
                if ((c & 0x80) == 0) break;
                shift += 7;
                if (shift > 35) throw new InvalidDataException("bad 7-bit string length at " + P);
            }
            return Encoding.UTF8.GetString(Raw(n));
        }
    }

    /// <summary>BinaryWriter-compatible writer with a buffer stack so that a whole
    /// object can be written and then discarded (used to drop Text objects).</summary>
    public sealed class Writer
    {
        readonly Stack<MemoryStream> stack = new Stack<MemoryStream>();

        public Writer() { stack.Push(new MemoryStream()); }

        public MemoryStream Out { get { return stack.Peek(); } }
        public void Push() { stack.Push(new MemoryStream()); }
        public byte[] Pop() { return stack.Pop().ToArray(); }
        public byte[] ToArray() { return Out.ToArray(); }

        public void Raw(byte[] b) { Out.Write(b, 0, b.Length); }

        public void I32(int v)
        {
            var b = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, v); Out.Write(b, 0, 4);
        }

        public void I64(long v)
        {
            var b = new byte[8]; BinaryPrimitives.WriteInt64LittleEndian(b, v); Out.Write(b, 0, 8);
        }

        public void F32(float v)
        {
            var b = new byte[4]; BinaryPrimitives.WriteSingleLittleEndian(b, v); Out.Write(b, 0, 4);
        }

        public void Bool(bool v) { Out.WriteByte(v ? (byte)1 : (byte)0); }

        public void Str(string s)
        {
            var data = Encoding.UTF8.GetBytes(s);
            int n = data.Length;
            while (true)
            {
                int c = n & 0x7F;
                n >>= 7;
                if (n != 0) Out.WriteByte((byte)(c | 0x80));
                else { Out.WriteByte((byte)c); break; }
            }
            Out.Write(data, 0, data.Length);
        }
    }

    /// <summary>Thrown when the input is already a KK-layout scene (nothing to convert).</summary>
    public sealed class AlreadyKkException : Exception
    {
        public AlreadyKkException(string msg) : base(msg) { }
    }

    /// <summary>Thrown when the file is not a Studio scene at all (a plain picture, a
    /// character / coordinate card, ...). Batch conversion skips these instead of failing.</summary>
    public sealed class NotASceneException : Exception
    {
        public NotASceneException(string msg) : base("not a Studio scene: " + msg) { }
    }

    public sealed class SceneStats
    {
        public int Chars, Items, TextsDropped;
        public readonly SortedDictionary<int, int> Kinds = new SortedDictionary<int, int>();
        public readonly SortedDictionary<string, string> BlockVersions = new SortedDictionary<string, string>(StringComparer.Ordinal);
        public readonly List<string> TimelineRenames = new List<string>();

        public string KindsText()
        {
            var sb = new StringBuilder();
            foreach (var kv in Kinds)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(Transcoder.KindName(kv.Key)).Append('=').Append(kv.Value);
            }
            return sb.ToString();
        }

        public string BlockVersionsText()
        {
            if (BlockVersions.Count == 0) return "none";
            var sb = new StringBuilder();
            foreach (var kv in BlockVersions)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(kv.Key).Append(' ').Append(kv.Value);
            }
            return sb.ToString();
        }

        public override string ToString()
        {
            return "chars=" + Chars + " items=" + Items + " texts_dropped=" + TextsDropped
                + " kinds={" + KindsText() + "} block_versions={" + BlockVersionsText() + "}"
                + (TimelineRenames.Count > 0 ? " timeline_owner_renames=[" + string.Join(", ", TimelineRenames) + "]" : "");
        }
    }

    /// <summary>Reads the KKS layout, writes the KK layout.</summary>
    public sealed class Transcoder
    {
        public const string KkSceneVersion = "1.0.4.2";
        public const string MarkKks = "【KoiKatuCharaSun】";
        public const string MarkKk = "【KoiKatuChara】";
        public const string TailMark = "【KStudio】";

        static readonly Dictionary<int, string> KindNames = new Dictionary<int, string>
        {
            { 0, "Character" }, { 1, "Item" }, { 2, "Light" }, { 3, "Folder" }, { 4, "Route" }, { 5, "Camera" }, { 7, "Text" },
        };

        public static string KindName(int kind)
        {
            string s;
            return KindNames.TryGetValue(kind, out s) ? s : kind.ToString();
        }

        // Highest chara-card block versions KK accepts (ChaFileDefine in KK's Assembly-CSharp).
        static readonly KeyValuePair<string, string>[] KkBlockVersions =
        {
            new KeyValuePair<string, string>("Custom", "0.0.0"),
            new KeyValuePair<string, string>("Coordinate", "0.0.0"),
            new KeyValuePair<string, string>("Parameter", "0.0.5"),
            new KeyValuePair<string, string>("Status", "0.0.0"),
        };

        // Timeline interpolable owners that are named differently in KKS plugins.
        static readonly KeyValuePair<string, string>[] TimelineOwnerRenames =
        {
            new KeyValuePair<string, string>("owner=\"KKSPE\"", "owner=\"KKPE\""),
        };

        readonly Reader r;
        readonly Writer w;
        readonly Action<string> log;
        Ver srcVer;

        public readonly SceneStats Stats = new SceneStats();
        public string SrcVersion { get; private set; }
        public string OutVersion { get; private set; }
        public bool IsKks { get; private set; }
        /// <summary>Length of the leading PNG image (up to and including IEND).</summary>
        public int PngLength { get; private set; }
        public string TailMarkFound { get; private set; }
        public int TailLength { get; private set; }

        public Transcoder(byte[] data, Action<string> log = null)
        {
            r = new Reader(data);
            w = new Writer();
            this.log = log ?? (s => { });
        }

        // ---- primitive copy helpers -------------------------------------
        int CI32() { int v = r.I32(); w.I32(v); return v; }
        long CI64() { long v = r.I64(); w.I64(v); return v; }
        float CF32() { float v = r.F32(); w.F32(v); return v; }
        bool CBool() { bool v = r.Bool(); w.Bool(v); return v; }
        string CStr() { string v = r.Str(); w.Str(v); return v; }
        byte[] CRaw(int n) { var v = r.Raw(n); w.Raw(v); return v; }
        byte[] CLenBytes() { int n = CI32(); return CRaw(n); }
        void CVec3() { CF32(); CF32(); CF32(); }
        void CChangeAmount() { CVec3(); CVec3(); CVec3(); }
        bool VGe(string s) { return srcVer >= new Ver(s); }

        // ---- ObjectInfo family ------------------------------------------
        int ObjectBase(bool other = true)
        {
            int key = CI32();
            CChangeAmount();
            if (other)
            {
                CI32();  // treeState
                CBool(); // visible
            }
            return key;
        }

        void PatternInfo() { CI32(); CStr(); CBool(); CStr(); CF32(); }

        void Item()
        {
            ObjectBase();
            int g = CI32(), c = CI32(), n = CI32();
            if (VGe("1.1.1.0")) r.I32();  // animePattern - KK does not have it
            CF32();  // animeSpeed
            int colors = VGe("0.0.3") ? 8 : 7;
            for (int i = 0; i < colors; i++) CStr();  // color json
            for (int i = 0; i < 3; i++) PatternInfo();
            CF32();  // alpha
            if (VGe("0.0.4")) { CStr(); CF32(); }          // lineColor, lineWidth
            if (VGe("0.0.7")) { CStr(); CF32(); CF32(); }  // emissionColor, emissionPower, lightCancel
            if (VGe("0.0.6")) PatternInfo();               // panel
            CBool();  // enableFK
            int nb = CI32();
            for (int i = 0; i < nb; i++)
            {
                CStr();
                ObjectBase(false);  // OIBoneInfo
            }
            if (VGe("1.0.1")) CBool();  // enableDynamicBone
            CF32();  // animeNormalizedTime
            LoadChild();
            Stats.Items++;
            log("      item group=" + g + " cat=" + c + " no=" + n);
        }

        string CharaCard()
        {
            CI32();  // product
            string mark = r.Str();
            if (mark == MarkKks) w.Str(MarkKk);
            else if (mark == MarkKk) w.Str(mark);
            else throw new InvalidDataException("unexpected chara mark \"" + mark + "\"");
            CStr();       // ChaFileVersion
            CLenBytes();  // face png
            var header = r.Raw(r.I32());  // BlockHeader (msgpack)
            var changed = new List<Tuple<string, string, string>>();
            header = PatchBlockVersions(header, changed);
            foreach (var t in changed) Stats.BlockVersions[t.Item1] = t.Item2 + "->" + t.Item3;
            w.I32(header.Length);
            w.Raw(header);
            long total = CI64();
            CRaw(checked((int)total));  // all blocks incl. KKEx (ES patches the size sum)
            return mark;
        }

        void Character()
        {
            ObjectBase();
            int sex = CI32();
            string mark = CharaCard();
            int n;
            n = CI32(); for (int i = 0; i < n; i++) { CI32(); ObjectBase(false); }  // bones
            n = CI32(); for (int i = 0; i < n; i++) { CI32(); ObjectBase(false); }  // ikTarget
            n = CI32(); for (int i = 0; i < n; i++) { CI32(); LoadChild(); }        // child per accessory point
            CI32();                     // kinematicMode
            CI32(); CI32(); CI32();     // animeInfo
            CI32(); CI32();             // handPtn
            CF32();                     // nipple
            CRaw(5);                    // siru
            CF32();                     // mouthOpen
            CBool();                    // lipSync
            ObjectBase(false);          // lookAtTarget
            CBool();                    // enableIK
            for (int i = 0; i < 5; i++) CBool();
            CBool();                    // enableFK
            for (int i = 0; i < 7; i++) CBool();
            int expr = VGe("0.0.9") ? 8 : 4;
            for (int i = 0; i < expr; i++) CBool();  // expression
            CF32(); CF32();             // animeSpeed, animePattern(float)
            CBool(); CBool();           // animeOptionVisible, isAnimeForceLoop
            n = CI32(); for (int i = 0; i < n; i++) { CI32(); CI32(); CI32(); }  // voiceCtrl list
            CI32();                     // voice repeat
            CBool(); CF32(); CBool();   // visibleSon, sonLength, visibleSimple
            CStr();                     // simpleColor
            CF32(); CF32();             // animeOptionParam
            CLenBytes();                // neckByteData
            CLenBytes();                // eyesByteData
            CF32();                     // animeNormalizedTime
            n = CI32(); for (int i = 0; i < n; i++) { CI32(); CI32(); }  // dicAccessGroup
            n = CI32(); for (int i = 0; i < n; i++) { CI32(); CI32(); }  // dicAccessNo
            Stats.Chars++;
            log("      character sex=" + sex + " mark=" + mark);
        }

        void Light()
        {
            ObjectBase();
            CI32();
            for (int i = 0; i < 4; i++) CF32();  // color rgba
            CF32(); CF32(); CF32();
            CBool(); CBool(); CBool();
        }

        void Folder()
        {
            ObjectBase();
            string name = CStr();
            log("      folder \"" + name + "\"");
            LoadChild();
        }

        void Camera()
        {
            ObjectBase();
            CStr(); CBool();
        }

        void RoutePoint()
        {
            ObjectBase(false);
            CF32(); CI32();  // speed, easeType
            if (srcVer == new Ver("1.0.3")) CBool();
            if (VGe("1.0.4.1"))
            {
                CI32();             // connection
                ObjectBase(false);  // aidInfo
                CBool();            // aidInfo.isInit
            }
            if (VGe("1.0.4.2")) CBool();  // link
        }

        void Route()
        {
            ObjectBase();
            CStr();
            LoadChild();
            int n = CI32();
            for (int i = 0; i < n; i++) RoutePoint();
            if (VGe("1.0.3")) { CBool(); CBool(); CBool(); }
            if (VGe("1.0.4")) CI32();
            if (VGe("1.0.4.1")) CStr();
        }

        void Text()
        {
            // Parsed into the current (throw-away) buffer; caller discards it.
            ObjectBase();
            CI32(); CStr(); CStr(); CF32();
            CLenBytes();
        }

        void ObjectBody(int kind)
        {
            int c;
            Stats.Kinds[kind] = (Stats.Kinds.TryGetValue(kind, out c) ? c : 0) + 1;
            switch (kind)
            {
                case 0: Character(); break;
                case 1: Item(); break;
                case 2: Light(); break;
                case 3: Folder(); break;
                case 4: Route(); break;
                case 5: Camera(); break;
                case 7: Text(); break;
                default: throw new InvalidDataException("unknown object kind " + kind + " at " + r.P);
            }
        }

        /// <summary>ObjectInfoAssist.LoadChild: count, then (kind, body)*. Drops kind 7.</summary>
        void LoadChild()
        {
            int n = r.I32();
            var kept = new List<byte[]>();
            for (int i = 0; i < n; i++)
            {
                int kind = r.I32();
                w.Push();
                w.I32(kind);
                ObjectBody(kind);
                var buf = w.Pop();
                if (kind == 7) Stats.TextsDropped++;
                else kept.Add(buf);
            }
            w.I32(kept.Count);
            foreach (var buf in kept) w.Raw(buf);
        }

        // ---- SceneInfo ---------------------------------------------------
        void CameraData()
        {
            int ver = CI32();
            for (int i = 0; i < 6; i++) CF32();
            if (ver == 1) CF32();
            else CVec3();
            CF32();  // parse
        }

        void LightInfo(bool isMap)
        {
            CStr(); CF32(); CF32(); CF32(); CBool();
            if (isMap) CI32();  // LightType
        }

        /// <summary>Transcode the whole file. With requireKks=false a KK-layout file is
        /// accepted too (the version guards then read the KK layout), which turns
        /// this into a structural validator for converter output.</summary>
        public byte[] Scene(bool requireKks = true)
        {
            // --- PNG ---
            if (r.Remaining < 8) throw new NotASceneException("file too small (" + r.Remaining + " bytes)");
            var sig = CRaw(8);
            if (!(sig[0] == 0x89 && sig[1] == (byte)'P' && sig[2] == (byte)'N' && sig[3] == (byte)'G'
                  && sig[4] == 0x0D && sig[5] == 0x0A && sig[6] == 0x1A && sig[7] == 0x0A))
                throw new NotASceneException(DescribeNonScene(r.B, 0) ?? "not a PNG file");  // cards may omit the picture
            while (true)
            {
                if (r.P + 8 > r.B.Length) throw new EndOfStreamException("truncated PNG chunk at " + r.P);
                int length = BinaryPrimitives.ReadInt32BigEndian(new ReadOnlySpan<byte>(r.B, r.P, 4));
                bool iend = r.B[r.P + 4] == (byte)'I' && r.B[r.P + 5] == (byte)'E' && r.B[r.P + 6] == (byte)'N' && r.B[r.P + 7] == (byte)'D';
                CRaw(12 + length);
                if (iend) break;
            }
            PngLength = r.P;
            // --- header ---
            string srcVerStr = ReadSceneVersion();
            srcVer = new Ver(srcVerStr);
            SrcVersion = srcVerStr;
            IsKks = srcVer >= new Ver("1.1.0.0");
            if (requireKks && !IsKks)
                throw new AlreadyKkException("scene version " + srcVer + " is already KK-compatible (< 1.1.0.0); nothing to do");
            OutVersion = IsKks ? KkSceneVersion : srcVerStr;
            log("scene version " + srcVer + " -> " + OutVersion);
            w.Str(OutVersion);
            // --- root objects: (key, kind, body)* with kind-7 filtering ---
            int n = r.I32();
            var kept = new List<byte[]>();
            for (int i = 0; i < n; i++)
            {
                int key = r.I32();
                int kind = r.I32();
                log("  root key=" + key + " kind=" + kind + " (" + (KindNames.ContainsKey(kind) ? KindNames[kind] : "?") + ")");
                w.Push();
                w.I32(key); w.I32(kind);
                ObjectBody(kind);
                var buf = w.Pop();
                if (kind == 7) Stats.TextsDropped++;
                else kept.Add(buf);
            }
            w.I32(kept.Count);
            foreach (var buf in kept) w.Raw(buf);
            // --- scene-wide settings ---
            CI32();            // map
            CChangeAmount();   // caMap
            CI32();            // sunLightType
            CBool();           // mapOption
            CI32();            // aceNo
            if (VGe("0.0.2")) CF32();  // aceBlend
            if (srcVer <= new Ver("0.0.1")) { CBool(); CF32(); CStr(); }
            if (VGe("0.0.2")) { CBool(); CStr(); CF32(); }  // AOE
            CBool(); CF32(); CF32();                        // bloom
            if (VGe("0.0.2")) CF32();  // bloomThreshold
            if (srcVer <= new Ver("0.0.1")) CBool();
            CBool(); CF32(); CF32();   // depth
            CBool();                   // vignette
            if (srcVer <= new Ver("0.0.1")) CF32();
            CBool();                   // fog
            if (VGe("0.0.2")) { CStr(); CF32(); CF32(); }
            CBool();                   // sunShafts
            if (VGe("0.0.2")) { CStr(); CStr(); }
            if (VGe("0.0.4")) CI32();  // sunCaster
            if (VGe("0.0.2")) CBool(); // enableShadow
            if (VGe("0.0.4")) { CBool(); CBool(); CF32(); CStr(); }
            if (VGe("0.0.5")) { CF32(); CI32(); CF32(); }  // lineWidthG, rampG, ambientShadowG
            if (VGe("1.1.0.0")) r.I32();          // shaderType (KKS only)
            if (VGe("1.1.2.0")) r.Raw(r.I32());   // SkyInfo msgpack (KKS only)
            CameraData();  // cameraSaveData
            for (int i = 0; i < 10; i++) CameraData();
            LightInfo(false);  // charaLight
            LightInfo(true);   // mapLight
            CI32(); CI32(); CBool();  // bgmCtrl
            CI32(); CI32(); CBool();  // envCtrl
            CI32(); CStr(); CBool();  // outsideSoundCtrl
            string bg = r.Str();
            string bgKk = bg;
            if (!string.IsNullOrEmpty(bg))
            {
                string norm = bg.Replace('\\', '/');
                int slash = norm.LastIndexOf('/');
                bgKk = slash >= 0 ? norm.Substring(slash + 1) : norm;
            }
            w.Str(bgKk);
            if (bg != bgKk) log("background \"" + bg + "\" -> \"" + bgKk + "\"");
            CStr();  // frame
            // --- tail: "【KStudio】" + ExtensibleSaveFormat block, identical in KK ---
            var tail = r.Raw(r.Remaining);
            string mark = null;
            try { mark = new Reader(tail).Str(); } catch { mark = null; }
            TailMarkFound = mark;
            TailLength = tail.Length;
            if (mark != TailMark)
            {
                log("warning: expected \"" + TailMark + "\" after frame, got " + (mark == null ? "null" : "\"" + mark + "\""));
            }
            else if (IsKks)
            {
                var changes = new List<string>();
                tail = PatchSceneTail(tail, log, changes);
                if (changes.Count > 0)
                {
                    Stats.TimelineRenames.AddRange(changes);
                    log("timeline owners renamed: " + string.Join(", ", changes));
                }
            }
            w.Raw(tail);
            return w.ToArray();
        }

        /// <summary>The scene version string that follows the PNG. Anything else means
        /// the file is not a Studio scene; say what it looks like instead.</summary>
        string ReadSceneVersion()
        {
            if (r.Remaining == 0)
                throw new NotASceneException("nothing follows the PNG image (a plain picture)");
            int start = r.P;
            string v = null;
            try { v = r.Str(); }
            catch (EndOfStreamException) { }
            catch (InvalidDataException) { }
            if (v != null && IsVersionString(v)) return v;
            throw new NotASceneException(DescribeNonScene(r.B, start) ?? "the data after the PNG image is not a scene header");
        }

        /// <summary>"1.1.2.1"-style: 2 to 4 dot-separated numbers.</summary>
        static bool IsVersionString(string s)
        {
            var parts = s.Split('.');
            if (parts.Length < 2 || parts.Length > 4) return false;
            foreach (var p in parts)
            {
                if (p.Length == 0 || p.Length > 9) return false;
                foreach (char ch in p) if (ch < '0' || ch > '9') return false;
            }
            return true;
        }

        /// <summary>Character and coordinate cards store int32 productNo + a mark string,
        /// usually after a PNG picture but sometimes without one. Returns what the data at
        /// <paramref name="start"/> looks like, or null when it is not recognised.</summary>
        static string DescribeNonScene(byte[] data, int start)
        {
            try
            {
                var p = new Reader(data) { P = start };
                p.I32();  // productNo
                string mark = p.Str();
                if (mark.StartsWith("【KoiKatuChara", StringComparison.Ordinal)) return "this is a character card (" + mark + ")";
                if (mark.StartsWith("【KoiKatuClothes", StringComparison.Ordinal)) return "this is a coordinate card (" + mark + ")";
            }
            catch (EndOfStreamException) { }
            catch (InvalidDataException) { }
            return null;
        }

        // ---- patches -----------------------------------------------------

        static byte[] Fixstr(string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            if (b.Length > 31) throw new ArgumentException("fixstr too long");
            var o = new byte[b.Length + 1];
            o[0] = (byte)(0xA0 | b.Length);
            Buffer.BlockCopy(b, 0, o, 1, b.Length);
            return o;
        }

        static byte[] Concat(params byte[][] parts)
        {
            int n = 0;
            foreach (var p in parts) n += p.Length;
            var o = new byte[n];
            int at = 0;
            foreach (var p in parts) { Buffer.BlockCopy(p, 0, o, at, p.Length); at += p.Length; }
            return o;
        }

        public static int IndexOf(byte[] hay, byte[] needle, int start = 0)
        {
            if (needle.Length == 0) return start;
            int last = hay.Length - needle.Length;
            for (int i = start; i <= last; i++)
            {
                if (hay[i] != needle[0]) continue;
                int j = 1;
                while (j < needle.Length && hay[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }

        /// <summary>Rewrite `version` of known blocks inside a ChaFile BlockHeader
        /// (msgpack map {lstInfo:[{name,version,pos,size}...]}) so KK does not skip
        /// them. Works on the raw bytes: `name` and `version` are always short fixstr.</summary>
        public static byte[] PatchBlockVersions(byte[] header, List<Tuple<string, string, string>> changed)
        {
            var outb = header;
            foreach (var kv in KkBlockVersions)
            {
                var needle = Concat(new byte[] { 0xA4 }, Encoding.ASCII.GetBytes("name"), Fixstr(kv.Key),
                                    new byte[] { 0xA7 }, Encoding.ASCII.GetBytes("version"));
                int i = IndexOf(outb, needle);
                if (i < 0) continue;
                int j = i + needle.Length;
                if (j >= outb.Length) continue;
                byte tag = outb[j];
                if ((tag & 0xE0) != 0xA0) continue;  // not a fixstr; leave untouched
                int n = tag & 0x1F;
                if (j + 1 + n > outb.Length) continue;
                string cur = Encoding.UTF8.GetString(outb, j + 1, n);
                if (new Ver(cur) > new Ver(kv.Value))
                {
                    var rep = Fixstr(kv.Value);
                    var nb = new byte[outb.Length - (1 + n) + rep.Length];
                    Buffer.BlockCopy(outb, 0, nb, 0, j);
                    Buffer.BlockCopy(rep, 0, nb, j, rep.Length);
                    Buffer.BlockCopy(outb, j + 1 + n, nb, j + rep.Length, outb.Length - (j + 1 + n));
                    outb = nb;
                    changed.Add(Tuple.Create(kv.Key, cur, kv.Value));
                }
            }
            return outb;
        }

        /// <summary>tail = everything after `frame`: "【KStudio】" + ExtensibleSaveFormat
        /// block ("KKEx", int32 version, int32 len, msgpack Dictionary&lt;string,PluginData&gt;).
        /// Rewrites Timeline XML owner names KK does not know; otherwise returns the
        /// input unchanged (byte-identical).</summary>
        public static byte[] PatchSceneTail(byte[] tail, Action<string> log, List<string> changes)
        {
            var r = new Reader(tail);
            int ver;
            byte[] blob;
            try
            {
                if (r.Str() != TailMark || r.Str() != "KKEx") return tail;
                ver = r.I32();
                int n = r.I32();
                blob = r.Raw(n);
            }
            catch { return tail; }
            bool any = false;
            foreach (var kv in TimelineOwnerRenames)
                if (IndexOf(blob, Encoding.UTF8.GetBytes(kv.Key)) >= 0) { any = true; break; }
            if (!any) return tail;
            try
            {
                int end = 0;
                var d = Msgpack.Decode(blob, ref end);
                if (end != blob.Length) throw new InvalidDataException("trailing bytes in KKEx blob");
                var root = d as MpMap;
                if (root == null) throw new InvalidDataException("KKEx root is not a map");
                // PluginData is serialized by MessagePack-CSharp as [version, data]
                // (index-keyed), not as a string-keyed map.
                var tl = root.Get("timeline");
                MpMap data = null;
                var tlList = tl as List<object>;
                if (tlList != null && tlList.Count >= 2 && tlList[1] is MpMap m1) data = m1;
                else if (tl is MpMap m2) data = m2.Get("data") as MpMap;
                if (data != null)
                {
                    for (int i = 0; i < data.Items.Count; i++)
                    {
                        var val = data.Items[i].Value as string;
                        if (val == null) continue;
                        string cur = val;
                        foreach (var kv in TimelineOwnerRenames)
                        {
                            int c = CountOccurrences(cur, kv.Key);
                            if (c > 0)
                            {
                                cur = cur.Replace(kv.Key, kv.Value);
                                changes.Add(kv.Key + " x" + c);
                            }
                        }
                        if (cur != val) data.Items[i] = new KeyValuePair<object, object>(data.Items[i].Key, cur);
                    }
                }
                if (changes.Count == 0) return tail;
                var newBlob = Msgpack.Encode(d);
                var w = new Writer();
                w.Str(TailMark); w.Str("KKEx"); w.I32(ver); w.I32(newBlob.Length); w.Raw(newBlob);
                w.Raw(r.Raw(r.Remaining));  // anything after the KKEx block (normally nothing)
                return w.ToArray();
            }
            catch (Exception e)
            {
                log("warning: could not rewrite Timeline owners (" + e.Message + "); tail copied verbatim");
                changes.Clear();
                return tail;
            }
        }

        static int CountOccurrences(string s, string sub)
        {
            int c = 0, at = 0;
            while ((at = s.IndexOf(sub, at, StringComparison.Ordinal)) >= 0) { c++; at += sub.Length; }
            return c;
        }

        // ---- convenience -------------------------------------------------

        public static string SummaryLine(string srcName, string dstName, int inLen, int outLen, SceneStats s)
        {
            return srcName + " -> " + dstName + " : " + inLen + " -> " + outLen + " bytes | " + s.KindsText()
                + " | text dropped=" + s.TextsDropped + " | card block versions patched: " + s.BlockVersionsText();
        }
    }
}
