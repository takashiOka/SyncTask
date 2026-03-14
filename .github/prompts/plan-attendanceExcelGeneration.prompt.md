# Plan: 出勤簿Excel生成 + Redmineプロジェクト別工数表示

## TL;DR
Attendance ページに、①期間内の Redmine 工数をプロジェクト別に集計・表示するテーブルと、②出勤データを既存 Excel テンプレートに流し込んでファイル保存ダイアログで書き出すボタンを追加する。

---

## Phase 1: Redmine time_entries API 追加

**目的:** 締め期間（前月21日〜当月20日）の time_entries を `user_id=me` で全件取得しプロジェクト別に集計できるようにする。

1. `RedmineService.cs` に以下を追加:
   - データクラス: `RedmineTimeEntry` (`Id`, `Project` (id/name), `Hours`, `SpentOn`)
   - レスポンスクラス: `RedmineTimeEntriesResponse` (`TimeEntries`, `TotalCount`, `Offset`, `Limit`)
   - メソッド: `GetTimeEntriesForPeriodAsync(settings, from, to, cancellationToken)`
     - API: `time_entries.json?user_id=me&from=YYYY-MM-DD&to=YYYY-MM-DD&limit=100&offset=N`
     - ページング（GetIssuesForProjectAsync と同じ方式でループ）

## Phase 2: Attendance UI — プロジェクト別工数テーブル

**目的:** 出勤簿表の下にプロジェクト別集計テーブルを表示。手動トリガーボタン付き。

2. `Attendance.razor.cs` に追加:
   - `[Inject] private RedmineService RedmineService { get; set; }`
   - `List<ProjectHourEntry> projectHours = new()` (内部 record: ProjectName + Hours)
   - `bool isLoadingRedmine` state
   - `LoadRedmineHoursAsync()` メソッド: Redmine 設定を取得し `GetTimeEntriesForPeriodAsync` を呼び、プロジェクト名でグループ化・合計
   - Redmine 未設定（BaseUrl or ApiKey が空）の場合は無音でスキップ
3. `Attendance.razor` に追加（`attendance-sheet` セクションの下):
   - 「Redmine工数を取得」ボタン + ローディング表示
   - `@if (projectHours.Count > 0)` でプロジェクト別テーブルを表示（プロジェクト名 / 工数[h]）

## Phase 3: ファイル保存サービス（cross-platform DI）

**目的:** Excel バイト列を受け取り、プラットフォームに応じたファイル保存ダイアログを開く。

4. `Services/IFileSaveService.cs` 新規作成:
   ```csharp
   interface IFileSaveService {
       Task<bool> SaveFileAsync(string defaultFileName, byte[] contents);
   }
   ```
5. `Platforms/Windows/FileSaveService.cs` 新規作成:
   - `Windows.Storage.Pickers.FileSavePicker` を使用
   - `WinRT.Interop.InitializeWithWindow` で HWND を渡す（MAUI の `Application.Current.Windows[0].Handler.PlatformView as MauiWinUIWindow` から取得）
   - フィルター: `.xlsx`
6. Android / iOS / MacCatalyst 向け fallback: `Documents` フォルダに自動保存する実装（各 Platforms/ サブフォルダ）
7. `MauiProgram.cs` に `builder.Services.AddSingleton<IFileSaveService, FileSaveService>()` 登録（プラットフォーム別）

## Phase 4: AttendanceExcelService

**目的:** EPPlus でテンプレートを読み込みデータを流し込む。セル番地は定数で集約し後で修正可能にする。

8. `Services/AttendanceExcelService.cs` 新規作成:
   - テンプレートパス: `Resources/Raw/attendance_template.xltx`（`FileSystem.OpenAppPackageFileAsync` で読み込み）
   - セル番地定数をクラス先頭に集約（後でテンプレートに合わせて修正）:
     ```csharp
     // TODO: テンプレート確定後にセル番地を修正
     private const int DataStartRow = 9;          // 日付データ開始行
     private const string DateColumn = "B";       // 日付列
     private const string DayOfWeekColumn = "C";  // 曜日列
     private const string AttendanceColumn = "D"; // 出勤○列
     private const string StartTimeColumn = "F";  // 出勤時刻列
     private const string EndTimeColumn = "G";    // 退勤時刻列
     private const string WorkHoursColumn = "H";  // 勤務時間列
     // プロジェクト別は別シートまたは別テーブル範囲（TODO）
     ```
   - メソッド: `GenerateAsync(AttendanceMonthlyReport report, List<ProjectHourEntry> projectHours) → byte[]`
   - EPPlus ライセンス設定 (`ExcelPackage.LicenseContext = LicenseContext.NonCommercial`)

