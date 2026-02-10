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
| **GUI を開く** | 設定画面を表示 |
| **プロファイルを適用 (配置のみ)** | GUI を表示 |
| **終了** | アプリを終了 |

### ホットキー

| キー | 動作 |
|---|---|
| **Ctrl + Alt + W** | GUI を開く |

### GUI

| 領域 | 説明 |
|---|---|
| 上段 | 起動中ウィンドウ一覧（チェックして保存対象を選択） |
| 中段 | プロファイル名入力 → 「チェックを保存」 |
| 下段 | 保存済みプロファイル一覧（チェック＝連動 ON/OFF） |
| ボタン | 適用（配置のみ）/ 一括起動＋配置 / 削除 |
| 設定 | 連動機能全体 ON/OFF、起動時 GUI 表示、profiles.json 保存先変更 |

## データの保存場所

| ファイル | パス |
|---|---|
| 設定 | `%LOCALAPPDATA%\WindowController\appsettings.json` |
| プロファイル | `%LOCALAPPDATA%\WindowController\profiles.json` （GUI で変更可能） |
| ログ | `%LOCALAPPDATA%\WindowController\window-controller.log` |

> `profiles.json` の保存先は GUI 下部の「変更…」ボタンで任意のフォルダに変更できます。
> 「既定に戻す」で上記デフォルトに戻ります。

### profiles.json スキーマ

```json
{
  "version": 1,
  "settings": {
    "syncMinMax": 0,
    "showGuiOnStartup": 1
  },
  "profiles": [
    {
      "name": "作業用レイアウト",
      "syncMinMax": 0,
      "createdAt": "2026-01-01T20:20:20",
      "updatedAt": "2026-01-01T20:20:20",
      "windows": [
        {
          "match": {
            "exe": "Code - Insiders.exe",
            "class": "Chrome_WidgetWin_1",
            "title": "Window Title",
            "url": "",
            "urlKey": "",
            "browser": null
          },
          "path": "C:\\Users\\you\\AppData\\Local\\Programs\\...\\Code - Insiders.exe",
          "rect": { "x": 0, "y": 0, "w": 960, "h": 1040 },
          "minMax": 0,
          "snap": { "type": "left" },
          "monitor": { "index": 1, "name": "\\\\.\\DISPLAY1" }
        }
      ]
    }
  ]
}
```

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
