# Window-Controller (.NET / WPF版)

AHK版と同等のUX/機能を C#/.NET 8 + WPF で再実装したWindows常駐ユーティリティです。

## 前提

- **Windows** (Windows 10/11)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 以上

## ビルド・実行

```bash
cd src
dotnet build
dotnet run --project WindowController.App
```

Release ビルド:

```bash
cd src
dotnet build --configuration Release
dotnet run --project WindowController.App --configuration Release
```

## 機能

AHK版と同等の操作導線を提供します。

### 1) プロファイル保存

- GUI上段で起動中ウィンドウを一覧表示
- チェックしたウィンドウのスナップショットを保存
  - exe / class / title / path
  - rect（座標とサイズ）
  - minMax（-1=最小化 / 0=通常 / 1=最大化）
  - snap（左右/上下/四分割の自動判定）+ monitor情報
  - ブラウザURL（UI Automationで非侵襲的に取得）

### 2) プロファイル適用（配置のみ）

- マッチングで対象ウィンドウを解決し、rect/minmax/snapで復元
- snap情報がある場合は現在のモニタのワークエリアから再計算

### 3) 一括起動＋配置

- 見つからないウィンドウは `path` や URL から起動を試み、配置
- 起動後リトライ（最大12秒）でウィンドウ出現を待機

### 4) 最小化/最大化（＋前面順）の常時連動

- 全体スイッチ + プロファイル単位のON/OFF
- WinEventHookで最小化/復元/最大化/前面イベントを監視
- 同一プロファイルグループ内で状態を伝播
- 曖昧なマッチは同期対象から除外（誤爆防止）

## 操作導線

### トレイメニュー

起動後、タスクトレイに常駐します。

- **GUIを開く**: 設定画面を表示
- **プロファイルを適用(配置のみ)**: GUI表示
- **終了**: アプリ終了

### ホットキー

- **Ctrl + Alt + W**: GUIを開く

### GUI構成

- 上段: 起動中ウィンドウ一覧（チェックボックス付き）
- プロファイル名入力 + 「チェックを保存」ボタン
- 下段: 保存済みプロファイル一覧（チェック=連動ON/OFF）
- ボタン: 適用（配置のみ） / 一括起動＋配置 / 削除
- チェック: 連動機能を有効にする（全体） / 起動時にGUIを表示する

## 設定ファイル

`config/profiles.json` にAHK版と互換の形式で保存されます。

- アプリ実行ディレクトリの `config/` に自動生成
- JSON破損時はバックアップ退避して初期化

### JSON スキーマ

```json
{
  "version": 1,
  "settings": {
    "syncMinMax": 0,
    "showGuiOnStartup": 1
  },
  "profiles": [
    {
      "name": "プロファイル名",
      "syncMinMax": 0,
      "createdAt": "2026-01-01T20:20:20",
      "updatedAt": "2026-01-01T20:20:20",
      "windows": [
        {
          "match": {
            "exe": "app.exe",
            "class": "ClassName",
            "title": "Window Title",
            "url": "",
            "urlKey": "",
            "browser": null
          },
          "path": "C:\\path\\to\\app.exe",
          "rect": { "x": 0, "y": 0, "w": 1000, "h": 700 },
          "minMax": 0,
          "snap": { "type": "left" },
          "monitor": { "index": 1, "name": "\\\\.\\DISPLAY1" }
        }
      ]
    }
  ]
}
```

## ログ

- `config/window-controller.log` : 動作ログ（Serilog）

## ブラウザURL取得

- **Chromium系** (Chrome/Edge/Brave/Vivaldi): UI AutomationのValuePatternで非侵襲的に取得
- **Firefox/Floorp**: UI Automationで取得を試みる（クリップボード不使用）
  - 取得できない場合は空のまま（誤マッチ防止のためURLなしとして扱う）

## マッチング仕様

- 必須条件: exe + class（classはワイルドカード対応）
- スコアリング:
  - process path一致: +60
  - browser profile一致: +50～70
  - urlKey完全一致: +60
  - host一致: +20
  - title完全/部分一致: +10～30
- 同期時は曖昧一致（スコア差≤10 かつ スコア<50）を除外

## プロジェクト構成

```text
src/
  WindowController.sln
  WindowController.Core/       # モデル、URL正規化、マッチングロジック
  WindowController.Win32/       # P/Invoke、ウィンドウ列挙/配置、モニタ、WinEventHook
  WindowController.Browser/     # FlaUI.UIA3によるURL取得
  WindowController.App/         # WPF アプリ（トレイ、GUI、ViewModel）
```

## 技術スタック

- .NET 8, WPF
- CommunityToolkit.Mvvm (MVVM)
- FlaUI.UIA3 (UI Automation)
- Serilog (Logging)
- Hardcodet.NotifyIcon.Wpf (System Tray)
- System.Text.Json (JSON)
- System.Management (WMI CommandLine取得)