## Phase 5: Attendance UI — 出勤簿生成ボタン

**目的:** ボタン1クリックで Excel 生成 → ファイル保存ダイアログ。

9. `Attendance.razor.cs` に追加:
   - `[Inject] private AttendanceExcelService ExcelService { get; set; }`
   - `[Inject] private IFileSaveService FileSaveService { get; set; }`
   - `bool isGenerating` state
   - `GenerateExcelAsync()` メソッド:
     1. `ExcelService.GenerateAsync(report, projectHours)` でバイト列生成
     2. `FileSaveService.SaveFileAsync($"出勤簿_{targetMonth:yyyy_MM}.xlsx", bytes)` で保存ダイアログ
     3. `statusMessage` で成功/失敗を表示
10. `Attendance.razor` の `attendance-sheet__summary` にボタン追加:
    ```html
    <button class="btn btn--primary" @onclick="GenerateExcelAsync" disabled="@isGenerating">
        出勤簿生成
    </button>
    ```

---

## 関連ファイル
- `SyncTask/Services/RedmineService.cs` — Phase 1: time_entries API 追加
- `SyncTask/Services/DailyLogService.cs` — 変更なし（GetOrCreateRedmineSettingsAsync を再利用）
- `SyncTask/Services/AttendanceExcelService.cs` — Phase 4: 新規作成
- `SyncTask/Services/IFileSaveService.cs` — Phase 3: 新規作成
- `SyncTask/Platforms/Windows/FileSaveService.cs` — Phase 3: 新規作成
- `SyncTask/Platforms/Android/FileSaveService.cs` — Phase 3: fallback 新規作成
- `SyncTask/Platforms/iOS/FileSaveService.cs` — Phase 3: fallback 新規作成
- `SyncTask/Platforms/MacCatalyst/FileSaveService.cs` — Phase 3: fallback 新規作成
- `SyncTask/Components/Pages/Attendance.razor` — Phase 2, 5: UI追加
- `SyncTask/Components/Pages/Attendance.razor.cs` — Phase 2, 5: ロジック追加
- `SyncTask/Components/Pages/Attendance.razor.css` — Phase 2, 5: スタイル追加
- `SyncTask/MauiProgram.cs` — Phase 3: DI登録
- `SyncTask/Resources/Raw/attendance_template.xlsx` — Phase 4: テンプレート配置（ユーザーが用意）
- `SyncTask/SyncTask.csproj` — 変更なし（EPPlus 7.4.1 は既存）

## 検証
1. Redmine設定なしの場合にプロジェクト別テーブルが表示されないことを確認
2. Redmine設定ありで「Redmine工数を取得」→ テーブルに集計結果が表示されること
3. 「出勤簿生成」→ ダイアログが開き、.xlsx が保存されること
4. 保存した Excel をExcelで開いて日付・時刻・勤務時間が正しく入っていること
5. Redmine工数がExcelのプロジェクト別セクションに反映されること（テンプレート確定後）

## 決定事項・スコープ
- セル番地はテンプレート確定後（Phase 4 で定数として集約し後で修正）
- Redmine工数は `user_id=me` のみ（他ユーザーは対象外）
- EPPlus は既存パッケージ（v7.4.1）を使用、追加 NuGet なし
- ファイル保存ダイアログは Windows 側 `FileSavePicker` WinRT、他プラットフォームは Documents フォルダへ自動保存
- テンプレートは `Resources/Raw/attendance_template.xlsx` としてアプリに同梱
- MauiWinUIWindow からの HWND 取得はありのまま（#if WINDOWS コンパイル条件）
