using System;
using System.Collections.Generic;
using KksSceneConv;

namespace KksSceneConv.Tests
{
    /// <summary>Assembles minimal, synthetic Studio scenes in either the KKS (1.1.2.1)
    /// or the KK (1.0.4.2) layout, so the transcoder can be tested without real game
    /// data. The layout mirrors the reader in Scene.cs, so this catches regressions in
    /// the conversion, not misunderstandings of the format itself (real scenes cover that).</summary>
    public static class SceneBuilder
    {
        public const string KksVersion = "1.1.2.1";
        public const string KkVersion = Transcoder.KkSceneVersion;

        static readonly byte[] PngSignature = { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };
        static readonly byte[] IendChunk = { 0, 0, 0, 0, (byte)'I', (byte)'E', (byte)'N', (byte)'D', 0xAE, 0x42, 0x60, 0x82 };

        /// <summary>One object: its kind code plus a body writer. The body receives
        /// the target layout (true = KKS) because a few fields exist only in KKS.</summary>
        public sealed class Obj
        {
            public int Kind;
            public Action<Writer, bool> Body;
            /// <summary>dicKey for root objects; 0 = position in the root list (1-based).</summary>
            public int Key;

            public Obj WithKey(int key) { Key = key; return this; }
        }

        public sealed class Options
        {
            public bool Kks = true;
            public List<Obj> Roots = new List<Obj>();
            public string Background = "";
            /// <summary>msgpack blob of the ExtensibleSaveFormat block; null = no KKEx block.</summary>
            public byte[] KkEx;
        }

        public static byte[] Build(Options o)
        {
            var w = new Writer();
            w.Raw(PngSignature);
            w.Raw(IendChunk);
            w.Str(o.Kks ? KksVersion : KkVersion);
            w.I32(o.Roots.Count);
            for (int i = 0; i < o.Roots.Count; i++)
            {
                w.I32(o.Roots[i].Key != 0 ? o.Roots[i].Key : i + 1);  // dicKey
                w.I32(o.Roots[i].Kind);
                o.Roots[i].Body(w, o.Kks);
            }
            SceneSettings(w, o.Kks, o.Background);
            w.Str(Transcoder.TailMark);
            if (o.KkEx != null)
            {
                w.Str("KKEx");
                w.I32(3);
                w.I32(o.KkEx.Length);
                w.Raw(o.KkEx);
            }
            return w.ToArray();
        }

        // ---- objects -----------------------------------------------------

        public static Obj Item(params Obj[] children)
        {
            return new Obj
            {
                Kind = 1,
                Body = (w, kks) =>
                {
                    ObjectBase(w);
                    w.I32(0); w.I32(1); w.I32(2);  // group, category, no
                    if (kks) w.I32(5);             // animePattern (KKS only)
                    w.F32(1f);                     // animeSpeed
                    for (int i = 0; i < 8; i++) w.Str("{}");
                    for (int i = 0; i < 3; i++) PatternInfo(w);
                    w.F32(1f);                     // alpha
                    w.Str("{}"); w.F32(0f);        // lineColor, lineWidth
                    w.Str("{}"); w.F32(0f); w.F32(0f);
                    PatternInfo(w);                // panel
                    w.Bool(false);                 // enableFK
                    w.I32(1);                      // one bone
                    w.Str("bone");
                    ObjectBase(w, false);
                    w.Bool(false);                 // enableDynamicBone
                    w.F32(0f);                     // animeNormalizedTime
                    Children(w, kks, children);
                },
            };
        }

        public static Obj Folder(string name, params Obj[] children)
        {
            return new Obj
            {
                Kind = 3,
                Body = (w, kks) => { ObjectBase(w); w.Str(name); Children(w, kks, children); },
            };
        }

        public static Obj Light()
        {
            return new Obj
            {
                Kind = 2,
                Body = (w, kks) =>
                {
                    ObjectBase(w);
                    w.I32(0);
                    for (int i = 0; i < 7; i++) w.F32(1f);
                    w.Bool(true); w.Bool(false); w.Bool(true);
                },
            };
        }

        public static Obj Camera()
        {
            return new Obj { Kind = 5, Body = (w, kks) => { ObjectBase(w); w.Str("cam"); w.Bool(true); } };
        }

        /// <summary>OITextInfo (kind 7); KKS only, the converter must drop it.</summary>
        public static Obj Text()
        {
            return new Obj
            {
                Kind = 7,
                Body = (w, kks) =>
                {
                    ObjectBase(w);
                    w.I32(0); w.Str("hello"); w.Str("font"); w.F32(1f);
                    w.I32(3); w.Raw(new byte[] { 1, 2, 3 });
                },
            };
        }

