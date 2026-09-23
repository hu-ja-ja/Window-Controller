# Window-Controller 作業プラン (2026-09-23)

> 一時的な作業メモ。実行が終わったら削除するか、残すなら `docs/` へ移動する。
>
> **決定事項**: 仮想デスクトップ(VD)機能は完成させず `feature/virtual-desktop` ブランチへ退避。
> 優先順位は ①VD 退避 + 掃除 → ②.NET 10 移行 → ③(延期)パッケージ メジャー更新。
>
> **進捗**: ✅ Step 1(VD 退避)・Step 2(置き去りコード削除)・Step 3(.NET 10 移行) 完了。
> `feature/virtual-desktop` ブランチは Step 1 着手前の dev(660cf32)から作成済み。
> 残りは Step 4(延期可)。

## 前提

- 作業ブランチは `dev`(660cf32、origin/dev と同期済み、master より2コミット先行)
- VD コード(`VirtualDesktopService` / `VirtualDesktopMoveHelper` / `DesktopPickerWindow`)は master にもマージ済み
- `.NET 8` は **2026-11-10 サポート終了**(このプランで唯一の期限)

---

## Step 1: VD をブランチへ退避(約1時間・設計判断ゼロ) ✅完了

### 1-1. ブランチ作成

```bash
git branch feature/virtual-desktop        # 現 HEAD(dev) から。VD 一式がそのまま残る
git push -u origin feature/virtual-desktop
# 任意: feature/virtual-desktop → dev の draft PR を1本立てる(忘れ防止 + CI)
```

以後 `dev` から VD を削除し、通常どおり dev → master へマージ。
再開時は `feature/virtual-desktop` を dev に rebase(自己完結ファイル中心なので衝突は軽い)。

### 1-2. dev から削除するもの

- **ファイル削除**
  - `src/WindowController.Win32/VirtualDesktopService.cs`
  - `src/WindowController.Win32/VirtualDesktopMoveHelper.cs`
  - `src/WindowController.App/DesktopPickerWindow.xaml` / `.xaml.cs`
- **`App.xaml.cs`**: `_vdService` 4箇所(フィールド / 生成 / `ExitApp` と `OnExit` の Dispose)、`MainViewModel` の ctor 引数
- **`MainViewModel.cs`**
  - `_vdService`(フィールド / ctor 引数 / 代入)
  - `CaptureWindowEntry` の `dId` 取得ブロックと `DesktopId = desktopId`
  - `SetTargetDesktop` / `ClearTargetDesktop` / `FormatDesktopLabel` / `ApplyToDesktopAndMonitor`(no-op)
  - `ProfileItem.TargetDesktopLabel` と `ReloadProfiles` の代入
- **モデル**: `Profile.TargetDesktopId` / `WindowEntry.DesktopId`
- **`MainWindow.xaml`**: Desktop 列、「仮想デスクトップを選択して配置 ※整備中」メニュー項目
- **`NativeMethods.cs`**: `DwmGetWindowAttribute` + `DWMWA_CLOAKED`(`IsWindowCloaked` 専用)
- **appHwnd 配管**(VD 専用のため一緒に削除): `ApplyByIdAsync` の `appHwnd` 引数、not-implemented デバッグログ、呼び出し元4箇所の `WindowInteropHelper` ブロック、`GetMainWindowHandle`
- **README.md**: スキーマの `targetDesktopId` / `desktopId`、null 省略例、将来拡張の注記、右クリック表

### 1-3. 残すもの

- `Settings.AllowCrossDesktopApply`(README 公開済みスキーマ。ブランチとの衝突回避のため dev に残す)
- `MonitorInfo.DevicePath`(既存の任意項目。触らない)

### 1-4. 検証

- `dotnet build` / `dotnet test` 緑
- 起動スモーク: トレイ、GUI、ホットキー、通常適用 / モニター選択適用 / 一括起動
- 「※整備中」の文言が UI から消えていること

---

## Step 2: 置き去りコード削除(約 −80 行・挙動不変) ✅完了

Step 1 とは別コミットにする(レビューしやすさ優先)。

- `HotkeyManager.cs`: `RegisterGuiHotkey`、`UnregisterProfileHotkey`
- `ProfileStore.cs`: `DeleteProfile(string)`(`DeleteProfileById` が本線)
- `ProfileApplier.cs`: `ApplyByNameAsync` + `ProfileApplierTests.cs` の該当テスト
- 未使用プロパティ: `MonitorData.AspectRatio`、`MonitorInfo.AspectRatio`、`MonitorPickerItem.Number`
- 書き込み専用 Rect/MinMax: `WindowInfo.Rect` / `MinMax`、`WindowItem.Rect` / `MinMax`
  (`GetWindowRect` / `GetMinMax` 自体は他で使用中のため残す)
- `NativeMethods.cs` 未使用10件: `GetWindowPlacement` / `WINDOWPLACEMENT` / `POINT` / `GetForegroundWindow` / `SetForegroundWindow` / `MonitorFromWindow` / `MonitorFromRect` / `SW_SHOWNORMAL` / `VK_W` / `MONITOR_DEFAULTTONEAREST`
- `SyncManager.cs`: `UpdateHooksIfNeeded(bool skipRebuild = false)` の引数削除(true 呼び出し実績なし)

検証: build / test / 起動スモーク。

---

## Step 3: .NET 10 移行(期限 2026-11-10) ✅完了

### 3-1. 変更内容

