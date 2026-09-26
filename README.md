<div align="center">

  # KKS → KK Scene Converter

  **Down-convert Koikatsu Sunshine (KKS) Studio scenes so that Koikatsu (KK) CharaStudio can load them.**
  コイカツサンシャイン（KKS）の Studio シーンを、無印コイカツ（KK）の CharaStudio で読み込める形式に変換するツール。

  ![platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
  ![.NET](https://img.shields.io/badge/.NET-8-512BD4)
  ![license](https://img.shields.io/badge/license-MIT-green)

  [日本語](#日本語) · [English](#english)
</div>

> [!CAUTION]
> **変換前にシーンを必ずバックアップしてください。** 変換後のシーンをサンシャインで読み込むと元のシーンデータから一部のデータが抜け落ちる場合があります。
>
> **Always back up your scenes before converting.** Loading a converted scene back into Koikatsu Sunshine may lose some of the data from the original scene.

---

## 日本語

### 概要

コイカツサンシャインのキャラスタジオで作成されたシーンデータを無印のキャラスタジオで読めるように変換します。
GUIアプリとして使えますが、CLI上で扱うこともできます。

### ダウンロード

[Releases](../../releases) から：

| | 説明 |
|---|---|
| `KksSceneConv.exe`（約 66 MB） | **推奨**。解凍してそのままご利用可能です。 |
| `KksSceneConv-lite.exe`（約 1 MB） | ご利用いただく場合、[.NET 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) 以上が必要です。 |

> 起動時や出力先の選択時に `ui-lang.txt` と `last-output-dir.txt` が exe の隣に作られます（ポータブル。レジストリには書きません）。

### 使い方（GUI）

1. シーン（`.png`）、複数のシーン、またはフォルダを**ウィンドウにドロップ**（または「参照…」）。exe のアイコンにドロップしても開けます
2. ドロップした時点で解析結果が表示されます（バージョン、オブジェクト数、削除される Text の数、書き換えられるカードブロック、Timeline の変更）
3. 出力先を確認（既定は元ファイルと同じフォルダ、ファイル名は `元の名前_kk.png`）。「参照…」で一度選んだ出力先は次回以降も記憶され、入力を選び直しても変わりません
4. **▶ 変換開始**

「検証のみ」は変換せずに構造を解析し、末尾マーカーまで正しく読めるかを確認します。KK のシーンにも使えるので、変換結果の確認にも使えます。「出力を検証」をオンにしておくと、変換後に出力を自動で再解析します。

### 使い方（CLI）

```bat
KksSceneConv.exe convert in.png [out.png] [-v]     :: 変換（既定の出力名は in_kk.png）
KksSceneConv.exe check   scene.png                 :: 解析のみ（KK / KKS どちらも可）
KksSceneConv.exe batch   D:\scenes D:\out [-r]     :: フォルダ内の *.png を一括変換（-r: サブフォルダも。シーン以外の画像やカードはスキップ）
KksSceneConv.exe help
```

終了コード: 0 = 成功、1 = 失敗 / NG、2 = 引数エラー。

> GUI アプリとしてビルドされているため、`cmd.exe` からは出力が終わる前にプロンプトが戻ることがあります。バッチファイルや PowerShell から終了コードを取りたい場合は `start /wait` や `Start-Process -Wait` を使ってください（`tools/selftest.ps1` が例です）。

### ビルド / 自己テスト

```powershell
pwsh -File tools/build.ps1                                 # dist\self-contained と dist\lite を生成
pwsh -File tools/selftest.ps1 -Scene "KKS のシーン.png"   # 変換 → 再解析 → Python 版と一致するか比較
dotnet test KksSceneConv.sln                               # 単体テスト（合成シーンを使うためシーン不要）
```

.NET 8 SDK 以降が必要です。**シーンはご自身で用意してください**。リポジトリにゲーム素材は含まれません（例外はテスト用の実シーン2件 `tests/fixtures/real/*.png` のみ）。

`dotnet test` は `tests/fixtures/real/` の実シーンでも「変換 → 再解析」を検証します。環境変数 `KKS_SCENES_DIR` でフォルダを指定すると、手元のシーンでも同じ検証ができます。CI では `KKS_REQUIRE_REAL_SCENES=1` を設定し、実シーンが見つからない場合はスキップではなく失敗にしています。

[exapmle/kks2kk.py](exapmle/kks2kk.py) は移植元の Python 版リファレンス実装です。C# 版は同じ入力に対して同じバイト列を出力します。

### 変換で行うこと

| 項目 | 内容 |
|---|---|
| シーンバージョン | `1.1.x` → `1.0.4.2` に書き換え |
| キャラカード | マーク `【KoiKatuCharaSun】` → `【KoiKatuChara】`。KK が読み飛ばす新しいブロックバージョン（Parameter 0.0.6 など）を KK が受け付ける番号に下げる |
| KKS 専用フィールド | アイテムの `animePattern`、シーンの `shaderType` / `SkyInfo` を除去 |
| Text オブジェクト | KK に存在しないため削除（子要素の個数も再計算） |
| 背景 | `UserData/bg/x.png` のようなパスをファイル名のみに |
| Timeline | KKSPE 由来の `owner="KKSPE"` を `KKPE` に変更 |

それ以外（カメラ・ライト・BGM・環境・ExtensibleSaveFormat の末尾）は両ゲームで同一のため、バイト単位でそのままコピーします。

### 免責

非公式のサードパーティ製ツールであり、ゲームの開発・販売元とは無関係です。使用は自己責任で。

---

## English

### What it is

Converts KKS Studio scenes (`.png`) into the layout KK CharaStudio expects. GUI and CLI in a single exe.

**What the conversion does**

- Scene version `1.1.x` → `1.0.4.2`
- Embedded chara cards: mark `【KoiKatuCharaSun】` → `【KoiKatuChara】`; BlockHeader versions newer than KK knows (e.g. Parameter 0.0.6) are lowered so KK does not skip the block (name / personality would vanish otherwise)
- KKS-only fields removed: item `animePattern`, scene `shaderType` / `SkyInfo`
- Text objects (kind 7) removed; child counts recomputed
- Background path (`UserData/bg/x.png`) reduced to a file name
- Timeline `owner="KKSPE"` renamed to `KKPE`

Everything else is byte-identical between the two games and copied verbatim.

### Download

Grab a build from [Releases](../../releases): the **self-contained** exe (≈66 MB, nothing to install) or the **lite** exe (≈1 MB, needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)).

### Usage

GUI: drop a scene, several scenes or a folder into the window (or onto the exe icon), read the analysis, confirm the output folder (default: the source's own folder, files named `<name>_kk.png`; a folder picked with **Browse…** is remembered for later sessions and is not reset when the input changes) and press **▶ Convert**. **Check only** parses without writing and works on KK scenes too.

CLI:

```bat
KksSceneConv.exe convert in.png [out.png] [-v]
KksSceneConv.exe check   scene.png
KksSceneConv.exe batch   D:\scenes D:\out [-r]   :: non-scene .png files (pictures, cards) are skipped
KksSceneConv.exe help
```

Exit codes: 0 ok, 1 failed / NG, 2 usage. The exe is a GUI-subsystem binary, so `cmd.exe` may return the prompt before the output finishes; use `start /wait` or `Start-Process -Wait` when you need the exit code from a script (see `tools/selftest.ps1`).

### Build

```powershell
pwsh -File tools/build.ps1
pwsh -File tools/selftest.ps1 -Scene "your KKS scene.png"
dotnet test KksSceneConv.sln   # unit tests (synthetic scenes, no game data needed)
```

Requires the .NET 8 SDK or newer. **Bring your own scenes**; the repository contains no game assets apart from two test scenes in `tests/fixtures/real/`. [exapmle/kks2kk.py](exapmle/kks2kk.py) is the Python reference the C# port was verified against (byte-identical output).

`dotnet test` also round-trips the real scenes in `tests/fixtures/real/`; point `KKS_SCENES_DIR` at a folder to run the same checks on your own scenes. CI sets `KKS_REQUIRE_REAL_SCENES=1`, so missing fixtures fail the build instead of being skipped.

### Disclaimer

Unofficial third-party tool, not affiliated with the game's publisher. Use at your own risk.

### License

[MIT](LICENSE)