        /// <summary>Character with an embedded chara card.</summary>
        public static Obj Character(string mark, string parameterVersion)
        {
            return new Obj
            {
                Kind = 0,
                Body = (w, kks) =>
                {
                    ObjectBase(w);
                    w.I32(1);                          // sex
                    // chara card
                    w.I32(100);                        // product
                    w.Str(mark);
                    w.Str("0.0.0");                    // ChaFileVersion
                    w.I32(4); w.Raw(new byte[] { 9, 9, 9, 9 });  // face png
                    var header = BlockHeader(parameterVersion);
                    w.I32(header.Length); w.Raw(header);
                    var blocks = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00 };
                    w.I64(blocks.Length); w.Raw(blocks);
                    // character body
                    w.I32(1); w.I32(0); ObjectBase(w, false);  // bones
                    w.I32(0);                                   // ikTarget
                    w.I32(1); w.I32(0); Children(w, kks, new Obj[0]);  // accessory point
                    w.I32(0);                          // kinematicMode
                    w.I32(0); w.I32(0); w.I32(0);      // animeInfo
                    w.I32(0); w.I32(0);                // handPtn
                    w.F32(0f);                         // nipple
                    w.Raw(new byte[5]);                // siru
                    w.F32(0f); w.Bool(false);          // mouthOpen, lipSync
                    ObjectBase(w, false);              // lookAtTarget
                    w.Bool(false);                     // enableIK
                    for (int i = 0; i < 5; i++) w.Bool(true);
                    w.Bool(false);                     // enableFK
                    for (int i = 0; i < 7; i++) w.Bool(true);
                    for (int i = 0; i < 8; i++) w.Bool(false);  // expression
                    w.F32(1f); w.F32(0f);
                    w.Bool(true); w.Bool(false);
                    w.I32(1); w.I32(0); w.I32(1); w.I32(2);  // voiceCtrl list
                    w.I32(0);                          // voice repeat
                    w.Bool(true); w.F32(1f); w.Bool(false);
                    w.Str("{}");                       // simpleColor
                    w.F32(0f); w.F32(0f);
                    w.I32(2); w.Raw(new byte[] { 1, 2 });  // neckByteData
                    w.I32(0);                              // eyesByteData
                    w.F32(0f);
                    w.I32(1); w.I32(0); w.I32(1);      // dicAccessGroup
                    w.I32(0);                          // dicAccessNo
                },
            };
        }

        /// <summary>ChaFile BlockHeader as MessagePack-CSharp writes it.</summary>
        public static byte[] BlockHeader(string parameterVersion)
        {
            var list = new List<object>
            {
                BlockInfo("Custom", "0.0.0"),
                BlockInfo("Parameter", parameterVersion),
                BlockInfo("KKEx", "3"),
            };
            var root = new MpMap();
            root.Items.Add(new KeyValuePair<object, object>("lstInfo", list));
            return Msgpack.Encode(root);
        }

        static MpMap BlockInfo(string name, string version)
        {
            var m = new MpMap();
            m.Items.Add(new KeyValuePair<object, object>("name", name));
            m.Items.Add(new KeyValuePair<object, object>("version", version));
            m.Items.Add(new KeyValuePair<object, object>("pos", 0));
            m.Items.Add(new KeyValuePair<object, object>("size", 0));
            return m;
        }

        /// <summary>KKEx plugin dictionary with a Timeline entry holding <paramref name="xml"/>,
        /// serialized like ExtensibleSaveFormat's PluginData ([version, data]).</summary>
        public static byte[] TimelineKkEx(string xml)
        {
            var data = new MpMap();
            data.Items.Add(new KeyValuePair<object, object>("sceneInfo", xml));
            var root = new MpMap();
            root.Items.Add(new KeyValuePair<object, object>("timeline", new List<object> { 0, data }));
            return Msgpack.Encode(root);
        }

        // ---- building blocks ---------------------------------------------

        static void ObjectBase(Writer w, bool other = true)
        {
            w.I32(0);                                  // dicKey
            for (int i = 0; i < 9; i++) w.F32(i);      // changeAmount pos/rot/scale
            if (other) { w.I32(0); w.Bool(true); }     // treeState, visible
        }

        static void PatternInfo(Writer w)
        {
            w.I32(0); w.Str(""); w.Bool(false); w.Str("{}"); w.F32(1f);
        }

        static void Children(Writer w, bool kks, Obj[] children)
        {
            w.I32(children.Length);
            foreach (var c in children) { w.I32(c.Kind); c.Body(w, kks); }
        }

        static void SceneSettings(Writer w, bool kks, string background)
        {
            w.I32(-1);                                 // map
            for (int i = 0; i < 9; i++) w.F32(0f);     // caMap
            w.I32(0); w.Bool(true); w.I32(0);          // sunLightType, mapOption, aceNo
            w.F32(0f);                                 // aceBlend
            w.Bool(false); w.Str("{}"); w.F32(0f);     // AOE
            w.Bool(false); w.F32(0f); w.F32(0f);       // bloom
            w.F32(0f);                                 // bloomThreshold
            w.Bool(false); w.F32(0f); w.F32(0f);       // depth
            w.Bool(false);                             // vignette
            w.Bool(false);                             // fog
            w.Str("{}"); w.F32(0f); w.F32(0f);
            w.Bool(false);                             // sunShafts
            w.Str("{}"); w.Str("{}");
            w.I32(0);                                  // sunCaster
            w.Bool(true);                              // enableShadow
            w.Bool(false); w.Bool(false); w.F32(0f); w.Str("{}");
            w.F32(0f); w.I32(0); w.F32(0f);            // lineWidthG, rampG, ambientShadowG
            if (kks)
            {
                w.I32(1);                              // shaderType
                w.I32(3); w.Raw(new byte[] { 0x91, 0x01, 0xC0 });  // SkyInfo msgpack
            }
            for (int i = 0; i < 11; i++)               // cameraSaveData + 10 slots
            {
                w.I32(2);
                for (int j = 0; j < 9; j++) w.F32(0f);
                w.F32(23f);
            }
            w.Str("{}"); w.F32(1f); w.F32(0f); w.F32(0f); w.Bool(true);           // charaLight
            w.Str("{}"); w.F32(1f); w.F32(0f); w.F32(0f); w.Bool(true); w.I32(0); // mapLight
            w.I32(0); w.I32(0); w.Bool(false);         // bgmCtrl
            w.I32(0); w.I32(0); w.Bool(false);         // envCtrl
            w.I32(0); w.Str(""); w.Bool(false);        // outsideSoundCtrl
            w.Str(background);
            w.Str("");                                 // frame
        }
    }
}
