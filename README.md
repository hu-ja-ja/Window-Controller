# Window-Controller

AutoHotkey v2 で動く「ウィンドウ配置プロファイル」ツールです。
起動中のウィンドウをチェックして **配置（位置・サイズ・最小化/最大化状態）** を保存し、ワンクリックで再適用できます。

加えて、プロファイル単位で **最小化/最大化（＋前面順）を常時連動** させることもできます（任意）。

## 動作環境

- Windows
- [AutoHotkey v2](https://www.autohotkey.com/)（`#Requires AutoHotkey v2.0`）

## 使い方（クイックスタート）

1. AutoHotkey v2 をインストール
2. `main.ahk` を実行
3. GUI を開く
   - トレイ: **GUIを開く**
   - ホットキー: `Ctrl + Alt + W`
4. 上段「起動中ウィンドウ」一覧で、保存したいウィンドウにチェック
5. 右側の「プロファイル名」を入力して **チェックを保存**
6. 下段「保存済みプロファイル」から選んで **適用（配置のみ）**

### 一括起動＋配置

下段の **一括起動＋配置** は、プロファイルに含まれるウィンドウが見つからない場合に、保存時の情報（実行ファイル/パス、URL など）を元に起動を試みた上で配置します。

## 機能

### 1) プロファイル保存

GUI 上段でチェックしたウィンドウのスナップショットを保存します。

保存される主な情報:
- 対象の識別情報（`exe` / `class` / `title` / （ブラウザなら URL など））
- 実行ファイルパス（取得できた場合）
- ウィンドウ矩形（x/y/w/h）
- 最小化/最大化状態（`minMax`: `-1/0/1`）
- スナップ配置（左右/上下/四分割）判定できた場合は `snap` とモニタ情報

### 2) プロファイル適用（配置のみ）

保存した矩形・状態へ復元します。
- `minMax = -1`: 最小化
- `minMax = 0`: 通常
- `minMax = 1`: 最大化

スナップ情報（`snap`）がある場合は、保存したモニタの **ワークエリア** から再計算して配置します。

### 3) 最小化/最大化（＋前面順）の常時連動（任意）

- 全体スイッチ: GUI の **連動機能を有効にする（全体）**
- プロファイル単位: 下段リストの **チェック**

有効時、同じプロファイルに属するウィンドウの
- 最小化 / 復元 / 最大化
- 前面に来たときの Z オーダー（フォーカスは奪わないように調整）

を同一グループ内へ伝播します。

## 設定ファイル

設定は `config/profiles.json` に保存されます。

- アプリ初回起動時に `config/` が無い場合は自動生成します
- JSON が壊れている場合は、バックアップへ退避して初期化します（`.broken.YYYYMMDD_HHMMSS`）

### settings

- `settings.syncMinMax`（0/1）: 連動機能（全体）の ON/OFF
- `settings.showGuiOnStartup`（0/1）: 起動時に GUI を開くか（OFF ならトレイ常駐）

### profiles（例）

`config/profiles.json` は概ね次の形です（例は概略です）。

```json
{
  "version": 1,
  "settings": {
    "syncMinMax": 1,
    "showGuiOnStartup": 0
  },
  "profiles": [
    {
      "name": "左画面",
      "syncMinMax": 1,
      "createdAt": "2026-01-01T20:20:20",
      "updatedAt": "2026-01-01T20:20:20",
      "windows": [
        {
          "match": {
            "exe": "Discord.exe",
            "class": "Chrome_WidgetWin_1",
            "title": "#chat | ...",
            "url": "",
            "urlKey": "",
            "browser": {
              "kind": "chromium",
              "userDataDir": "C:\\...",
              "profileDirectory": "Default"
            }
          },
          "path": "C:\\Users\\...\\Discord.exe",
          "rect": { "x": 0, "y": 0, "w": 1000, "h": 700 },
          "minMax": 0,
          "snap": { "type": "left" },
          "monitor": { "index": 1, "name": "..." }
        }
      ]
    }
  ]
}
```

## トレイメニュー

`main.ahk` 実行後、タスクトレイに常駐します。

- **GUIを開く**: 設定画面を表示
- **プロファイルを適用(配置のみ)**: GUI を開き、適用操作に入りやすい状態にします
- **終了**: 終了

## ログ / エラー

`config/` に出力されます。

- `config/window-controller.log` : 動作ログ（連動の伝播など）
- `config/__last_error.txt` : 未処理例外（保険のエラーログ）
- `config/__exit_log.txt` : 終了ログ

## 既知の注意点

- 管理者権限で動くアプリや保護されたウィンドウは、通常権限のスクリプトから操作できない場合があります。
  - その場合はスクリプトを管理者として実行する必要があるかもしれません。
- Firefox 系（`firefox.exe` / `floorp.exe`）の URL 取得は、保存時のみクリップボード経由のベストエフォートです（フォーカス移動が発生します）。
  - Chromium 系（Chrome/Edge/Brave/Vivaldi）は、可能な範囲で非侵襲的に取得します。
- ウィンドウの再生成（HWND 変化）が頻繁に起きるアプリでは、連動対象の解決が遅れることがあります。

## 開発メモ（構成）

- `main.ahk`: エントリポイント（トレイ/ホットキー/OnExit/OnError）
- `gui.ahk`: GUI（ウィンドウ一覧・プロファイル操作）
- `window_control.ahk`: 本体ロジック（保存/適用/WinEventフックによる連動）
- `json.ahk`: JSON Parse/Stringify（最小実装）

---
