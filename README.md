# SyncTask

SyncTask は、勤務時間の入力、Redmine への実工数登録、勤怠集計、メール本文生成までを一つのアプリで扱うための .NET MAUI + Blazor アプリケーションです。

主な目的は、日々の作業記録を簡単に入力し、Redmine のプロジェクト・ストーリー・タスク情報と紐づけて、勤務報告メールや工数登録を効率化することです。

## 機能概要

- 日次の作業入力
  - 日付ごとに対象日を選択
  - プロジェクト / ストーリー / タスク / 工数 / 実勤務時間を管理
  - 休憩・私用の区分を含む作業行の追加・削除
- Redmine 連携
  - Base URL と API Key を設定してプロジェクト一覧を取得
  - ストーリー用トラッカー / タスク用トラッカーを設定可能
  - 実工数を Redmine の time_entries に登録
  - 既存の issue を参照して状態確認
- 勤怠管理
  - 月次の勤務サマリー表示
  - 勤怠データの集計と平日/休日判定
  - 出退勤・労働時間の手動修正に対応
- Excel 出力
  - 勤怠データを Excel として出力・保存
- メール作成支援
  - 作成済みの勤務報告本文をプレビュー
  - コピーやメール起動の支援

## アーキテクチャ

### 全体構成

このアプリは、Microsoft の .NET MAUI と Blazor WebView を組み合わせたハイブリッド型アプリです。

- UI 層: MAUI + Blazor
- 画面構成: `Components/Pages` 配下の Razor ページ
- ビジネスロジック: `Services` 配下のサービスクラス
- データモデル: `Data` 配下の DTO / エンティティ
- 永続化: SQLite

### 主要コンポーネント

- `SyncTask/Components/Pages/Home.razor`
  - 日次入力のメイン画面
  - カレンダー、工数入力、メール本文生成を担当
- `SyncTask/Components/Pages/RedmineSettingsPage.razor`
  - Redmine 接続先などの設定画面
- `SyncTask/Services/DailyLogService.cs`
  - SQLite への保存読み込み、勤怠や設定の管理
- `SyncTask/Services/RedmineService.cs`
  - Redmine API へのアクセスと取得処理
- `SyncTask/Services/AttendanceExcelService.cs`
  - Excel 出力処理
- `SyncTask/Services/JapaneseHolidayService.cs`
  - 休日判定
- `SyncTask/Data/*`
  - `WorkLog`, `WorkLogEntry`, `RedmineSettings` などのデータ定義

### データ保存先

ローカル環境の AppData 配下に SQLite データベースを保存します。

例:

- Windows: `%LOCALAPPDATA%\Oka\SyncTask\user_data.db3`

これにより、アプリを再起動しても入力した日次ログや Redmine 設定を保持できます。

### 外部連携

- Redmine API
  - `projects.json`
  - `issues.json`
  - `time_entries.json`
- Excel 出力
  - EPPlus を利用して勤怠データや集計結果を保存

## 技術スタック

- .NET 10
- .NET MAUI
- Blazor Hybrid
- SQLite.NET
- EPPlus
- Redmine REST API

## 前提条件

### 必須

- Windows 10 / 11
- .NET SDK 10.x
- Visual Studio 2022
  - .NET MAUI ワークロード
  - Windows デスクトップ開発ワークロード

### 推奨

- Redmine サーバーと API キー
- 対象プロジェクトの tracker 名（Story / Task）
- PDF / Excel の出力を扱う場合のローカル環境権限

## インストールとセットアップ

1. リポジトリをクローンします

   ```bash
   git clone <repository-url>
   cd SyncTask
   ```

2. .NET ワークロードを復元します

   ```bash
   dotnet workload restore
   ```

3. NuGet パッケージを復元します

   ```bash
   dotnet restore
   ```

4. Visual Studio 2022 でソリューションを開き、起動対象を `SyncTask` に設定します。

5. アプリを実行します。

   ```bash
   dotnet build SyncTask/SyncTask.csproj -f net10.0-windows10.0.19041.0
   ```

   もしくは、Visual Studio の F5 で起動します。

## 実行方法

### Windows での実行

```bash
dotnet build SyncTask/SyncTask.csproj -f net10.0-windows10.0.19041.0
dotnet run --project SyncTask/SyncTask.csproj -f net10.0-windows10.0.19041.0
```

### Visual Studio での実行

- ソリューションを開く
- `SyncTask` をスタートアップ プロジェクトとして設定
- `Debug` → `Start Debugging` を実行

## 初期設定

アプリ起動後、まず Redmine 設定を行います。

1. 画面上部またはナビゲーションから Redmine 設定画面を開く
2. `BaseUrl` に Redmine の URL を入力
3. `API Key` に Redmine の API キーを入力
4. `StoryTrackerNames` と `TaskTrackerNames` に対象 tracker 名をカンマ区切りで設定
5. `設定保存` を押す
6. `Redmine読込テスト` を実行して接続確認

例:

```text
BaseUrl: https://redmine.example.com
StoryTrackerNames: Story,ストーリー
TaskTrackerNames: Task,タスク
```

## プロジェクト構造

```text
SyncTask/
├── Components/
│   ├── Layout/
│   └── Pages/
├── Data/
├── Platforms/
├── Properties/
├── Resources/
├── Services/
├── App.xaml
├── App.xaml.cs
├── MainPage.xaml
├── MainPage.xaml.cs
├── MauiProgram.cs
├── SyncTask.csproj
└── ...
```

## 開発メモ

- 現在のプロジェクト設定は Windows を主対象にしています。
- iOS / Android / Mac Catalyst のターゲット定義は含まれていますが、実際の開発と検証は Windows 環境で行う前提です。
- ローカルデータは SQLite に保存されるため、バックアップや初期化が必要な場合は `user_data.db3` を管理してください。

## ライセンス

このプロジェクトのライセンスは、リポジトリの付随ファイルを確認してください。ライセンスを明示していない場合は、利用前に管理者へ確認してください。
