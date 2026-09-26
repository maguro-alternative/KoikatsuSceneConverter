using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace KksSceneConv
{
    /// <summary>UI localization. Keys are the English strings; ui-lang.txt next to
    /// the exe remembers the choice (portable, no registry).</summary>
    static class L
    {
        public static string Lang = "en";
        public static readonly string[] Langs = { "ja", "en" };
        public static readonly string[] LangNames = { "日本語", "English" };

        static string ConfigPath { get { return Path.Combine(AppContext.BaseDirectory, "ui-lang.txt"); } }

        public static void Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string s = File.ReadAllText(ConfigPath).Trim();
                    if (Array.IndexOf(Langs, s) >= 0) { Lang = s; return; }
                }
            }
            catch { }
            Lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? "ja" : "en";
        }

        public static void Save()
        {
            try { File.WriteAllText(ConfigPath, Lang); } catch { }
        }

        public static string T(string key)
        {
            string s;
            if (Lang == "ja" && Ja.TryGetValue(key, out s)) return s;
            return key;
        }

        static readonly Dictionary<string, string> Ja = new Dictionary<string, string>
        {
            ["KKS → KK Scene Converter"] = "KKS → KK シーン変換ツール",
            ["Language"] = "言語",
            ["Input"] = "入力",
            ["Single scene file"] = "シーン1枚",
            ["Whole folder"] = "フォルダごと",
            ["Browse…"] = "参照…",
            ["Drop a KKS Studio scene (.png), several scenes, or a folder here"] = "サンシャイン製のシーン(複数可)、またはフォルダをドロップ",
            ["Output"] = "出力",
            ["Include subfolders"] = "サブフォルダを含む",
            ["Overwrite existing"] = "同名を上書き",
            ["Verify output"] = "出力を検証",
            ["Verbose log"] = "詳細ログ",
            ["Analysis"] = "解析結果",
            ["▶  Convert"] = "▶  変換開始",
            ["Check only"] = "検証のみ",
            ["Cancel"] = "キャンセル",
            ["Open output folder"] = "出力フォルダを開く",
            ["Hide log"] = "ログを隠す",
            ["Show log"] = "ログを表示",
            ["Ready. Drop a scene (or click Browse…), then press ▶ Convert."] = "準備完了。シーンをドロップ（または参照…）して「▶ 変換開始」を押してください。",
            ["Analyzing…"] = "解析中…",
            ["files selected"] = "個のファイルを選択",
            ["No input selected."] = "入力が選択されていません。",
            ["No .png scenes found."] = "変換対象の .png が見つかりません。",
            ["Converting"] = "変換中",
            ["Checking"] = "検証中",
            ["Done"] = "完了",
            ["Cancelled"] = "キャンセルしました",
            ["Cancelling…"] = "キャンセル中…",
            ["Could not list input files"] = "入力ファイルを列挙できませんでした",
            ["converted"] = "変換",
            ["skipped"] = "スキップ",
            ["failed"] = "失敗",
            ["ok"] = "OK",
            ["ng"] = "NG",
            ["exists, skipped (enable Overwrite to replace)"] = "既に存在するためスキップ（上書きする場合は「同名を上書き」をオン）",
            ["already KK format, skipped"] = "既に KK 形式のためスキップ",
            ["not a Studio scene, skipped"] = "Studio のシーンではないためスキップ",
            ["This file is not a Studio scene."] = "このファイルは Studio のシーンではありません。",
            ["Not a Studio scene"] = "Studio のシーンではありません",
            ["FAILED"] = "失敗",
            ["This scene is already in KK format. No conversion needed."] = "このシーンは既に KK 形式です。変換は不要です。",
            ["Scene version"] = "シーンバージョン",
            ["Objects"] = "オブジェクト",
            ["Text objects"] = "Text オブジェクト",
            ["will be removed (KK has no Text object)"] = "削除されます（KK に Text オブジェクトはありません）",
            ["Card block versions"] = "カードのブロックバージョン",
            ["Timeline owners"] = "Timeline owner",
            ["Tail"] = "末尾",
            ["OK, parsed cleanly up to the KStudio marker"] = "OK。KStudio マーカーまで正常に解析",
            ["NG, tail marker not found (layout desync)"] = "NG。末尾マーカーが見つかりません（レイアウト不整合）",
            ["Folder"] = "フォルダ",
            ["scenes to convert"] = "件のシーンを変換予定",
            ["Could not analyze"] = "解析できませんでした",
            ["Select a KKS scene"] = "KKS シーンを選択",
            ["Select a folder of KKS scenes"] = "KKS シーンのフォルダを選択",
            ["Select the output folder"] = "出力フォルダを選択",
            ["Select the output file"] = "出力ファイルを選択",
            ["Output file must be different from the input file."] = "出力ファイルが入力ファイルと同じです。別の名前かフォルダを指定してください。",
            ["Output folder is required."] = "出力フォルダを指定してください。",
            ["Back up your scenes before converting. Text objects are removed and cannot be restored from the converted file."] = "変換前にシーンをバックアップしてください。Text オブジェクトは削除され、変換後のファイルから復元できません。",
            ["Note"] = "注意",
            ["verify NG: output could not be re-parsed"] = "検証 NG: 出力を再解析できませんでした",
        };
    }
}
