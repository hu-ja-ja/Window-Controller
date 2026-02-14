# Window-Controller

ウィンドウの配置を保存・復元できる Windows 常駐ユーティリティです。
マルチモニター環境やブラウザの複数ウィンドウ運用を効率化します。

## ダウンロード・インストール

1. [Releases](https://github.com/hu-ja-ja/Window-Controller/releases) から最新の `WindowController-vX.X.X-win-x64.zip` をダウンロード
2. 任意のフォルダに展開
3. `WindowController.exe` を実行

> .NET ランタイムは同梱済み（自己完結型）のため、別途インストール不要です。
> 二重起動は自動でブロックされます。

## 動作環境

- Windows 10 / 11 (x64)

## 機能

### プロファイル保存

GUI で起動中のウィンドウを一覧表示し、チェックしたウィンドウの配置を「プロファイル」として保存します。

- 各ウィンドウの位置・サイズ・状態（最小化/最大化/通常）を記録
- スナップ位置（左半分、右半分など）を自動検出 → モニタ構成が変わっても再計算
- ブラウザの URL を UI Automation で取得（クリップボードやフォーカスに一切触れません）

### プロファイル適用（配置のみ）

保存済みプロファイルを選んで「適用」すると、対応ウィンドウを検索して配置を復元します。

### 一括起動 ＋ 配置

見つからないウィンドウはアプリを起動した上で配置します（最大 12 秒待機）。

### 最小化 / 最大化の連動

プロファイル内のウィンドウを「グループ」として扱い、1 つを最小化・復元すると他も追従させます。

- 全体 ON/OFF ＋ プロファイル個別 ON/OFF
- 曖昧なマッチは連動対象から自動除外（誤爆防止）

## 使い方

### タスクトレイ

起動するとタスクトレイに常駐します。

| メニュー | 動作 |
|---|---|
| **GUI を開く** | メイン画面を表示 |
| **設定…** | 設定画面を表示 |
| **プロファイルを適用(配置のみ)** | メイン画面を表示（適用は GUI でプロファイルを選択） |
| **終了** | アプリを終了 |

### ホットキー

| キー | 動作 |
|---|---|
| **Ctrl + Alt + W** | メイン画面を表示（既定。設定で変更/無効化可） |
| （任意） | プロファイル適用ホットキー（プロファイルごとに設定。配置のみ適用） |

### GUI

| 領域 | 説明 |
|---|---|
| 起動中ウィンドウ | チェックしたウィンドウをプロファイルに保存（Title/Exe/Class/URL/Browser Profile を表示） |
| プロファイル保存 | プロファイル名入力 → 「プロファイル保存」 |
| 保存済みプロファイル | チェック＝最小化/最大化を常時連動、名前編集（競合時は自動で連番付与） |
| ボタン | 適用（配置のみ）/ 一括起動＋配置 / 削除 |
| 右クリック | 「モニターを選択して配置」（配置前に警告が出ることがあります） |
| 設定画面 | 連動機能全体 ON/OFF、起動時 GUI 表示、profiles.json 保存先、ホットキー（GUI表示/プロファイル適用） |

## データの保存場所

| ファイル | パス |
|---|---|
| 設定 | `%LOCALAPPDATA%\WindowController\appsettings.json` |
| プロファイル | `%LOCALAPPDATA%\WindowController\profiles.json` （GUI で変更可能） |
| ログ | `%LOCALAPPDATA%\WindowController\window-controller.log` |

> `profiles.json` の保存先は GUI 下部の「変更…」ボタンで任意のフォルダに変更できます。
> 「既定に戻す」で上記デフォルトに戻ります。

### appsettings.json について

`appsettings.json` はアプリ全体の設定（`profiles.json` の保存先・ホットキー設定など）を保持します。

```json
{
  "profilesPath": "C:\\Path\\To\\profiles.json",
  "hotkeys": {
    "showGui": { "key": "W", "ctrl": true, "alt": true, "shift": false, "win": false },
    "profiles": {
      "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx": { "key": "1", "ctrl": true, "alt": false, "shift": false, "win": false }
    }
  }
}
```

### profiles.json スキーマ

```json
{
  "version": 1,
  "settings": {
    "syncMinMax": 0,
    "showGuiOnStartup": 1,
    "aspectRatioWarnThreshold": 0.02,
    "warnOnResolutionMismatch": true,
    "warnOnMonitorMismatch": true,
    "allowCrossDesktopApply": true
  },
  "profiles": [
    {
      "id": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
      "name": "作業用レイアウト",
      "syncMinMax": 0,
      "createdAt": "2026-01-01T20:20:20",
      "updatedAt": "2026-01-01T20:20:20",
      "targetDesktopId": "yyyyyyyy-yyyy-yyyy-yyyy-yyyyyyyyyyyy",
      "windows": [
        {
          "match": {
            "exe": "Code - Insiders.exe",
            "class": "Chrome_WidgetWin_1",
            "title": "Window Title",
            "url": "https://example.com/",
            "urlKey": "https://example.com/",
            "browser": {
              "kind": "chromium",
              "userDataDir": "C:\\Users\\you\\AppData\\Local\\Google\\Chrome\\User Data",
              "profileDirectory": "Default"
            }
          },
          "path": "C:\\Users\\you\\AppData\\Local\\Programs\\...\\Code - Insiders.exe",
          "rect": { "x": 0, "y": 0, "w": 960, "h": 1040 },
          "rectNormalized": { "xN": 0.0, "yN": 0.0, "wN": 0.5, "hN": 1.0 },
          "minMax": 0,
          "snap": { "type": "left" },
          "monitor": {
            "index": 1,
            "name": "\\\\.\\DISPLAY1",
            "devicePath": "\\\\?\\DISPLAY#...",
            "pixelWidth": 3840,
            "pixelHeight": 2160
          },
          "desktopId": "zzzzzzzz-zzzz-zzzz-zzzz-zzzzzzzzzzzz"
        }
      ]
    }
  ]
}
```

> `id` はプロファイルの内部識別子（UUID）です。旧バージョンで作成された `profiles.json` に `id` がない場合や、`id` が重複している場合は起動時に自動で再付与されます。

> JSON は `null` の項目を出力しません（例: `browser` / `snap` / `monitor` / `rectNormalized` / `desktopId` / `targetDesktopId` は状況により省略されます）。また `monitor.pixelWidth` / `monitor.pixelHeight` は 0 の場合は省略されます。

> `desktopId` / `targetDesktopId` / `allowCrossDesktopApply` など仮想デスクトップ関連は将来拡張向けのフィールドで、GUI 上に「※整備中」と表示される機能は現状動作しない場合があります。

## ブラウザ URL 取得

| ブラウザ | 方式 | 備考 |
|---|---|---|
| Chrome / Edge / Brave / Vivaldi | UI Automation (ValuePattern) | 非侵襲 |
| Firefox / Floorp | UI Automation | 取得不可時は空（誤マッチ防止） |

## ビルド（開発者向け）

```bash
# ビルド
cd src
dotnet build

# 実行
dotnet run --project WindowController.App

# 公開用ビルド（単一ファイル・自己完結型）
dotnet publish WindowController.App -c Release
# 出力先: src/WindowController.App/bin/Release/net8.0-windows/win-x64/publish/
```

## 配布用ZIPの生成

配布用のZIP（中身は `WindowController.exe` のみ）をリポジトリ直下の `dist/` に出力します。

```powershell
pwsh scripts/package.ps1
```

- 出力: `dist/WindowController-v<version>-win-x64.zip`
- publish出力（確認用）: `dist/publish/WindowController.exe`

### 必要環境

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 以上
- Windows 10 / 11

### プロジェクト構成

```
src/
  WindowController.sln
  WindowController.Core/       モデル、URL正規化、マッチングロジック
  WindowController.Win32/      P/Invoke、ウィンドウ列挙/配置、WinEventHook
  WindowController.Browser/    FlaUI.UIA3 による URL 取得
  WindowController.App/        WPF アプリ（トレイ、GUI、ViewModel）
```

### 技術スタック

- .NET 8 / WPF
- CommunityToolkit.Mvvm
- FlaUI.UIA3 (UI Automation)
- Serilog (Logging)
- Hardcodet.NotifyIcon.Wpf (System Tray)
- System.Text.Json
- System.Management (WMI)

## ライセンス

[LICENSE](../LICENSE) を参照してください。