- TFM: `net8.0` → `net10.0`、`net8.0-windows` → `net10.0-windows`(全5 csproj)
- CI `.github/workflows/ci.yml`: `dotnet-version: '10.0.x'`
- `System.Management` 8.0.0 → 10.0.12(プラットフォーム整合)
- GitHub Actions 更新(2026-09 時点の最新):
  - `actions/checkout` v4 → **v7**
  - `actions/setup-dotnet` v4 → **v6**
  - `actions/upload-artifact` v4 → **v7**
  - `softprops/action-gh-release` v2 → **v3**

### 3-2. 検証

- build / test 緑
- **単一ファイル publish の出力が起動すること**(`PublishTrimmed=false` + `IncludeNativeLibrariesForSelfExtract` の組み合わせが .NET 10 でも動くか)
- `pwsh scripts/package.ps1` で zip 生成確認

補足: NuGet は上位 TFM から下位 TFM のパッケージを消費できるため、**TFM バンプだけなら既存パッケージのままで通る**。詰まったらブランチを退避して Step 1-2 を先に進めてよい(技術的依存はない)。

### 3-3. 実施結果(2026-09-23)

- build / test 緑(Core 140 + App 26、net10.0 で実行)
- 単一ファイル publish 成功(`WindowController.exe` 172.7MB、net8 時代は 162.2MB)
- 起動スモーク: 本番インスタンスが Mutex を保持していたため二重起動ブロックに到達 = CLR 起動・WPF 初期化・マネージコード実行まで確認。本番プロセスには触れていない
- 残警告: `NU1904 System.Drawing.Common 5.0.2`(FlaUI 4.0.0 経由の推移的依存、Critical) → Step 4 の FlaUI 5.0.0 更新で解消見込み

---

## Step 4: 延期項目(期限なし)

- パッケージ更新(低リスク組を一括): CommunityToolkit.Mvvm 8.4.2 / Serilog 4.4.0 / WPF-UI 4.3.0
- メジャー更新は1つずつ、それぞれスモーク付き: FlaUI.UIA3 5.0.0 → Hardcodet.NotifyIcon.Wpf 2.0.1
- 任意の掃除(そのファイルを触るついで): JSON 定型の共通化、`SettingsViewModel` のパス変更2メソッド重複、`App.ExitApp` / `OnExit` の二重 Dispose、`package.ps1` の XML パース簡略化

---

## やらないと決定済み

- FlaUI.UIA3 維持(`System.Windows.Automation` への置換は UIA2 差で回帰リスクの方が大きい)
- バージョン情報 / About 一式維持(直前コミットで意図的に追加)
- モニター警告3設定維持(README スキーマで公開済み)
- WPF-UI / Hardcodet / System.Management / SyncManager のロック群維持(代替なし・機能上必要)

---

## 付録: VD 再開時の実装メモ

`feature/virtual-desktop` を dev に rebase してから着手。

### 適用ロジック(`ProfileApplier.cs`)

- ctor に移動デリゲート `Func<nint, Guid, bool> moveToDesktop` を追加
  (既存の `candidatesProvider` と同じテストシーム方式。`VirtualDesktopService` 直渡しより COM を触らず分岐をテストできる)
- `appHwnd` 引数と not-implemented ログは Step 1 で削除済みの前提。新シグネチャ:
  `ApplyByIdAsync(string profileId, bool launchMissing, MonitorData? targetMonitor = null, Guid? targetDesktopId = null)`
- ゲート(`_candidatesProvider()` 呼び出し前 = 列挙前に置く):
  `targetDesktopId != null && !_store.Data.Settings.AllowCrossDesktopApply`
  → `new ApplyResult(0, profile.Windows.Count, ["設定で仮想デスクトップ間の配置が無効になっています"])` を即返し
- 移動 → 配置の順: hwnd 確定(起動待ち含む)後、`moveToDesktop` が false なら failure に追加して `continue`。成功時のみ `_arranger.Arrange` へ

### 呼び出し元

- `App.xaml.cs`: `ProfileApplier` 生成に `_vdService.MoveWindowToDesktop` を渡す
- `MainViewModel.ApplyToDesktopAndMonitor` を実装:
  1. `GetAllDesktops()` が空ならステータス表示して終了
  2. `GetCurrentDesktopId(メインウィンドウのハンドル)`(`(現在)` バッジ用。0 なら null で続行)
  3. `DesktopPickerWindow(desktops, currentId, "選択したデスクトップにウィンドウを移動して配置します")` を Owner 付き `ShowDialog`
  4. 選択 GUID で `ApplyByIdAsync(SelectedProfile.Id, false, targetDesktopId: desktopId)` → ステータス表示
- 永続ターゲット(`TargetDesktopId` / Desktop 列 / `SetTargetDesktop` 等)は復活させない(ワンショットのピッカーで足りる)

### テスト・検証

- ユニットテスト: `AllowCrossDesktopApply=false` + `targetDesktopId` → 失敗・移動デリゲート未呼び出し・rebuild 未実行
- 手動スモーク: VD を2枚用意 → 別デスクトップのウィンドウ(クローク中)も移動 + 配置されること
- E_ACCESSDENIED フォールバック(`VirtualDesktopMoveHelper`)が通ることをログで確認
- `profiles.json` に `targetDesktopId` / `desktopId` が出力されないこと(未知プロパティは読み飛ばされるので移行不要)

### 注意

- 未公開 COM(`VirtualDesktopMoveHelper`)は Windows 更新で壊れうる。壊れたら VD 機能は「移動失敗」を返すだけで、他機能に影響しないことを確認しておく
- `IsWindowCloaked` / `IsWindowOnCurrentDesktop` は「他デスクトップのウィンドウを列挙から除外」したくなった時に使う
