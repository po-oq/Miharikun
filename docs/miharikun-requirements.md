# Miharikun（みはりくん）要件定義・実装方針（MVP / PoC）

> このドキュメントは実装担当（Claude Code）向け。決定事項はすべて確定済み。
> 「要検証」と書かれた項目は Step 0 の実機検証で確定させ、結果をこのファイルに追記すること。

---

## 1. 目的・背景

社内で Cursor IDE のチャット（Agent）を使って開発している。以下が困りごと。

- チャットのログが長く、あとから見ると全部読まないと「いまどうなっているか」がわからない
- IDE のチャット欄は狭くタブを多く開けない。History はタイトル数文字しか出ず探せない
- 放置したセッションを再開する／似た作業の参考にするために、セッションへ一言残したい
- 実装計画 md をフェーズ単位で複数セッションに分けて実行している（計画との紐付けは MVP 対象外）

### 発想の元になったアイデア
- https://x.com/tokkyo/status/2105860432405954572
- https://x.com/tokkyo/status/2105862649540173872
- https://x.com/tokkyo/status/2104584770856599989

**ゴール**：プロジェクトごとに起動するデスクトップツールで、そのプロジェクトの Cursor セッションを
「一覧で探せる」「状態が3秒でわかる」「概要とメモを残せる」ようにする。

## 2. 用語

| 用語 | 意味 |
|---|---|
| セッション | Cursor のチャット1つ（`conversation_id`）、または Claude Code のセッション1つ（`sessionId`）。`SessionKey(AgentId, SessionId)` で識別 |
| イベント | Cursor Hooks から hook exe に渡される1回分の JSON、または Claude Code の会話ログの1行。どちらも App 側で共通イベント `AgentEvent` に直す |
| ターン | ユーザー入力1回（Cursor は `beforeSubmitPrompt`、Claude Code は人の入力）〜 `stop`（Claude Code は `end_turn` ほか）まで |
| 概要 | チャットに1行要約させた返事を取り込んだもの（セッションごとに1つ） |
| メモ | 人間が自由に書くセッションごとのメモ |
| 状態 | Hook のイベントから**自動判定**するセッションの様子（実行中・ボスの番・停止・エラー・閉じた）。10章 |
| ステータス | **ユーザーが手で設定**するセッションの進み具合（作業中・中断・完了。未設定あり）。自動では変わらない。12章 |
| プロジェクトメモ | プロジェクト単位の自由メモ（Markdown。1 プロジェクトに 1 つ）。メモタブで読み書きする（12.10）。セッションごとの「メモ」とは別 |

## 3. 前提・制約（確定）

- 対象は **Cursor IDE のチャット**と **Claude Code**（CLI と、Claude デスクトップの Code タブ。どちらも同じ会話ログを使う）。cursor-agent CLI は社内で使用不可のため対象外
- **ツール単体で動作**させる：LLM 呼び出しはしない
- Cursor の `state.vscdb` は**読まない**（非公式・巨大・破損リスク）。コピーも禁止
- Claude Code の会話ログ（`%USERPROFILE%\.claude\projects\`）は**読み取りだけ**。書き換え・削除をしない。ログの形式は公開された仕様ではないので、読めない行は飛ばして動き続ける（11.1）
- トークン数・コスト・レート制限は取得手段がないため**扱わない**（Claude Code の会話ログに使用量があっても表示しない）
- 管理者権限は不要であること（ユーザー階層 `~/.cursor/hooks.json` のみ使用。Claude Code は Hook 不要で、設定ファイルにも触れない）
- 対象 OS は Windows。開発は自宅、会社では GitHub Release から exe をダウンロードして使う

## 4. スコープ

### MVP 対象
- ダッシュボードタブ（一覧／詳細／右ペイン）
- ドキュメントタブ（12.7。Issue #9）
- Claude Code のセッションを、Cursor と同じ一覧に表示（会話ログの読み込み。5.1・11.1・12.8。Issue #11）
- Hook exe（Cursor 用のイベント記録）と、その導入機能
- 概要・メモ・タイトルの手動編集
- メモタブ（プロジェクトメモ。12.10）

### MVP 対象外（後で実装）
- 実装計画 md とセッションの紐付け、完了判定ミニ表示（✓✓✓✗）、Cursor でチャットを開くボタン、タスク・課題の更新、承認待ちの判定

### 不採用
LLM 要約・要対応抽出、ctx% バー、PID、分類タブ、トークン/コスト/レート制限、関連セッション、テスト項目一覧

## 5. システム構成

```
Cursor IDE ──(stdin JSON)──▶ Miharikun.Hook.exe --agent cursor ──append──▶ events\cursor\{sessionId}.jsonl（生データ）
Claude Code ──(自分で追記)──▶ %USERPROFILE%\.claude\projects\<フォルダ名>\{sessionId}.jsonl（会話ログ。読み取りだけ）

Miharikun.exe (WPF)
  ISessionSource（エージェントごとの読み込み。CursorSessionSource / ClaudeSessionSource。差分 SessionDelta を返す）
        └─▶ 共通イベント（AgentEvent）─▶ 汎用 ProjectEventStore（保持・要約キャッシュ・変更通知）─▶ 状態判定・画面
  └─ read/write ─▶ meta\{agentId}\{sessionId}.json
```

### ソリューション構成
| プロジェクト | 種別 | 役割 |
|---|---|---|
| `Miharikun.Core` | クラスライブラリ | `IAgentInfo` / `IHookAgent`、`ISessionSource`、汎用 `ProjectEventStore`、共通イベントモデル、`CursorAgent` / `ClaudeCodeAgent`、状態判定、パス正規化。**AOT 互換で書く**（System.Text.Json はソースジェネレーター使用、リフレクション禁止） |
| `Miharikun.Docs` | クラスライブラリ | ドキュメントタブの md→HTML（12.7。Markdig・mermaid・色付け・CSS）。**Markdig はここだけが参照する**（Core に入れると NativeAOT の Hook にも依存が混ざる）。`net10.0`、Core への参照なし |
| `Miharikun.Hook` | コンソール, NativeAOT | Cursor 用。stdin を受けて JSONL に追記するだけ。高速起動が最優先。Claude Code では使わない |
| `Miharikun` | WPF | 画面。WPF-UI 4.x を使用。ドキュメントのプレビューに WebView2（Edge の Runtime）を使う |
| `Miharikun.Tests` | xUnit | Core と Docs のテスト |

- ターゲット：.NET 10（`net10.0` / `net10.0-windows`）
- UI ライブラリ：WPF-UI（lepoco/wpfui）4.x。選定理由は継続的に保守されていること
- MVVM：CommunityToolkit.Mvvm
- 配布：`dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`（App）、NativeAOT（Hook）。両 exe を zip にして GitHub Release に置く

## 5.1 エージェント抽象化（実装方針）

Cursor と Claude Code に対応し、将来 Codex / GitHub Copilot にも対応する予定のため、**エージェント固有の処理を、エージェントごとの小さなクラスに閉じ込める**構成にする。
2 つのエージェントは、データの取り方が違う（Cursor は Hook でイベントを受け取る／Claude Code は会話ログを読むだけ）。そのため、**「保持・要約・通知」の汎用部分と、「どこから・どんな形式で読むか」のエージェント固有部分を分ける**（Issue #11）。

### ルール
1. **`IAgentInfo`**（Id・表示名・Capabilities）は全エージェントが実装する。**`IHookAgent : IAgentInfo`**（Hook 側の処理）は、Hook を使うエージェント（Cursor）だけが実装する。Claude Code は `IAgentInfo` だけ
2. **読み込みは `ISessionSource` をエージェントごとに 1 つ**（`CursorSessionSource`、`ClaudeSessionSource`）。返すのは**差分（`SessionDelta`）**：追記 / 作り直し / 消えた のどれかと、`AgentEvent` の列。**プロジェクトとの照合（Cursor は `workspace_roots`、Claude Code は `cwd`）は Source の中で行う**。「Hook があれば transcript より優先」のような、エージェント固有の事情も Source の中に閉じる
3. **保持・要約（`SessionSummary`）のキャッシュ・変更通知は、汎用の `ProjectEventStore` 1 つ**が行う。エージェントのことは知らない。`SessionMonitor` は Store を 1 つ持ち、各 Source の監視先（`WatchTargets`）をまとめて監視する。Store は Source ごとに例外を捕まえ、他の Source は動き続ける（13 章）。差分の出し方の細かい規則（Cursor の「作り直し」「取り込み済み → Hook」など）は実装計画 `docs/issue11/issue11-claude-code-plan.md` 8.1
4. **App（状態判定・画面・検索）は共通イベント `AgentEvent` だけを扱う**。生 JSON のフィールド（Cursor の `stop.status`・`workspace_roots`、Claude Code の `stop_reason`・`cwd` 等）を App 側で直接参照してはならない。ツール名も共通名に直す（`CommonTools.Shell` ほか。Claude Code の `Bash` / `PowerShell` → `Shell`）
5. **Hook exe は生データを保存するだけ**（Cursor 用）。正規化（生 JSON → 共通イベント）は App が読み込み時に行う（正規化を修正すれば過去データにも反映される）
6. **能力差は `Capabilities` フラグで表現**し、UI はフラグを見て表示の有無を決める
7. **共通の抽象基底クラスは作らない**。`ISessionSource` と汎用 `ProjectEventStore` 以外は、実際に重複した処理だけを切り出す
8. **表示名・Capabilities は `AgentCatalog`（AgentId → `IAgentInfo`。固定の switch）から引く**（App 用）。`AgentRegistry` は Hook exe 用で `IHookAgent` だけを返す

### interface（目安。実装時に調整してよい）
```csharp
public interface IAgentInfo
{
    string Id { get; }                      // "cursor" / "claude"
    string DisplayName { get; }
    AgentCapabilities Capabilities { get; }
}

// Hook を使うエージェント（Cursor）だけ。Hook exe（NativeAOT）が使うので AOT 互換で実装すること
public interface IHookAgent : IAgentInfo
{
    string? GetEventName(JsonNode payload);
    string? GetSessionId(JsonNode payload);
    bool NeedsGitSnapshot(string eventName);
    HookResponse RespondToHook(string eventName, JsonNode payload);   // stdout と終了コード（7章の表）
}

public interface ISessionSource
{
    string AgentId { get; }
    IReadOnlyList<WatchTarget> WatchTargets { get; }                  // SessionMonitor が監視するフォルダ。毎回取り直す（あとから増える）
    IReadOnlyList<SessionDelta> ReadNew();                            // 追記分だけ読む。呼び出しは Store の所有スレッド
    IReadOnlyList<AgentEvent> GetSubagentEvents(SessionKey key, string subagentId);   // 今は空。将来、サブエージェントの詳細用
}

public sealed record SessionDelta(SessionKey Key, SessionDeltaKind Kind, IReadOnlyList<AgentEvent> Events);
public enum SessionDeltaKind { Append, Replace, Remove }   // 空の Replace は Remove と同じ。空の Append は無視
public sealed record WatchTarget(string Path, bool CreateIfMissing);   // Cursor の events は作る。Claude（.claude 配下）は作らない

[Flags]
public enum AgentCapabilities
{
    None = 0, RealtimeHooks = 1, ToolEvents = 2, AssistantText = 4, Thinking = 8,
    Compaction = 16, Subagents = 32, FileEdits = 64, SessionEnd = 128,
    TurnStatus = 256,     // 完了/中断/エラーを区別できる
    Transcript = 512, TokenUsage = 1024,
}

public sealed record SessionKey(string AgentId, string SessionId);

public enum AgentEventKind
{
    SessionStarted, SessionEnded, PromptSubmitted, TurnEnded,
    ToolStarted, ToolSucceeded, ToolFailed,
    AssistantMessage, AssistantThought,
    SubagentStarted, SubagentStopped, FileEdited, Compacted,
}

public sealed record AgentEvent(
    SessionKey Session, long Seq, DateTimeOffset At, AgentEventKind Kind,
    string? Text = null, string? ToolName = null, string? ToolUseId = null,
    string? Command = null, int? ExitCode = null, TurnOutcome? Outcome = null,
    string? FilePath = null, CompactionInfo? Compaction = null,
    GitSnapshot? Git = null, string? Model = null,
    string? SubagentId = null /* Issue #11 で追加。サブエージェントの識別 */);
    // ほかに Output / Duration / Reason / ModelParams / TranscriptPath / Imported（Phase 3〜9 で追加済み）。
    // Imported は「時刻が推定のもの（Cursor の transcript だけから作った過去セッション）」の意味

public enum TurnOutcome { Completed, Aborted, Error, Unknown }
```

### Cursor のイベント対応表（`CursorAgent.Normalize`）
| Cursor | AgentEventKind | 備考 |
|---|---|---|
| sessionStart | SessionStarted | git 付き |
| sessionEnd | SessionEnded | reason を保持 |
| beforeSubmitPrompt | PromptSubmitted | Text = prompt |
| stop | TurnEnded | Outcome = status（completed/aborted/error）、git 付き |
| preToolUse | ToolStarted | ToolUseId, ToolName, Command |
| postToolUse | ToolSucceeded | Shell は tool_output の exitCode を ExitCode に |
| postToolUseFailure | ToolFailed | |
| afterAgentResponse / afterAgentThought | AssistantMessage / AssistantThought | |
| subagentStart / subagentStop | SubagentStarted / SubagentStopped | |
| afterFileEdit | FileEdited | FilePath |
| preCompact | Compacted | 使用率・trigger 等 |

Cursor の Capabilities：`RealtimeHooks | ToolEvents | AssistantText | Thinking | Compaction | Subagents | FileEdits | SessionEnd | TurnStatus`（Transcript は Step 0 の結果次第、TokenUsage はなし）


Cursor は、ツール系などのイベントで `model` に `"default"` を入れてくる（実機で確認）。これを無視する処理は `CursorAgent.Normalize` の中で行う（状態判定側は Cursor を知らない）。

### Claude Code のイベント対応表（`ClaudeTranscriptNormalizer`。Issue #11）
会話ログの 1 行を共通イベントに直す。**状態を持つ**（ツール呼び出しと結果の対応・経過時間のため）。**本体のログにも、サブエージェントのログにもそのまま使える**形にする（7 章の将来の詳細表示のため）。

| Claude Code の記録 | AgentEventKind | 備考 |
|---|---|---|
| ファイルの最初の記録（`timestamp` のあるもの） | SessionStarted | 時刻は最初の `timestamp`。ブランチは `gitBranch`（hash は無し。コミットは App が git で、ブランチと時刻から求める。10.1）。**実ログ（2.1.286）の最初の記録は `queue-operation` で、`cwd` も `gitBranch` も無い**ので、ブランチは「最初に `gitBranch` を持つ行」の最初のイベントの Git に載せ、変わったときにも載せる（セッションの途中の切り替えを追える） |
| `user` の人の入力：文字の行で、`isMeta` でない・`tool_result` でない・**`origin` が無いか `origin.kind=human`**・`[Request interrupted` で始まらない | PromptSubmitted | Text = `message.content`。古い版（2.1.156）は `origin` 欄が無い。`origin.kind=task-notification`（裏の作業の終わりの通知）は入力にしない。スラッシュコマンドや `<local-command…>` はそのまま文字で出す（実機で確認） |
| `assistant` の `text` | AssistantMessage | Model = `message.model`。**`<synthetic>`（合成の返答）は null**（Cursor の `"default"` と同じ扱い） |
| `assistant` の `thinking` | AssistantThought | **本文（`thinking`）が空・空白だけのものは出さない**（実ログ 2.1.286 の思考 700 件のうち 648 件は、本文が空で `signature` だけ。Claude Code が思考の本文を記録していない） |
| `tool_use` | ToolStarted | ToolUseId = `id`。**ToolName は共通名に直す**：`Bash` と `PowerShell` → `Shell`、ほかは元の名前（Read・Edit・Write・Grep・`mcp__…`）。Command = `input.command` |
| `tool_result`（`is_error=false`） | ToolSucceeded | Shell の ExitCode = 0。**ただし裏で動かした Bash（`input.run_in_background`、または結果に `backgroundTaskId`）は ExitCode = null**（すぐ返るため、結果は不明）。Output = **Shell だけ内容の末尾 2000 字**（ほかは持たない。メモリのため）。Duration = 結果の時刻 − 呼び出しの時刻 |
| `tool_result`（内容が `Exit code N` で始まる） | **ToolSucceeded**（ExitCode = N） | コマンドは実行できて、終了コードが 0 でなかった。**失敗したテストを成果に出すため、ToolFailed にしない**（テスト判定は ToolSucceeded のとき記録する） |
| `tool_result`（その他の `is_error`：拒否・中断など） | ToolFailed | |
| 編集の成功（`Edit`・`Write`・`MultiEdit`・`NotebookEdit`） | FileEdited | FilePath = `input.file_path`（`NotebookEdit` は `notebook_path`）。`is_error` のときは出さない |
| `Agent` の `tool_use` | SubagentStarted | ToolUseId = `id`。Text = 説明（`description`）、ToolName = 種類（`subagent_type`） |
| `Agent` の結果（**`toolUseResult.status = completed`** のとき） | SubagentStopped | SubagentId = `agentId`。ToolUseId で Started と対応。`completed` 以外（裏で動かした）は動いているまま、`task-notification` の `<tool-use-id>` が一致したら SubagentStopped。**結果が `is_error`（拒否・中断・失敗）のときも終わり**（動いていないのに「動いている」が残らないように。実ログでの確認は Phase 22） |
| `assistant` の `stop_reason = end_turn` | TurnEnded（Completed） | **本文（`text`）を含む行で 1 回だけ**（思考の行と本文の行の両方に付くことがある。同じ `message.id` では 1 回）。`stop_hook_summary` / `turn_duration` は使わない（重複） |
| `[Request interrupted…]` | TurnEnded（Aborted） | `origin` は無い |
| `isApiErrorMessage` の返答 | TurnEnded（Error） | `system` の `api_error` は再試行されることがあるので、最初は使わない（実機で確認） |
| `AskUserQuestion`・`ExitPlanMode` の `tool_use` | TurnEnded（Completed） | ユーザーの返事待ち＝ボスの番。その結果（回答・承認）が来たら PromptSubmitted（Text は「（回答）」＋内容）にして、実行中に戻す。**回答も依頼数・ターン数・最近の入力に数える** |
| 圧縮（コンパクト） | （未対応） | 実物が無く未確認。出たら Compacted を追加 |
| `custom-title` / `ai-title` | （後回し） | セッションのタイトル候補。最初は「最初の依頼の先頭 40 文字」のまま |
| 上記以外（`attachment`・`queue-operation`・`file-history-*` など） | 読み飛ばす | 知らない種類・壊れた行も、止まらずに飛ばす（ログは 11.1） |

Claude Code の Capabilities：`ToolEvents | AssistantText | Thinking | Subagents | FileEdits | TurnStatus | Transcript`（`RealtimeHooks`・`SessionEnd`・`Compaction` はなし。`Compaction` は実機で確認できたら追加。`TokenUsage` は扱わない）

### 将来の拡張ポイント（参考）
- 他のエージェントは、`ISessionSource` を 1 つ足す（Hook を使うなら `IHookAgent` も）。保持・要約・画面は変えない
- エージェントごとに hook の設定場所・イベント名・承認系 hook の返し方が異なるため、それらは各クラスで吸収する
- トークン使用量を取得できるエージェントが入った場合は、`TokenUsage` フラグを持つセッションに限りトークン欄を表示する
- hook 導入処理（`HookInstaller` / `HookSetup`）は、いまは Cursor 専用（名前は変えず、クラスのコメントに「Cursor 専用」と明記する）。別のエージェントが Hook を使うことになったら、そのときに抽象化する。Claude Code は導入不要
- サブエージェントの詳細（7 章の将来）：`GetSubagentEvents` を実装して、`<セッション>\subagents\agent-<id>.jsonl` を同じ変換部品で読む

## 6. 保存データ

ルート：`%LOCALAPPDATA%\Miharikun\`

| パス | 内容 | 書き手 |
|---|---|---|
| `events\{agentId}\{sessionId}.jsonl` | 生イベント1件1行（Cursor は sessionId = conversation_id） | Cursor の Hook のみ（追記専用） |
| `events\{agentId}\_app.jsonl` | セッション ID を持たないイベント（MVP では未登録） | Hook |
| `meta\{agentId}\{sessionId}.json` | タイトル・概要・メモ・ステータス | App |
| `projects\{slug}-{hash8}.json` | プロジェクトごとの設定（ドキュメントタブの除外パターン） | App |
| `projects\{slug}-{hash8}.memo.md` | プロジェクトメモ（12.10）。中身はただの Markdown（UTF-8・BOM なし）。名前の `{slug}-{hash8}` はプロジェクト設定と同じ。無ければ「メモなし」 | App |
| `webview2\` | WebView2 の作業フォルダ（キャッシュ・Cookie 等。消してよい。自動掃除しない） | WebView2 |
| `preview\` | md を HTML にした一時ファイル（1 md＝1 ファイル。起動時に 1 日より古いものを削除） | App |
| `settings.json` | アプリ全体の設定（テーマ、実行中の停止判定の時間、Cursor の Hook exe の置き場所。将来はテストコマンドのパターン等） | App |
| `logs\hook-error.log` | Hook の例外ログ | Hook |

### イベント行スキーマ（Hook が書く）
```json
{
  "v": 1,
  "agent": "cursor",
  "received_at": "2026-10-03T11:19:31.123+09:00",
  "event": "beforeSubmitPrompt",
  "git": { "branch": "feature/x", "head": "56c958d..." },
  "payload": { ...Cursorから受け取ったJSONそのまま... }
}
```
- `git` は `sessionStart` と `stop` のときだけ付与。それ以外は省略
- `payload` は加工せず丸ごと保存（後から仕様が増えても対応できるように）

### メタスキーマ（App が書く）
```json
{
  "v": 1,
  "title": "テストタブの追加",
  "titleIsManual": true,
  "summary": { "text": "...", "importedAt": "...", "sourceTurn": 4 },
  "previousSummary": { "text": "...", "importedAt": "...", "sourceTurn": 2 },
  "memo": "6〜8まで。続きは明日",
  "status": "working",
  "updatedAt": "..."
}
```
- `status`：省略（未設定）/ `"working"`（作業中）/ `"paused"`（中断）/ `"done"`（完了）。既存ファイルは項目が無い＝未設定として読み、`v` は上げない。知らない値は未設定として扱う
- 書き込みは一時ファイル → `File.Replace` でアトミックに。複数起動の競合は後勝ちで可

### プロジェクト設定（projects\{slug}-{hash8}.json）
```json
{ "v": 1, "path": "c:\\work\\proj", "documents": { "ignore": ".git/\nnode_modules/\n...", "lastOpened": "docs/README.md" } }
```
- ファイル名：対象フォルダのフルパスを、末尾区切りなし・小文字にしたもの。`slug`＝英数字以外の連なりを `-` にしたもの（読みやすさ用）、`hash8`＝そのパスの SHA-256 先頭8桁（日本語・記号で slug が衝突しても別ファイルになる）
- `path`：対象フォルダのフルパス（人が見て分かるようにするためだけ。判定には使わない）
- `documents.lastOpened`：最後に開いたファイルの相対パス（無ければ復元しない）
- `documents.ignore`：gitignore 形式のテキスト（改行区切り）。キーが無い・ファイルが無い・壊れているときは既定値（12.7）を使う。他のキーは今は無い（将来、プロジェクトごとの設定はここに足す）

### settings.json
```json
{ "theme": "system", "runningTimeoutMinutes": 10, "hookDir": "C:\\dev\\miharikun-hook" }
```
- `hookDir`：Cursor の Hook exe を置くフォルダ（8 章。Issue #17）。欠けている・空・文字列でないときは既定の `%LOCALAPPDATA%\Miharikun\bin`。⚙ メニューの「設定…」で変える（12.9）。起動時に hooks.json の登録から受け入れたときも、ここに書く
- `theme`：`"system"`（OS のライト/ダークに追従。既定）/ `"light"` / `"dark"`。ファイルなし・壊れている・知らない値は `system`
- `runningTimeoutMinutes`：「実行中」のまま新しい記録が来ない時間（分）。超えると「停止」と表示する（10.1）。既定 `10`。`0` 以下で無効。整数以外・壊れている・欠けているときは既定値。⚙ メニューの「設定…」の画面で変える（12.9）。保存するとすぐ効く
- 書き込みは、**ファイル全体を読み、変えるキーだけを書き換えて保存する**（知らないキーも残す）。テーマ（⚙ メニュー）と設定画面が同じファイルに書くため、片方の保存で他方の値を消さない

## 7. Hook exe 仕様

### 登録するイベント
`sessionStart`, `sessionEnd`, `beforeSubmitPrompt`, `preToolUse`, `postToolUse`, `postToolUseFailure`,
`subagentStart`, `subagentStop`, `afterFileEdit`, `afterAgentResponse`, `afterAgentThought`, `preCompact`, `stop`

（`beforeReadFile` 等は MVP では登録しない）

### 起動
`Miharikun.Hook.exe --agent cursor`。`--agent` の値で `IAgent` 実装を選ぶ（MVP は cursor のみ。未知の値は exit 1）

### 処理
1. stdin を UTF-8 で全読み → JSON パース → `hook_event_name`, `conversation_id` を取得
2. `sessionStart` / `stop` のときだけ git 情報を取得
   - 対象は `workspace_roots[0]`。`git -C <root> rev-parse --abbrev-ref HEAD` と `rev-parse HEAD`
   - 各コマンドのタイムアウト 1.5 秒。git リポジトリでない／失敗時は `git` を省略
3. `events\cursor\{conversation_id}.jsonl` に1行追記（`FileShare.Read` で開き、IOException は 20ms 間隔で最大10回リトライ）
4. 出力ルール（`CursorAgent.RespondToHook` に実装）：

| イベント | 出力 | 終了コード |
|---|---|---|
| 承認系（`preToolUse`, `subagentStart`） | **何も出力しない** | **1**（失敗扱い＝判定に参加しない） |
| `beforeSubmitPrompt` | `{"continue":true}` | 0 |
| その他 | `{}` | 0 |
| 例外発生時 | 何も出力しない | 1（fail-open）。`logs\hook-error.log` に記録 |

> **理由**：承認系 hook が `allow` を返すと Cursor 本来の確認ダイアログに影響する可能性がある（要検証）。
> exit 0 で不正 JSON を返すとアクションがブロックされるため、記録専用 hook は exit 1 で抜ける。

### 性能要件
- 1回の処理は git なしで 50ms 以内、git ありで 3 秒以内（`hooks.json` の `timeout` は 5 秒）
- `postToolUse` 等の高頻度イベントでは外部プロセスを起動しない

## 8. Hook の導入機能（App）

- 起動時に `%USERPROFILE%\.cursor\hooks.json` を確認し、Miharikun の hook が未登録なら導入ダイアログを出す
- 導入処理：
  1. 同梱の `Miharikun.Hook.exe` を **Hook の置き場所**（既定 `%LOCALAPPDATA%\Miharikun\bin\`。`settings.json` の `hookDir` で変更可）にコピー
  2. 既存 `hooks.json` を `hooks.json.bak-{日時}` にバックアップ
  3. 既存設定を**壊さずにマージ**（各イベントの配列に自分のエントリを追加。重複は追加しない）
  4. `version` がなければ `1` を設定
- コマンドはフルパス（`/` 区切り）。スペースを含む場合の扱いは Step 0 で確定
- 「hook を削除」メニューも用意（自分のエントリだけ取り除く）

### 8.2 Hook の置き場所（Issue #17）
会社の PC などでは、`%LOCALAPPDATA%` 配下の exe の実行が止められることがある（実機で確認。`C:\dev` のようなフォルダなら動いた）。Hook exe が起動できないと何も記録されず、`hook-error.log` も残らない。
- **置き場所を設定にする**：⚙ →「設定…」に「Cursor の Hook exe の置き場所」（フォルダ）。既定は `%LOCALAPPDATA%\Miharikun\bin`。導入（コピーと hooks.json への登録）も、起動時の確認も、この場所を使う。変えて保存したら、その場所に導入し直すかを聞く（「はい」で導入。古い場所の exe は消さない）
- **既存の登録を受け入れる**：起動時の確認で、hooks.json の Miharikun の登録が、**登録する全イベントで同じ 1 つの別の場所**を指し、その exe が実在するときは、ダイアログを出さずにその場所を置き場所の設定として採用する（`app.log` に 1 行）。そのうえで中身が同梱と違えば、従来どおり「更新」を提案し、更新は**その場所に**コピーする。場所がばらばら・exe が無いときは、従来どおり「登録し直します」を提案する
- 導入ダイアログの「今後表示しない」は付けない（Issue #17 の Q5）

## 9. App：起動とプロジェクト判定

- 起動：`Miharikun.exe [フォルダ]`。省略時はカレントディレクトリ
- プロジェクト一致判定：`Path.GetFullPath` → 末尾区切り除去 → `StringComparison.OrdinalIgnoreCase` で**完全一致**。
  セッションの `workspace_roots` のどれか1つでも一致すれば対象（Claude Code は各ファイルの最初の `cwd`。9.1）
- タイトルバーに対象フォルダを表示。複数起動を前提とし、App は events を**読み取り専用**で扱う

### ファイル監視
- `FileSystemWatcher` で `events\` を監視（Cursor。Claude Code は 9.1）、300ms デバウンス。取りこぼし対策で 3 秒ごとのポーリングも併用
- Cursor は、対象プロジェクトの `agent-transcripts\` も監視する（サブフォルダ込み。読むだけで、無ければ作らない。11 章。Issue #17）
- ファイルごとに読み取り済みオフセットを保持し、追記分だけ読む（末尾の不完全な行は次回に回す）
- 初回起動時は全ファイルを読み、`workspace_roots` が一致するものだけをメモリに保持

### 9.1 Claude Code の探索と照合（Issue #11）
- 探索先：`%USERPROFILE%\.claude\projects\`（`MIHARIKUN_CLAUDE_DIR` で `.claude` の場所を上書きできる。検証用）。フォルダ名が「対象フォルダの Claude 式の名前」と同じ、または、それに `--claude-worktrees-` が続くものだけを開く。Claude 式の名前は、`C:\zDev\repo\Miharikun` → `C--zDev-repo-Miharikun`（`:` と `\` と `.` など英数字以外が、それぞれ `-`。Cursor の slug と違い、連なりを 1 つにまとめない）。名前の比較は大文字小文字を無視。**候補が 0 件のときだけ**、起動ごとに 1 回、全フォルダの各ファイルの先頭の `cwd` だけを読んで探す（日本語・記号を含むパスで規則が違った場合の保険。見つかったらログに残す）
- 照合：各ファイルの最初の `cwd` を `ProjectPath`（上の一致判定）で比べる。フォルダ名の規則の違いに頼らない。**作業ツリー**は、`cwd` が「対象フォルダ\.claude\worktrees\<名前>」のものを同じプロジェクトとして扱う
- 監視：`FileSystemWatcher`（300ms デバウンス）と 3 秒ごとのポーリング。ファイルごとに読み取り済みオフセットを保持し、追記分だけ読む（末尾の不完全な行は次回に回す。`JsonlTail`）。監視先は**ポーリングのたびに取り直す**（作業ツリーのフォルダはあとからできる）。フォルダの直下だけを見る（`subagents\` は見ない）
- **`.claude` には何も書かない**（フォルダも作らない。監視先が無ければ、次のポーリングでまた試す）
- 初回は全ファイルを読む（最大 20MB 程度。起動は背景）。遅ければ、「直近 N 日」の設定を足す（17章）

## 10. 状態判定ルール（Core）

> 状態判定・派生値は**共通イベント（AgentEvent）に対して**実装する。以下の Cursor イベント名は対応表（5.1）経由で読み替えること。
> `TurnOutcome.Unknown` で終わったターン（将来の他エージェント用）は 🟢 として扱う。

### セッション状態（最新イベントで上書き）
| 状態 | 表示 | 条件 |
|---|---|---|
| Running | 🔵 実行中 | 最後のターン系イベントが `beforeSubmitPrompt` 以降で、まだ `stop` が来ていない |
| YourTurn | 🟢 ボスの番 | 最後の `stop.status == "completed"` |
| Aborted | 🟡 停止 | 最後の `stop.status == "aborted"` |
| Error | 🔴 エラー | 最後の `stop.status == "error"` |
| Closed | ⚪ 閉じた | 最後のイベントが `sessionEnd`（reason 問わず） |
| Imported | ⚪ 閉じた（導入前） | transcript からの取り込み（11章）。transcript の最後の更新が、Hook の登録より前 |
| NoHook | ⚪ Hook なし | transcript からの取り込み（11章）で、transcript の最後の更新が Hook の登録より後（＝Hook が記録していない。Issue #17）。状態の件数（実行中・閉じた など）には数えない |

- `sessionEnd` の後に `beforeSubmitPrompt` 等が来たら（再開）その時点の状態に戻す
- 呼び名：自動判定の Aborted は「**停止**」（ユーザーが Stop した）。ユーザーが設定するステータスの「中断」（やりかけで置いてある）と混ざらないよう分ける。ターンの状態（12.3 の 7）も同じ

### 実行中ツールの管理
- `preToolUse` で `tool_use_id` を実行中リストに追加
- `postToolUse` / `postToolUseFailure` で該当 ID を除去
- `stop` / `sessionEnd` を受けたら残りを「終了（結果不明）」として全除去（保険）

### 派生値
| 項目 | 算出 |
|---|---|
| タイトル | メタの手動タイトル優先。なければ最初の `beforeSubmitPrompt.prompt` の先頭40文字（改行は空白に） |
| 依頼数 / ターン数 | `beforeSubmitPrompt` の件数 / `stop` の件数 |
| 開始時刻 | `sessionStart` の received_at（なければ最初のイベント） |
| 最後の動き | 最後のイベントの received_at |
| モデル | 最後のイベントの `model_id`（なければ `model`）、`model_params` |
| ブランチ | 最新の `git.branch` |
| ツール呼び出し数 | `postToolUse` + `postToolUseFailure` の件数 |
| サブエージェント | 起動中 = `subagentStart` − `subagentStop`、累計 = `subagentStart` 件数 |
| 圧縮 | `preCompact` の件数と直近の `context_usage_percent`・`trigger` |
| 継続時間 | 開始〜最後の動き（`sessionEnd.duration_ms` があればそれ） |
| 変更ファイル | `afterFileEdit.file_path` の重複除去 |
| テスト実行 | `postToolUse` で `tool_name == "Shell"` かつ command が設定のパターンに一致。成否は `tool_output` の `exitCode == 0` |
| コミット | App が `git log --oneline {開始時head}..{最新stop時head}` を実行（Hook ではやらない）。head が無いセッション（Claude Code）は、ブランチと時刻から求める（10.1） |
| 未コミット | App が `git status --porcelain` を実行し、変更ファイルと突合（シェル経由の変更は対象外と画面に注記） |

テストコマンドの既定パターン（settings.json で変更可）：
`dotnet test`, `npx playwright test`, `npm test`, `npm run test`, `vitest`, `jest`, `pytest`

### 10.1 Claude Code の状態判定の差（Issue #11）
状態判定は共通イベントに対して行う（上の表のまま）。Claude Code に固有の扱い：

| 状態 | Claude Code での判定 |
|---|---|
| 🔵 実行中 | 同じ（人の入力の後〜 `end_turn` / 質問 / 中断まで） |
| 🟢 ボスの番 | `end_turn`、または `AskUserQuestion` の待ち |
| 🟡 停止 | `[Request interrupted…]`。**加えて、「実行中」のまま `runningTimeoutMinutes`（既定 10 分）新しい記録が無いもの**（プロセスが落ちた・閉じた場合。Claude Code には終了の記録が無いため） |
| 🔴 エラー | API エラーの返答（`isApiErrorMessage`） |
| ⚪ 閉じた | **無い**（終了の記録が無い）。ボスの番のまま残り、一覧では最後の動きの降順で下がっていく |

- 「実行中のまま動きなし → 停止」は、**時刻（いま）が要る**ので、要約の計算には入れず、画面側の 1 秒ごとの更新で適用する。`StalledRule.DisplayState`（Core）が**表示用の状態を 1 か所で決める**：
  1. 実行中でない、またはしきい値が 0 以下 → 要約の状態のまま
  2. 「いま − 最後の動き」がしきい値未満 → 実行中
  3. 動いているサブエージェント、または結果待ちのツールがある → **実行中のまま**、「N 分動きなし」を添える（サブエージェントの中身は別ファイルなので本体は無音になる。長いコマンド・承認待ちもここ）
  4. それ以外 → 🟡 停止、「N 分動きなし」を添える（ボスが押した停止と区別する）
  - Cursor のセッションにも同じ設定が効く
- テスト実行は、`Shell` ツールのコマンドが設定のパターンに一致したもの。成否は `ExitCode == 0`（Claude Code は、失敗のときだけ内容の `Exit code N` から取る。成功は 0。裏で動かしたコマンドは不明）
- コミット：Claude Code のログには head（hash）が無いので、App が `git rev-list -1 --before=<時刻> <ブランチ>` で、開始時（最初の `timestamp`）と最後の動きの時点の head を求め、`git log {開始}..{最後}` を出す（Cursor と同じく、同じブランチで並行して動いた別のセッションのコミットも混ざる）。ブランチ名が `HEAD`・`-` で始まる・`git check-ref-format --branch` に通らないときは不明（—）
- 作業ツリー（`<対象フォルダ>\.claude\worktrees\` 配下）のファイルは、本体の `git status` で個別に出ないので、未コミットの判定から外す。変更ファイルがすべて作業ツリーなら、完了チェックは「コミットの確認なし（作業ツリーの変更のため不明）」
- サブエージェント：起動中 = `SubagentStarted` − `SubagentStopped`（`Agent` ツールの呼び出しと結果）、累計 = `SubagentStarted` 件数。要約に履歴（説明・種類・開始・終了・経過）も持たせ、画面に「動いている（説明つき）／動いていた」を出す（12.8）

## 11. 導入前の過去セッション（条件付き）

- Step 0 で「transcript のファイル名 UUID = conversation_id」と「プロジェクト ↔ slug の対応規則」が確認できた場合のみ実装
- 読み込み元：`%USERPROFILE%\.cursor\projects\<slug>\agent-transcripts\` 配下の
  `<uuid>\<uuid>.jsonl` と旧形式 `<uuid>.jsonl` の両方。`subagents\` 配下は除外
- hook のイベントが存在する conversation_id はスキップ（重複防止）
- 状態は Imported、時刻はファイルの更新日時、タイムラインは入力と返事のみ（時刻なし）
- 確認できなかった場合は MVP から外す
- **実装（Phase 9）**：Step 0 で前提を確認できたので実装した（14.1 参照）。`CursorTranscriptImporter`（Core）が transcript を共通イベントにし、`ProjectEventStore` が起動後の最初の `Refresh` で1回だけ取り込む。
  - プロジェクトとの対応は slug（英数字以外の連なりを `-` にして、大文字小文字を無視して比較。パスのスラッシュ形式も可）。記号・日本語を含むパスの slug 規則は未確認。
  - 取り込むのは入力（`<user_query>` の中身）と、assistant の本文だけ。ツール呼び出しは対象外。時刻はすべてファイルの更新日時なので、タイムラインでは時刻を出さない。
  - 「最近の入力」には出さない（時刻が実際のものではないため）。「最近閉じたセッション」にも出ない（`sessionEnd` がない）。
  - 取り込んだ後に同じ conversation_id の hook イベントが現れたら（導入後に再開したなど）、hook のイベントに切り替える。化けて記録済みの本文の補正は未実装。
- **Issue #17 で変更（Hook が動かないときの代わり）**：取り込みは起動時の 1 回だけでなく、**transcript が変わるたび**に行う。
  - `agent-transcripts\` を監視し（9 章）、ポーリングのたびに、長さか更新日時が変わった transcript だけを読み直して、そのセッションを丸ごと置き換える（全件を読み直さない）。Cursor が書き込み中でも読めるように開く。最後の行が書きかけなら、その行は飛ばす（次の変化で読み直す）
  - hook のイベントがある conversation_id は、これまでどおり取り込まない（現れたら切り替える）
  - transcript の最後の更新が **Hook の登録より後**なら状態は `NoHook`（「Hook なし」）、前なら `Imported`（「閉じた（導入前）」）。Hook の登録の時刻は、hooks.json に Miharikun の登録があるときの「hooks.json の更新日時」と「`events\cursor\` の一番古いファイルの作成日時」の早いほう。登録が無ければ、すべて `Imported`
  - `NoHook` のセッションが 1 件以上あれば、一覧の上に警告を出す（12 章の 12.11）
  - transcript が消えても、取り込んだセッションは消さない（これまでどおり）

### 11.1 Claude Code の会話ログ（Issue #11）
Cursor の transcript の取り込み（上）とは違い、Claude Code の会話ログは**主なデータ源**（導入前の過去分ではなく、現在のセッションも読む）。時刻は実際の `timestamp` なので、`Imported` にはしない。

- 場所・形式：`%USERPROFILE%\.claude\projects\<フォルダ名>\<セッションID>.jsonl`（1 行 1 JSON、追記される）。CLI と Code タブは同じ形式・同じ場所（`entrypoint` が `claude-desktop` / `cli`）。バージョンは 2.1 系で確認
- 全行に `sessionId`・`cwd`・`timestamp`（UTC）・`gitBranch`・`uuid`・`parentUuid` がある（`attachment` などの一部を除く）
- 読み飛ばすもの：`attachment`（全体の約 17%。半分以上はトークン数の通知）、`queue-operation`、`file-history-*`、`last-prompt`、`agent-name`、`mode`、`permission-mode`、`cost-state`、`pr-link`、`relocated`、`worktree-state` など
- **形式は公開された仕様ではなく、バージョンで変わり得る**：知らない種類・壊れた行は飛ばし（壊れた行はログに残す）、`version` を記録する。形式が変わったことに気づけるよう、`app.log` に次を書く（**行の中身は書かない**。会話の本文が入るため）
  - 読めない行（JSON として壊れている、欄の型が違うなど）：ファイル名・行番号・記録の種類・`version`・例外の種類とメッセージ。同じ種類のエラーは 1 ファイルにつき最初の 1 回だけ書き、最後に件数をまとめる（ログが膨れないように）
  - 知らない種類の記録と、初めて見る `version`：起動ごとに 1 回ずつ、件数つきで
  - 画面には出さない（ログだけ）テストのフィクスチャは、実ログのコピーを使わず、構造を真似て手書きする（会話の全文が入っているため）
- サブエージェントの詳細は別ファイル `<セッションID>\subagents\agent-<id>.jsonl`（本体と同じ形式。`isSidechain = true`、`agentId` つき）と `agent-<id>.meta.json`（`agentType`・`description`・`toolUseId`）。**今回は読まない**（本体の `Agent` の呼び出しと結果だけで、「動いている／動いていた」を出す）
- 確認結果と未確認の項目は 14.2

## 12. 画面要件（ダッシュボードタブ）

ワイヤー：`miharikun-wire.html` を正とする。3ペイン構成（左 一覧 / 中央 詳細 / 右 メモ・タイムライン）。

### ワイヤーの読み方（重要）
ワイヤーは**レイアウトと表示項目を決めるための設計資料**であり、そのまま再現する画面ではない。以下は**実装しないこと**。
- 取得元バッジ（H / T / G / A / M の色付きラベル）と、その凡例（黄色の帯）
- グレーの小さい注記テキスト（「取得元：〜」「状態判定：〜」「※〜」などの説明文）
- 「⏸ あとで相談」枠と「不採用」の一覧
- サンプルデータ（タイトル・時刻・件数などはすべてダミー）
- 見た目（色・フォント・枠線）はラフ。最終的な見た目は WPF-UI の Fluent スタイルに合わせる

ワイヤーと本書が食い違う場合は**本書を優先**する。注記に書かれた算出ロジックは本書 10章・12章に記載済み。

### 12.1 ヘッダー
- タブ：ダッシュボード / ドキュメント（12.7）/ メモ（12.10）
- 対象フォルダパス
- ⚙ ボタンと対象フォルダパスは、**どのタブでも見える共通ヘッダー**に置く（タブの中に置かない）

### 12.2 左：セッション一覧
- 状態別件数（🔵🟢🟡🔴⚪。🟡 は「停止」）
- **ステータス絞り込み**：全て / 未設定 / 作業中 / 中断 / 完了 のチップ（件数つき）。「全て」以外は**複数選べる**（選んだ状態のどれかに合えば出す＝OR）。何も選ばなければ「全て」（既定）。「全て」を押すと他の選択を外す。件数は他のフィルタ・検索の前の全カードから数える。下のフィルタ・検索と AND
- 全文検索ボックス：対象は全プロンプト、概要、メモ、タイトル、変更ファイル名。インクリメンタル、大文字小文字無視
- フィルタ：実行中のみ / 未コミットあり / メモあり
- 並び順：最後の動きの降順
- カード表示項目：状態バッジ、タイトル（✏️でリネーム）、依頼数・最後の動き・モデル・圧縮回数・ブランチ、
  3行サマリー（12.4 と同じ）、概要の先頭、メモ（あれば）、**ステータスのバッジ**（未設定のときは出さない）
- カードクリックで選択 → 中央・右ペインを切り替え

### 12.3 中央：セッション詳細
1. **ヘッダー**：タイトル（✏️）、状態、ID 先頭8桁、開始時刻、ブランチ、モデル
   - **ステータス**：未設定 / 作業中 / 中断 / 完了 のボタン。クリックで即保存（meta の `status`）。選択中をもう一度押すと未設定に戻る。導入前のセッションも設定できる。**自動では変わらない**（完了にしたセッションに新しい依頼が来ても戻さない）。検索の対象にはしない（絞り込みはタブで行う）
2. **セッション概要**：本文、取り込み日時・元ターン番号、ボタン「💬 最後の返事を概要に取り込む」「✏️ 編集」「↩ 1つ前に戻す」
   - 取り込み：最後の `afterAgentResponse.text` を概要にする。既存の概要は `previousSummary` に退避（1件のみ保持）
   - 戻す：`previousSummary` と入れ替え
3. **いまの状況（3行サマリー）**：
   - 📝 最後に頼んだこと：最後の `beforeSubmitPrompt.prompt`
   - 🔧 最後にやってたこと：実行中ツールがあれば「{tool_name}: {要約}（実行中・経過秒）」、なければ最後の postToolUse
   - 💬 最後の返事：最後の `afterAgentResponse.text` の先頭2行
   - **各行クリックで右のタイムラインの該当イベントへスクロールしてハイライト**。該当種別がフィルタで非表示なら自動で表示に切り替える
4. **完了チェック**：✓/✗ ターン終了（最後が stop）、裏の作業なし（起動中サブエージェント0）、コミット済み（未コミット0）
5. **稼働状態（コストの目安）**：ターン数 / ツール呼び出し数、圧縮回数（直近%・auto/manual）、継続時間、サブエージェント起動中/累計、transcript サイズ（`transcript_path` があれば）。「金額・トークンではない」旨を注記
6. **成果**：コミット件数（展開で一覧）、変更ファイル件数（展開で一覧）、テスト実行回数（成功/失敗、展開でコマンド・所要時間・出力末尾）
7. **ターン一覧**：番号、開始時刻、状態（済み/停止/エラー/実行中）、依頼文先頭

### 12.4 右ペイン
1. **メモ**：TextBox（複数行）。フォーカスアウトで自動保存
2. **タイムライン**：フィルタ（入力 / 返事 / 思考 / ツール / 圧縮）。既定は入力・返事・圧縮が ON
   - 入力 = beforeSubmitPrompt、返事 = afterAgentResponse、思考 = afterAgentThought、
     ツール = postToolUse / postToolUseFailure（実行中は preToolUse）、圧縮 = preCompact、その他 = セッション開始・終了
   - 各項目に時刻。仮想化された ListBox を使用（長いセッション対策）
   - **検索**：種別のチップと同じ行に検索ボックス。入れた語を含む行だけを表示する（大文字小文字無視。省略表示の分も含む全文が対象。種別のチップとは AND）。件数「3/48」と、クリア（✕）を添える。語の強調や「次へ」ジャンプはしない。セッションを切り替えたらクリア、拡大⇄戻すでは保持
   - **行のコピー**：行にマウスを載せると「ダブルクリックでコピー」のバブルを出し、ダブルクリックでその行の全文（省略表示の分も含む）をクリップボードへコピーする。コピーしたらバブルが「コピーしました」に変わる（約1.5秒）。1回のクリック（全文の開閉）はダブルクリックでは働かせない
   - **全部コピー**：見出しの「拡大」ボタンの横に「全部コピー」。いま表示している行（種別・検索で絞った結果）を「時刻　入力/返事：本文」の形で連結してコピーする
   - 通常の幅（340px）でも、種別のチップ（5つ）と検索ボックスは1行に収める。収まらない幅のときだけ折り返す
3. **最近の入力**：このプロジェクトの全セッション横断で beforeSubmitPrompt の新しい順10件。行クリックで左のカードを選択
4. **最近閉じたセッション**：sessionEnd の新しい順5件。行クリックで左のカードを選択

### 12.4.1 タイムラインの拡大モード
- タイムライン見出しの「拡大」ボタンで、左（一覧）と中央（詳細）を隠し、右ペインを画面いっぱいに広げる。「戻す」ボタンまたは `Esc` で元に戻る。ヘッダー（タブ・対象フォルダ・⚙）は残す
- 広げた右ペインは、**ウィンドウの全幅**を使う（最大幅は設けない。検索などで該当する行が無くても、幅は変えない）。縦の並びは通常と同じで、**上にメモ、その下にタイムライン**（同じ幅）。これは、タイムラインを見ながら、中断の理由や続きの予定などをメモに書くため。「最近の入力」「最近閉じたセッション」は隠す
- 拡大中は、上部にセッション名と状態を出す。拡大中にセッションは切り替えない（戻して切り替える。前/次ボタンは作らない）
- 選択中のセッション、種別フィルタ、検索語、強調中の行、スクロール位置、メモの入力途中の文字は、拡大⇄戻すで保たれる。拡大の状態は保存しない（起動時は常に通常）
- 新しいイベントは通常と同じように追加され、末尾にいれば追従する

### 12.5 テーマ
- ⚙ メニューの「テーマ」から OS に合わせる / ライト / ダーク を選ぶ。選択は `settings.json` に保存し、OS 追従のときは実行中の OS 設定変更にも従う
- 色はテーマ別の色定義（`Themes\Colors.Light.xaml` / `Colors.Dark.xaml`）に置き、画面側は DynamicResource で参照する（直書きしない）

### 12.6 イベントの一意 ID
- App 側で「SessionKey + ファイル内の行番号」を ID とする（AgentEvent.Seq）（3行サマリー → タイムラインのジャンプに使用）

### 12.7 ドキュメントタブ（Issue #9）
対象フォルダ配下の .md / .html / .htm をツリー → 一覧 → プレビューで読む。読み取り専用（編集・保存はしない）。設計イメージ：`docs/issue9/issue9-documents-design.html`（リポジトリ外）。

**画面（左から）**
- 上部：「読み直し」ボタン（全再走査）、「パスで絞り込み」ボックス（ファイル名・フォルダ名の部分一致、大文字小文字無視、インクリメンタル）。「一覧/ボード」切替は作らない
- ツリー：先頭に「すべて」。md/html を1件以上含むフォルダだけを出し、件数は**子孫を含む**数。絞り込み中は件数・表示をヒット分に変える
- 一覧：選んだフォルダの**直下のファイルだけ**（「すべて」は全件）。見出しに「{フォルダ}/ の直下」、行はファイル名＋所属フォルダ。並びは更新日の降順
- 右：「ドキュメント概要」カード（タイトル・相対パス・更新日時・作成日時・行数・サイズ。タイトルは md＝最初の見出し、html＝`<title>`、無ければファイル名）と、「内容」カード（プレビュー）。内容カードに「再読み込み」「フォルダで開く」「既定のアプリで開く」
- 最後に開いたファイルを覚え（プロジェクトごと）、次回起動時に復元する

**除外設定（プロジェクトごと）**
- ヘッダー右上の ⚙ メニューに「ドキュメントの設定…」。ダイアログに gitignore 形式のテキストボックスと「既定に戻す」。保存は 6章のプロジェクト設定
- 書式は gitignore のうち次を扱う：`#` コメント、空行、`!` 再包含、末尾 `/`＝フォルダのみ、先頭または途中の `/`＝対象フォルダ直下基準、`*` `?` `**`、それ以外は任意の深さの名前に一致。大文字小文字は区別しない（Windows）。後ろの行が優先。git と同じく、除外したフォルダの中身を `!` で戻すことはできない
- 既定値：`.git/` `node_modules/` `bin/` `obj/` `.vs/` `.idea/` `.venv/` `venv/` `__pycache__/` `dist/` `build/` `out/` `target/` `packages/` `.gradle/` `.next/` `coverage/`。`.cursor/` は除外しない
- 保存したら即座に再走査する

**走査**
- 背景スレッドで再帰列挙し、除外フォルダは中に入らない（枝刈り）。拡張子 `.md` `.html` `.htm`（大文字小文字無視）のみ拾い、中身は読まない。見つかった分から順にツリーへ出す（画面を固めない）
- 変更検知は `FileSystemWatcher`：ファイルの追加/削除/更新/名前変更だけ差分で反映し、フォルダの作成/削除/名前変更・バッファあふれは「読み直し」と同じ全再走査を自動で行う。除外は親フォルダまでさかのぼって判定する。取りこぼし用に「読み直し」がある。索引のキャッシュ保存はしない（遅ければ後で足す）
- 目標：1万ファイル超のフォルダでも、操作がもたつかず、ツリーが数秒以内に出る。走査にかかった時間は、上段の件数の横に「（走査 N 秒）」と出し、`logs\app.log` にも残す（実環境での実測に使う）
- 検索はファイル名のみ（本文検索はしない）

**プレビュー**
- WebView2（Edge の Runtime）を使う。未導入のときは「WebView2 Runtime が必要です」と案内を出し、ツリー・一覧・概要カードは使える。起動時に Runtime の有無を確認する
- html：元のファイルをそのまま開く（`file:///`）。相対・絶対のローカルパスの css・js・画像・リンクはそのまま効く。JavaScript は実行する。外部 http のリンクは既定ブラウザで開く
- **リンクの振り分け**（html・md 共通。`NavigationStarting` と `NewWindowRequested` の両方）：①http/https → 既定ブラウザ ②対象フォルダ内の .md/.html/.htm → アプリ内で選択（ツリー・一覧・最後のファイルも追従。html→html も同じ）③対象フォルダ外・除外フォルダ内の .md/.html → 既定のアプリ ④その他のローカルファイル・フォルダ → 既定のアプリ／エクスプローラー ⑤`#見出し` だけ → ページ内で移動
- md：Markdig（`UseAdvancedExtensions`）で HTML にし、`<base>` を元のフォルダにした一時 HTML を開く（見出しの id は日本語を残す GitHub 方式。`#見出し` のリンクはページ内スクロールにする）。相対パスの画像・リンクが効く。他の .md へのリンクはアプリ内で開き、ツリー・一覧も追従する
- md の**タスクリスト**（`- [ ]` `- [x]` `- [X]`、入れ子、番号付きリスト内）は、チェックボックスとして表示する。読み取り専用（クリックしても md は変わらない）。黒丸は消す。コードブロック内と見出し内の `[x]` は変換しない
- md の mermaid（`mermaid` 言語のコードブロック）は、CDN の mermaid.js で図にする（ネット接続が前提。繋がらないときはコードのまま表示）。コードブロックは highlight.js（CDN。繋がらないときは色なし）で色付けする
- md のプレビューはライト/ダークのテーマに追従する（html はファイルの見た目を尊重）
- ファイルが保存されたら自動で再読み込みする
- WebView2 の上に WPF の要素を重ねない（Runtime 未導入の案内などは、プレビュー領域と入れ替えて出す）。設定ダイアログは別ウィンドウにする

### 12.8 エージェントの表示（Issue #11）
- 一覧のカードと詳細のヘッダーに、エージェントの**バッジ**（Cursor / Claude）を出す（表示名は `IAgentInfo.DisplayName`）
- 左ペインに**エージェントの絞り込み**（全て / Cursor / Claude。1 つだけ選ぶ。既定は「全て」）。ステータスの絞り込み・検索・チップとは AND。件数は他のフィルタの前の全カードから数える（ステータスのタブと同じ流儀）
- `Capabilities` に無い項目は出さない（Claude Code の「閉じた」状態、最近閉じたセッションへの出現、など）
- 「停止」の表示：10.1 の `StalledRule.DisplayState` を、画面の 1 秒ごとの更新で適用する。**状態の丸・文字・「実行中のみ」の絞り込み・状態ごとの件数・詳細ヘッダー・拡大時のタイトルは、すべてこの表示用の状態で出す**（食い違わないように）。「N 分動きなし」を添える。要約は変えない
- タイムラインへのジャンプ（3 行サマリー・最近の入力）は、Seq と種類が両方一致する行を優先する（Claude Code は 1 行から複数のイベントが出て、同じ Seq になることがある）
- サブエージェント：詳細の稼働状態に「動いている N（説明）／ 動いた M」。完了チェックの「裏の作業なし」も、これで判定（これまでどおり）
- ドキュメントタブ・メモ・ステータスは、エージェントに関係なくそのまま使える（メタは `meta\{agentId}\{sessionId}.json`）

### 12.9 設定画面（Issue #11）
- ⚙ メニューに「設定…」を足し、アプリ全体の設定のダイアログを開く（プロジェクトごとの「ドキュメントの設定…」とは別）
- 項目は「実行中のまま動きなし → 停止とみなす時間（分）」（`runningTimeoutMinutes`。0 で無効）だけから始める。今後のアプリ全体の設定（テストコマンドのパターンなど）は、この画面に足す
- 保存は `settings.json`（6 章。他のキーを残す）。保存するとすぐ効く（再起動は要らない）。数字以外・負の数は、保存できないようにする
- テーマは、これまでどおり ⚙ メニューの「テーマ」で選ぶ（設定画面には移さない）
- **Issue #17**：「Cursor の Hook exe の置き場所」（フォルダ。`hookDir`）を足す。入力欄＋「参照…」（フォルダ選択）＋「既定に戻す」。存在しないフォルダは保存できない（作るかは聞かない）。保存して場所が変わったら、その場所に導入し直すかを聞く（8.2）

### 12.10 メモタブ（プロジェクトメモ）
プロジェクトのメモ帳。Markdown で書き、ふだんはドキュメントタブの md と同じ見た目で読む。設計イメージ：`docs/memo-tab/memo-tab-design.html`。

**画面**
- タブの全幅を 1 枚で使う。上に細いバー（見出し「プロジェクトのメモ」、最終更新の日時、ボタン）、その下をプレビューかエディタのどちらかが占める
- メモは **1 プロジェクトに 1 つ**。保存先は 6 章（`projects\{slug}-{hash8}.memo.md`）
- 最終更新の日時は、メモのファイルの更新日時。ファイルが無ければ出さない

**プレビュー（既定）**
- タブを開いたとき・起動時はプレビュー。バーの右に「✎ 編集」
- 表示は 12.7 の md のプレビューと同じ（Markdig・タスクリストは読み取り専用・mermaid・コードの色付け・ライト/ダークに追従）。**相対パスの基準は対象フォルダ**（`docs/xxx.md` や画像がそのまま効く）
- 空（ファイルが無い・空白だけ）のときは「メモはまだありません。右上の『編集』で書けます」と出す
- リンク：①http/https → 既定ブラウザ ②対象フォルダ内の .md/.html/.htm（除外フォルダ内を除く）→ **ドキュメントタブに切り替えて、そのファイルを選ぶ** ③それ以外のローカルファイル・フォルダ → 既定のアプリ／エクスプローラー ④`#見出し` → ページ内で移動
- メモのファイルが外で書き換わったら（別の起動のアプリなど）、プレビュー中なら自動で読み直す
- WebView2 Runtime が無いときは、プレビューの場所に 12.7 と同じ案内を出す。編集・保存は使える

**編集**
- 「✎ 編集」で、いまの内容を入れた複数行の入力欄（`TextBox`、`AcceptsReturn`・折り返し・縦スクロール・等幅フォント）に切り替える。バーの右は「保存」「キャンセル」に替わり、見出しの横に「編集中」（変更があれば「編集中（未保存）」）
- エディタとプレビューは重ねない（Visibility で入れ替える。WebView2 の上に WPF の要素を重ねない：12.7）
- **保存**（ボタン・Ctrl+S）：ファイルに書いて（一時ファイル → 置き換え。複数起動の競合は後勝ち）、プレビューに戻り、描き直す。書けなかったときは理由を出して、編集のまま（入力を失わない）
- **キャンセル**：変更が無ければそのままプレビューへ。変更があれば「変更を破棄しますか？」と確かめ、破棄ならプレビューへ戻る（内容は保存済みのもの）。Esc でキャンセルはしない
- 編集中にほかのタブへ移っても、入力はそのまま残る（戻れば続きから）
- 編集中は、外での書き換えを取り込まない（入力を上書きしない）
- **未保存のままアプリを閉じる**とき：「プロジェクトのメモが保存されていません。保存しますか？」［保存］［保存しない］［キャンセル］。キャンセルなら閉じない。保存に失敗したら閉じない
- 編集の補助（色付け・補完・太字ボタンなど）、左右同時のプレビュー、履歴は作らない

**そのほか**
- ダッシュボードの全文検索の対象にはしない（検索はセッションを探すためのもの）

### 12.11 Hook が記録していないときの警告（Issue #17）
- 状態が `NoHook` のセッションが 1 件以上あるとき、左の一覧の上（状態の件数の上）に黄色の帯を出す：「⚠ Cursor の Hook が記録していません（Hook なし N 件）。この PC で Hook exe の実行が止められている可能性があります。⚙ →「設定…」で置き場所を変えてください。」と、ボタン「hook-error.log を開く」（ファイルが無ければ押せない）
- 帯は閉じられない（原因が直り、`NoHook` が 0 件になれば消える）
- `NoHook` のセッションの詳細は、`Imported` と同じく、時刻・ターン・ツール・git の情報を「—」にする。チェック欄の文は「Hook の記録が無いセッションなので不明」

## 13. 非機能要件

- 1プロジェクト100セッション・1セッション5,000イベントで、起動3秒以内・操作がもたつかないこと
- Hook の処理が Cursor の操作感を損なわないこと（7章の性能要件）
- 文字コードはすべて UTF-8（BOM なし）
- 例外で落ちないこと：壊れた行はスキップしてログに残す。読み込み元（`ISessionSource`）ごとに例外を捕まえ、スタックつきでログに残して、他の読み込み元は動き続ける（Claude Code の読み込みが壊れても Cursor の表示は止めない）
- それでも捕まえられなかった例外（UI スレッド・背景スレッド・Task）は、`app.log` にスタックつきで残す（落ちた原因を調べられるように）
- イベントファイルの自動削除は MVP ではしない

## 14. Step 0：実機検証

ダンプ用 hook（`step0-dump-hook/`）を入れて普段どおり作業し、以下を確認して本書に追記する。
本体の hook を実際の Cursor に入れた PoC でも確認を進めており、結果は 14.1 に書く（`[x]` は確認済み、`[ ]` は未確認）。

- [x] Windows で hooks.json の command が実行されるか。`~/.cursor` が `%USERPROFILE%\.cursor` か（フルパス・`/` 区切り・スペースなしで確認。**スペースを含むパスは未確認**）
- [ ] `sessionEnd` の発火条件（タブを閉じる／別チャットに切り替える／ウィンドウを閉じる）と reason の値
- [ ] 閉じたチャットを履歴から再開したとき `sessionStart` が再度来るか（来ていない可能性が高い。**要再確認**）
- [x] transcript のファイル名 UUID と conversation_id が一致するか、Windows での slug の付け方
- [x] `transcript_path` が既定で null にならないか（**null になることがある**。`sessionStart` と最初の `beforeSubmitPrompt`）
- [ ] モデルが Auto のときの `model` / `model_id` の値（**未確認**）
- [x] Ask モードで hook が発火するか
- [ ] 承認系 hook を exit 1 で抜けたとき：アクションが通常どおり進むか（Read / Grep / Glob は進んだ。**Shell の確認ダイアログは未確認**）、Cursor の確認ダイアログが維持されるか、Hooks 出力チャンネルのエラー表示が許容範囲か
- [ ] `postToolUse`（Shell）の `tool_output` に `exitCode` が入るか（**未確認**）。`tool_use_id` が pre/post で一致するかは確認済み

### 14.1 実機で分かったこと（Cursor 3.23.12 / Windows、PoC 中の実測）

確認項目の一部が、PoC（本体の hook を実際の Cursor に入れて動かした結果）で分かった。残りは未確認のまま。

- [x] **hooks.json の command は Windows で実行される**。`C:/Users/…/Miharikun.Hook.exe --agent cursor`（`/` 区切り・スペースなし）で動作。`~/.cursor` は `%USERPROFILE%\.cursor`。スペースを含むパスは未確認。
- [x] **transcript の UUID = conversation_id**。`%USERPROFILE%\.cursor\projects\<slug>\agent-transcripts\<uuid>\<uuid>.jsonl`（`subagents\` あり）。**slug は、パスの英数字以外の連なり（`:\` や `\`）を 1 つの `-` にしたもの**（`C:\zDev\repo\Miharikun` → `c-zDev-repo-Miharikun`）。ドライブ文字の大小は一定ではない（同じ `projects\` に `C-Users-…-Temp-…` もある）ので、比較では大文字小文字を無視する。記号・日本語を含むパスの規則は未確認。→ Phase 9 の前提は満たす。
  - 形式：1行1メッセージ `{"role":"user|assistant","message":{"content":[{"type":"text","text":…}|{"type":"tool_use",…}]}}`。user の本文は `<timestamp>…</timestamp><user_query>…</user_query>` で包まれる。**行ごとの時刻はない**（user メッセージ内の `<timestamp>` のみ）。
- [x] **`transcript_path` は常には入らない**。`sessionStart` と最初の `beforeSubmitPrompt` では空で、ツール系・`stop` 以降で入る（`c:\Users\…\agent-transcripts\<uuid>\<uuid>.jsonl`）。
- [x] **モデル**：Agent の `beforeSubmitPrompt` / `stop` は `model:"grok-4.7-high"`、`model_id:"grok-4.7"`、`model_params:[{id,value},…]`（context / reasoning_effort / fast）。**ツール系など多くのイベントは `model:"default"`**（`model_id` なし）→ 表示では `default` を無視する。`sessionStart` は別の値（`cursor-grok-4.6-medium`）。
- [x] **承認系 hook を無出力・exit 1 で抜けても、`Read` / `Grep` / `Glob` の実行は通常どおり進んだ**。Shell の確認ダイアログへの影響は未確認。
- [x] **`tool_use_id` は pre / post で一致**（実測 6/6）。値に改行を含む（`call-…\nfc_…`）。
- [x] **`tool_output` は文字列**（JSON を文字列にしたもの。Read は `{"file_path":…,"content_length":…}`）。`duration` はミリ秒の小数。Shell の `exitCode` は未確認。
- [x] **`workspace_roots` は `/c:/zDev/repo/Miharikun` の形**（先頭に `/`、ドライブ文字は小文字）。→ `ProjectPath` で先頭の `/` を取り除いて比較する。Hook の `git -C` にも同じ処理が必要だった。
- [x] **日本語環境の Windows では、Cursor が渡す入力の日本語が壊れる（Cursor 側の不具合。原因と回避策を特定）**。
  - 症状：`内容教えて` → `蜀・ｮｹ謨吶∴縺ｦ`（UTF-8 のバイト列を CP932 として読み替えた文字列。元には戻せない）。さらに文字列中の `\` `"` が失われて JSON として読めなくなることがある（長い日本語を含む `afterAgentResponse` / `afterAgentThought` など）。入力の先頭には UTF-8 の BOM が付く。
  - 原因：Cursor は Windows で hook を次のように起動する（Cursor 内部のログには正しい入力が出ている）。
    `$OutputEncoding = [System.Text.Encoding]::UTF8; Get-Content -LiteralPath '%TEMP%\cursor-hook-payload-….json' -Raw | & { $input | <command> }`
    一時ファイルは BOM なしの UTF-8 だが、Windows PowerShell 5.1 の `Get-Content` は `-Encoding` なしだと既定のコードページ（日本語環境では CP932）で読む。`pwsh`（PowerShell 7）が PATH にあればそちらが使われ、起きないはず（未確認）。
  - **対応（実装済み）**：Hook は、入力が BOM 付きで非 ASCII を含むとき（または JSON として読めないとき）、元の一時ファイル `cursor-hook-payload-*.json`（hook の実行中だけ存在）を探して正しい内容を読み直す。取り違えないよう、「受け取った文字列 = その一時ファイルを CP932 で読んだもの」と一致する候補だけを採用し、一致を確かめられないときは、イベント名・会話 ID・生成 ID が一意に合うものだけを採用する。見つからなければ従来どおり、イベント名・セッション ID などの ASCII の項目だけを救出して記録する（`_salvaged: true`、元のバイト列は `logs\bad-input\`）。
  - 回避策（コード変更なし）：PowerShell 7 を入れる、または Windows の「ベータ: ワールドワイド言語サポートで Unicode UTF-8 を使用」を有効にする。
  - 修正前に記録されたイベント（化けた本文・本文なしのイベント）は元に戻らない。transcript（正しい日本語で保存されている）から補正する案は未実装。
- [x] **Ask モードでも hook は発火する**。`beforeSubmitPrompt` の `composer_mode` が Agent では `"agent"`、Ask では `"chat"`（`model` は `cursor-grok-4.6-medium`）。
- [ ] **履歴から再開したときの `sessionStart`**：利用者の操作ではタイムラインに出たが、イベントファイルには再開後の 2 回目の `sessionStart` は記録されていない（1 会話につき 1 行のみ）。再開で再度は来ない可能性が高い。**要再確認**。
- [ ] `sessionEnd` の発火条件と reason ／ Shell の `exitCode` と失敗時のイベント ／ Shell の確認ダイアログへの影響 ／ スペースを含むパス ／ モデルが Auto のときの値：**未確認**
- 補足：全イベントの payload に `user_email` が入っている（イベントファイルにそのまま保存される）。

### 14.2 Claude Code の Step 0（会話ログの確認。Issue #11）
完了済みのセッション 27 本（約 100MB、16,839 行。他のプロジェクトを含む）の**構造だけ**を数えて確認した（本文は読んでいない。進行中のセッションも読んでいない）。詳細は Issue #11 のコメント。

- [x] **会話ログだけで足りる**（フックは不要）：依頼・返答・思考・ツール実行と結果・ファイル編集・ターンの終わり・中断・エラー・質問待ち・サブエージェントの呼び出しと結果が取れる
- [x] **CLI と Code タブは同じ形式・同じ場所**（`entrypoint` = `cli` / `claude-desktop`）
- [x] 場所：`%USERPROFILE%\.claude\projects\<フォルダ名>\<セッションID>.jsonl`。ファイルは追記される。フォルダ名は `C:\zDev\repo\Miharikun` → `C--zDev-repo-Miharikun`
- [x] 全行に `sessionId`・`cwd`・`timestamp`・`gitBranch`。**プロジェクトの判定は `cwd` でできる**
- [x] ツール結果に**終了コードの欄は無い**。失敗は `is_error: true` で、内容が `Exit code N …` で始まる（37 件を確認）。成功は `is_error: false`
- [x] Shell は `Bash` と `PowerShell`（`input.command`）
- [x] サブエージェントは**別ファイル**（`<セッションID>\subagents\agent-<id>.jsonl` と `.meta.json`）。本体には `Agent` ツールの呼び出し（84 件）と、結果の `agentId`（84 件）。`isSidechain` は本体に 0 件
- [x] 作業ツリー（worktree）のセッションは別フォルダ（`...--claude-worktrees-...`）。`cwd` に `.claude\worktrees` を含む（621 行）
- [x] 中断：`[Request interrupted…]`（10 件）。エラー：`system` の `api_error`（8 件）・`isApiErrorMessage`（3 件）・`error`（11 件）
- [x] ユーザーの返事待ち：`AskUserQuestion`（26 件）
- [x] ターンの終わり：`stop_reason: end_turn`（563 件）。`stop_hook_summary`（368 件）・`turn_duration`（20 件）は補助
- [x] **実装計画のレビューで追加確認**（28 本・18,787 行。構造だけ。進行中のセッションは除く）：
  - `end_turn` は、返答 449 件のうち 172 件で、思考の行と本文の行の両方に付く。本文の行は全件に 1 つ → 本文の行で TurnEnded を 1 回（5.1）
  - モデル名 `<synthetic>`（合成の返答）が 5 件
  - 人の入力：`origin.kind=human` 417 件。古い版（2.1.156）は `origin` 欄が無い（人の入力 16 件）。`origin.kind=task-notification` が 27 件（`<tool-use-id>`・`<status>`・`<summary>` のタグつき。今回はすべて SendMessage のもの）。`[Request interrupted…]` は `origin` 無し
  - `Exit code N` の `is_error`：PowerShell 48 件・Bash 20 件
  - 裏で動かした Bash 1 件（`is_error=false`、内容「Command running in background…」、`toolUseResult.backgroundTaskId` あり）。Agent の結果は 84 件すべて `toolUseResult.status=completed`（裏で動かした Agent は 0 件）
  - `git commit` を含むコマンド 105 件のうち、出力に `[ブランチ sha]` の行があるのは 30 件だけ（`-q` など）→ コミットは出力から拾わず、ブランチと時刻から求める（10.1）
  - `ExitPlanMode` は 0 件
- [ ] **会話の圧縮（コンパクト）の記録**：27 本のどれにも、Phase 22 の 9 本（2.1.286）にも無かった。**保留**（出たら確認。出ても読み飛ばして止まらない）
- [ ] 裏で動かした Agent の結果（`status`）と、終わりの通知（`task-notification`）の形：Phase 22 の 9 本には `Agent` の呼び出し自体が 0 件。**保留**（手書きのテストで、`completed` 以外は動いているまま・通知で終わり・`is_error` も終わり、を確認済み）
- [ ] `ExitPlanMode` の結果の形：Phase 22 の 9 本にも 0 件。**保留**（手書きのテストだけ。`AskUserQuestion` の結果は実物で確認：`toolUseResult` に `questions` と `answers`）
- [ ] セッションの再開（`relocated` 51 件の意味）：Phase 22 の 9 本には 0 件。**保留**
- [ ] 承認待ち：記録が見当たらなかった（もともと対象外）
- [ ] `system` の `api_error` と再試行の関係（エラーの判定に使えるか）：Phase 22 の 9 本には `api_error` も `isApiErrorMessage` も 0 件。**保留**
- [ ] スラッシュコマンド（`<command-name>`）・`<local-command…>` を依頼としてどう表示するか：Phase 22 の 9 本には 0 件。**保留**（`isMeta` の行は出さず、それ以外は文字のまま出す）

- [x] **Phase 22-1 の実機確認（Claude Code 2.1.286。このプロジェクトの完了済みセッション 9 本・7,651 行・47.9MB。構造だけ数えた。進行中のセッションは除く）**：
  - **フォルダ名の規則どおり**：本体の `C--zDev-repo-Miharikun`（10 本の会話ログ）。作業ツリーのフォルダは無かった。セッションごとのサブフォルダには `tool-results`・`.json`・`.md` があり、`subagents` は無い（読まない）
  - **最初の記録は全 9 本とも `queue-operation` で、`cwd` も `gitBranch` も無い**。`cwd` は 2 行目以降に出る → 「最初の `cwd` が見つかるまで行を取っておく」作りが実際に必要だった。ブランチの取り方は 5.1 の表の行のとおりに直した（直す前は全セッションでブランチが取れず、コミット一覧が出なかった）
  - 新しい種類の記録 `atis-latch`（441 件。`last-prompt`・`agent-name` と同じ数）：会話の内容ではないので読み飛ばす（既知の種類に加えた）。`file-history-delta` は `file-history-*` として読み飛ばす
  - 人の入力：`origin.kind = human` が 156 件（人の入力の行だけに付く。ツールの結果には `origin` が無い）。`isMeta` が 13 件、割り込みが 5 件（`[Request interrupted…]` 4 件と `…for tool use` 1 件）。`array:image+text`（画像つきの入力）が 9 件 → 本文だけ読む。`origin` が無い・`tool_result` でもない・`isMeta` でもない文字の行は 0 件（古い版の形は、この 9 本には無い）
  - **`end_turn`**：返答 151 件に、本文つきの `end_turn` の行がちょうど 1 つずつ。ほかに思考だけの `end_turn` の行が 38 件。人の入力 156 件 = `end_turn` 151 件 + 割り込み 5 件（5.1 の「本文の行で 1 回」の規則どおり）
  - `AskUserQuestion` 3 件：結果は `is_error` でなく、`toolUseResult` に `questions` と `answers`（回答は依頼として数える）。裏で動かした Bash の結果（`backgroundTaskId`）が 1 件
  - ツールの結果に終了コードの欄は無い（`Exit code N` で判定する方式のまま）。`Bash` 394・`PowerShell` 273・`Edit` 171・`Read` 130・`Write` 112 ほか、`mcp__…` のツール
  - 表示（隔離環境・本物のコピー）：9 本とも「ボスの番」。人の入力 156 件 + 回答 3 件 = 依頼数 159 件と一致。イベント 3,916 件。テスト実行は最多のセッションで 44 回（成功 31 / 失敗 13）。コミット一覧は 0〜9 件、**開始より前にコミットが無いセッション（途中で最初のコミットができた）は「—」になっていた** → その場合は最後までの全部のコミットを出す（`GetCommitsUpTo`）ように直した
  - **初回の読み込み：9 本・47.9MB で 189〜316ms、変化なしのポーリング 0.7ms**（「直近 N 日」の設定は要らない）。`app.log` に出たのは、初めて見る version（2.1.286）の 1 行だけ。読めない行は 0
  - 手書きのダミーで確かめた形（実物と食い違いは無かった）：`message.content` の文字／配列、`tool_result`（文字／text の配列）、`is_error`、`stop_reason`、`message.id`、`toolUseResult`
  - 出なかったもの（上の 14.2 の未確認項目を **保留**）：圧縮、`relocated`、`api_error`・`isApiErrorMessage`、`Agent` の呼び出し・`task-notification`、`ExitPlanMode`、スラッシュコマンド（`<command-name>`）

## 15. 実装フェーズ

フェーズ完了時は見出しの `[ ]` を `[x]` に更新すること。

## [x] Phase 1: ソリューション雛形
- 4プロジェクト作成、5.1 の `IAgent`・`AgentEvent`・`AgentCapabilities` を Core に定義、WPF-UI と CommunityToolkit.Mvvm 導入、FluentWindow で3ペインの空画面
- 完了条件：ビルドが通り、空の3ペイン画面が表示される

## [x] Phase 2: Hook exe
- 7章の仕様どおり実装（NativeAOT）。`--agent` 引数、`CursorAgent` の Hook 側メソッド、イベント行モデルとシリアライザー（ソース生成）
- 完了条件：標準入力にサンプル JSON を流すと JSONL が追記され、出力・終了コードが表どおり

## [x] Phase 3: イベント読み込みと状態判定（Core）
- tail 読み込み、`CursorAgent.Normalize`（5.1 対応表）、プロジェクト一致判定、10章の状態判定・派生値（共通イベントのみ使用）
- 完了条件：サンプル JSONL を使った単体テストで状態・派生値が期待どおり

## [x] Phase 4: 一覧とファイル監視
- 左ペイン（件数、カード、検索、フィルタ、並び順）、FileSystemWatcher＋ポーリング
- 完了条件：hook が書いたイベントが数秒以内に一覧へ反映される

## [x] Phase 5: 詳細とタイムライン
- 中央ペイン全セクション、右ペインのタイムライン、3行サマリー → タイムラインのジャンプ
- 完了条件：12.3・12.4 の項目がすべて表示され、ジャンプとフィルタ自動切替が動く

## [x] Phase 6: メタ（タイトル・概要・メモ）
- リネーム、概要取り込み／編集／1つ前に戻す、メモ自動保存、検索対象への反映
- 完了条件：再起動後も内容が保持され、検索でヒットする

## [x] Phase 7: git 連携
- コミット一覧、未コミット判定、ブランチ表示
- 完了条件：git リポジトリでない場合もエラーにならない

## [x] Phase 8: hook 導入機能と配布
- 8章の導入／削除、バックアップ、マージ。publish 設定と zip 作成スクリプト
- 完了条件：まっさらな環境で zip 展開 → 起動 → 導入 → Cursor で会話するとダッシュボードに出る
- 状況：導入／削除・バックアップ・マージ・起動時ダイアログ・`scripts/publish.ps1` は実装し、単一ファイルの App と導入の流れは実機で確認済み。
  Hook の NativeAOT 発行は、VS 2026 の「C++ によるデスクトップ開発」を入れた環境で通り、zip（約 58 MB：App 約 132 MB、Hook 約 3.9 MB）を作れた。
  Hook の 1 回の実行は、git なしで中央値 17ms（何もしない exe は 15ms）、git ありで 51ms 前後。要件（git なし 50ms 以内・git あり 3 秒以内）を満たす（通常ビルドの PoC 版は 200〜380ms）。
  - `publish.ps1` は `vswhere.exe` の場所（VS Installer）を PATH に足す。ILCompiler がリンカーを探すときに呼ぶが、既定では PATH に無く、無いと「Platform linker not found」ではなく link.exe の起動で失敗するため。
  - 未確認：AOT 版の Hook を実際の Cursor に入れた動作（日本語の文字化け対策を含む）。リリース用 zip での通し確認（展開 → 起動 → 導入 → 会話）は、配布前に利用者が行う。

## [x] Phase 9: 過去セッション取り込み（Step 0 の結果次第）
- 11章。条件を満たさない場合はスキップして理由を本書に追記
- 完了条件：hook を入れる前のセッションが「⚪ 閉じた（導入前）」として一覧に出て、詳細・検索・リネーム・メモが使える。hook のイベントがあるセッションは重複しない
- 状況：実装し、実際の `.cursor` の transcript で画面確認済み（仕様と制限は 11章の「実装」）。
  導入前のセッションの詳細は、取れない情報（ターン・ツール・継続時間・完了チェック・コミット・変更ファイル・テスト）を「—」で出し、依頼数と transcript サイズだけを見せる。

## [x] Phase 10: 最近の入力・最近閉じたセッション、タブのプレースホルダー
- 右ペイン下部2リスト（クリックでカード選択）、ドキュメント／メモタブの「MVP 対象外」表示

## [x] Phase 11: ステータス（Issue #2）
- 12.2 / 12.3 / 6章。`SessionStatus` と meta の保存、一覧のバッジ・絞り込みタブ・件数、詳細のステータス設定。自動判定の「中断」の呼び名を「停止」に変更
- 完了条件：詳細でステータスを設定すると再起動後も残り、一覧のバッジと絞り込みタブ（件数）に反映される。既存の meta ファイルがそのまま読める。ライト/ダークの両方で表示を確認する
- 進め方：①要件定義 ②Core（保存。テスト先行）③ViewModel（絞り込みの組み合わせ。テスト先行）④画面
- 状況：実装し、隔離した環境（実データのコピー）でライト/ダークの両方を画面確認済み（ボタンで設定 → meta に保存 → カードのバッジと絞り込みタブの件数に反映、同じボタンをもう一度押すと未設定に戻る）。判定ロジック（絞り込み・件数）は Core の `StatusFilter` に置いてテストしている

## [x] Phase 12: タイムラインの拡大モード・行のコピー・検索（Issue #1）
- 12.4 / 12.4.1。拡大モード（Esc で戻す）、タイムライン内の検索（チップと同じ行）、行のダブルクリックコピー（バブル）、全部コピー
- 完了条件：拡大→戻すでスクロール位置・フィルタ・検索語・メモの入力途中の文字が保たれる。通常の幅で、チップと検索が1行に収まる。コピーした内容が全文（省略表示でない）である。ライト/ダークの両方で表示を確認する
- 進め方：①要件定義 ②Core（検索の判定・全部コピーの整形。テスト先行）③ViewModel・画面（拡大モード、検索、コピー）
- 状況：実装し、隔離した環境（実データのコピー）でライト/ダークの画面確認済み。通常の幅（340px）でチップ5つと検索ボックスが1行に収まること、検索（「1/5件」・✕で解除）、行のダブルクリックコピー（全文がクリップボードに入り、バブルが「コピーしました」に変わる。省略表示の開閉は元のまま）、拡大（全幅で上にメモ、下にタイムライン。Esc で戻る）を確認した。判定・整形は Core の `TimelineText` に置いてテストしている。

## Phase 13〜17: ドキュメントタブ（Issue #9）
12.7 / 6章。実装の分け方・ファイル構成は `docs/issue9/issue9-documents-plan.md`（図解 HTML と対）。全体の完了条件：1万ファイル超のフォルダでも画面が固まらずツリーが出る。除外設定の変更が即時に反映され、再起動後も残る。md のチェックボックス（未/済・入れ子・番号付き）が正しく出る。mermaid が図になる。html の相対 css・js・画像が効く。ライト/ダークの両方で確認する。

## [x] Phase 13: ドキュメント Core
- `GitIgnoreMatcher`・`DocumentIndexer`・索引・ツリー・`DocumentFilter`・`DocumentOverview`・`ProjectSettingsStore`・`DocumentWatcher`（画面なし。テスト先行）
- 完了条件：1万ファイルの一時フォルダのテストが数秒以内。`dotnet build --no-incremental` が警告 0
- 状況：実装済み（`src/Miharikun.Core/Documents/`、`Settings/ProjectSettingsStore.cs`）。除外判定・走査・索引・ツリー・絞り込み・概要・設定の保存・Watcher の振り分け（`DocumentChangeClassifier`）・再走査の予約（`RescanScheduler`）まで。1万ファイル（除外対象 2,000 を含む）の計測は `MIHARIKUN_PERF=1` のときだけ動く `PerfFact`。Core は AOT 互換（`--no-incremental` で警告 0）

## [x] Phase 14: ドキュメント md→HTML（新プロジェクト Miharikun.Docs）
- Markdig（Docs だけが参照。Core は AOT 互換のまま）。チェックボックス・mermaid・テーマ別 CSS
- 完了条件：チェックボックス 6 ケースのテストが通る。要件定義書そのものを変換できる
- 状況：実装済み（`src/Miharikun.Docs/`）。チェックボックスは Markdig 標準の出力を、読み取り専用（押しても変わらず、通常の色で表示。灰色の `disabled` をやめた）に置き換えている。見出し id は日本語を残す GitHub 方式。mermaid（CDN）・コードの色付け（highlight.js、CDN）・テーマ別 CSS・`#` リンク用スクリプト・`<base>` の URL 生成（日本語・空白・`#`・`%` 対応）。Edge（ヘッドレス）でライト/ダークの表示、チェックボックス、mermaid、色付けを確認済み

## [x] Phase 15: ドキュメント画面
- ツリー・一覧・絞り込み・概要カード・最後のファイルの復元・Watcher・設定ダイアログ（プレビューは空）
- 状況：実装済み（`Views/DocumentsView`・`DocumentSettingsDialog`、`ViewModels/DocumentsViewModel` ほか）。⚙ と対象フォルダ表示は TabControl の外の共通ヘッダーへ移した（どのタブでも見える）。隔離した環境で実機確認済み：ツリー（子孫込み件数・「すべて」）、フォルダ直下の一覧、絞り込み（ヒット分の件数）、概要カード、最後のファイルの復元、除外設定（ダイアログで保存 → 即再走査・再起動後も残る、除外パターンの変更が件数に反映）、Watcher（ファイル追加・フォルダ追加・フォルダ名変更・フォルダ削除・除外フォルダ内の変更）、12,000 ファイルでも固まらない、ライト/ダーク両方。プレビューは次の Phase

## [x] Phase 16: ドキュメントのプレビュー
- WebView2（html → md）、リンクの扱い、自動再読込、各ボタン。単一ファイル発行で動くかを、この Phase の中で確認する
- 状況：実装済み（`Views/DocumentPreview`、`PreviewFiles`、`ShellOpen`、`ThemeService.Changed`）。WebView2 の初期化には画面に載った（ウィンドウハンドルのある）コントロールが要るので、先に載せてから始める。DevTools プロトコル（`--remote-debugging-port`）で、隔離した環境のデバッグ版と、`scripts/publish.ps1 -SkipHook` で作った単一ファイル版（133.8MB）の両方を確認済み：md（チェックボックス 3 個・うち 2 個チェック済み、日本語の見出し id、mermaid の図、コードの色付け、相対パスの画像、`<base>`）、`#` リンクのページ内スクロール、md 内の別の md へのリンクと html 内の md へのリンク（アプリ内で選択）、html の相対 css・js・画像と JavaScript、ファイル保存での自動再読み込み、タブを切り替えて戻っても表示が戻る、ダーク（背景 #1E1E1E）。http リンクの既定ブラウザ起動、Runtime 未導入時の案内は、この PC が実環境のため実機では未確認（コードのみ）

## [x] Phase 17: ドキュメントの仕上げ
- 1万・5万ファイルの実測、Release の発行確認、本書（12.7・5章）の更新
- 状況：Release の発行（`scripts/publish.ps1`。Hook は NativeAOT、zip 58.5MB）を確認済み：展開した zip の Miharikun.exe（単一ファイル、133.8MB）で、隔離環境のドキュメントタブに md が表示された（チェックボックス・mermaid・色付け）。zip の README.txt と `docs/release.md` のチェックに、ドキュメントタブ（WebView2 Runtime）を追記した。走査時間を画面と `app.log` に出すようにした。**1万ファイル超の実フォルダでの実測は、利用者が会社の PC で行う**（結果待ち。5万ファイルの合成測定は行わない）。目安：ツリーが数秒以内に出ること。遅ければ、索引のキャッシュを別 Issue にする

## [x] Phase 18: Claude Code 対応 ① 共通化（Issue #11。挙動は変えない）
- 5.1 の構成に直す：`ISessionSource` / `SessionDelta`、`CursorSessionSource`（旧 `ProjectEventStore` の読み込み部分＋ `CursorTranscriptImporter`）、汎用 `ProjectEventStore`、`SessionMonitor`（各 Source の `WatchTargets`）、`IAgent` → `IAgentInfo` / `IHookAgent`、共通ツール名 `CommonTools`、モデル名 `"default"` の無視を `CursorAgent.Normalize` へ、`MetaStore` / `SessionMetaService` をエージェントごとに切り替え、`HookInstaller` / `HookSetup` のクラスのコメントに「Cursor 専用」と明記（名前は変えない。別のエージェントが Hook を使うときに抽象化する）、読み込み元ごとの例外の隔離と、捕まえられなかった例外のログ（13 章。画面の挙動は変えない）
- 完了条件：**既存のテストが全部通る（移したクラスに合わせた修正のみ。件数を減らさない）**。Cursor の画面・Hook exe の挙動が変わらない。Core は AOT 互換（警告 0）。`publish.ps1` が通る。**本物の Cursor のデータのコピー（利用者の了承を得てから）で、移す前と後の全セッションの要約が一致する**。同じコピーを使った隔離環境で、Cursor のセッションが従来どおり表示される
- 進め方：最初に、旧コードのまま特性テスト（テストの無い経路）と要約のダンプを足す。テストを先に直さず、既存のテストを正として、1 つずつ移す。`ProjectEventStore` は、ダミーの Source で新しいテストを書く。詳細は実装計画 `docs/issue11/issue11-claude-code-plan.md`（1 章の品質ゲート・8.1）
- 状況：実装済み（18-0〜18-5。コミットは利用者の指示を待つ）。`Sessions/CursorSessionSource.cs`・`ISessionSource.cs`、汎用 `ProjectEventStore`、`SessionMonitor`（`WatchTargets` を毎回取り直す）、`Agents/AgentCatalog.cs`・`CommonTools.cs`、`IHookAgent`、`SessionMetaService`（`Func<string, MetaStore>`）、`App.xaml.cs` の Source のリスト、未処理の例外のログ。品質ゲート 6 点を確認済み：テスト 514 件合格（スキップ 2。レビュー（`docs/issue11/issue11-phase18-review.html`）への対応後の件数。着手前は 482 件 + スキップ 1。既存テストの変更は構築行のみで、`Assert` の差分なし）／Core の `--no-incremental` ビルドは警告 0／`scripts/publish.ps1`（Hook の NativeAOT を含む）が通り警告 0／本物の Cursor のデータのコピーで、移す前と後（18-2・18-4・18-5 の各時点）の全セッション（5 件）の要約と検索文字列のダンプが完全に一致／同じコピーの隔離環境で、一覧 5 件（導入前を含む）・詳細・タイムライン・メモ欄・ステータスが表示された（UI Automation での確認。目視のスクリーンショットは未撮影）。**Opus によるレビューを挟んでから Phase 19 に進む**（計画 12.3）

## [x] Phase 19: Claude Code 対応 ② Core の正規化（Issue #11）
- `ClaudeCodeAgent`（`IAgentInfo`）、`ClaudeTranscriptNormalizer`（5.1 の対応表。状態を持つ。サブエージェントのログにも使える形）、`AgentEvent.SubagentId`、`SessionAnalyzer` の調整（サブエージェントの履歴・失敗したテスト）、`StalledRule`（表示用の状態）、`settings.json` の `runningTimeoutMinutes`（全体を読んで書く形に直す）、形式の変化のログ（11.1）、ブランチと時刻からの head（10.1）、作業ツリーのファイルを未コミットの判定から外す（10.1）
- 完了条件：対応表の全行にテスト（テスト先行。フィクスチャは実ログの構造を真似て手書き）。Core は AOT 互換
- 状況：実装済み（19-1〜19-5）。`Agents/ClaudeCodeAgent.cs`・`ClaudeTranscriptNormalizer.cs`・`ClaudeFormatLog.cs`、`AgentEvent.SubagentId`、`Sessions/StalledRule.cs`、`SessionSummary.Subagents`（履歴）、`Settings/AppSettings.cs`（`runningTimeoutMinutes`・全体を読んで書く）、`GitClient.GetHeadAt`、`Uncommitted.IsInWorktree` / `CommitCheck`（作業ツリー）。テスト 670 件合格（スキップ 2。Phase 18 の終わりは 514 件）。Core の `--no-incremental` ビルドは警告 0。フィクスチャは `ClaudeLogBuilder` で手書き（実ログのコピーなし）。**会話ログの実物との突き合わせは未実施**（JSON の形は要件の対応表と既知の形からの手書き。Phase 22-1 で確認）。実装で足した判断：Agent の結果が `is_error` のときもサブエージェントを終わりにする（5.1）／設定ファイルが一時的に読めないときは保存を取りやめる（他のキーを消さない）／Cursor の `subagent_id` を `SubagentId` に載せる（実データで未確認。無ければ古い順に照合）

## [x] Phase 20: Claude Code 対応 ③ Core の読み込み（Issue #11）
- `ClaudeSessionSource`（9.1：探索・`cwd` 照合・作業ツリー・候補 0 件のときの探索・追記読み・不完全な行・壊れた行・監視先の取り直し・`.claude` に書かない）、`MIHARIKUN_CLAUDE_DIR`
- 完了条件：隔離フォルダのダミー会話ログで、探索・追記・作業ツリーがテストで通る。大きなファイル（数 MB）の初回読み込みを実測して記録する
- 状況：実装済み（20-1〜20-3）。`Sessions/ClaudeLocations.cs`（`MIHARIKUN_CLAUDE_DIR`・候補フォルダ・最初の `cwd`・cwd での探索）、`ClaudeFolderName.cs`（英数字以外を 1 文字ずつ `-`。サロゲートペアは 2 つ）、`ClaudeSessionSource.cs`。照合は最初の `cwd`（作業ツリーは `<対象>\.claude\worktrees\` 配下も同じプロジェクト）。最初の `cwd` が見つかるまでの行は取っておき、見つかったらまとめて変換する（先頭 200 行に無ければあきらめる）。他のプロジェクトのファイルは開き直さない。作り直されたら変換の状態も作り直して `Replace`、消えたら（フォルダごとを含む）`Remove`（一覧が取れなかったフォルダは、消えたとは扱わない）。`WatchTargets` は候補のフォルダ＋まだ無くてもよい本体のフォルダ（すべて `CreateIfMissing=false`。`.claude` には何も作らない・書かないことをテストで確認）。cwd での探索は、候補が 0 件で `.claude\projects` が在るときだけ起動ごとに 1 回、`ReadNew` の中で行う（UI スレッドから呼ばれる監視先の取り直しでは行わない）。`GetSubagentEvents` は空を返す口だけ（`ClaudeSessionSource` のメソッド。`ISessionSource` には足していない）。テスト 738 件合格（スキップ 4 = 計測用 `PerfFact` 等。Phase 19 の終わりは 670 件）。Core の `--no-incremental` ビルドは警告 0。
- **大きなファイルの実測（20-3。`MIHARIKUN_PERF=1`。合成ダミー）**：1 回のやりとり約 16KB（Bash の出力 3KB・Read の結果 8KB・思考 1KB・読み飛ばす attachment 1KB を含む）を並べたログ。5MB：初回 29ms／20MB：初回 144ms（確保 173MB・保持 12MB）／60MB：初回 418ms（確保 518MB・保持 35MB）。要約の計算は 5ms 以下、追記 1 回分の読み込みは約 4ms、変化なしのポーリングは 0.1ms。セッション 300 本（合計 29.6MB）：初回 452ms、変化なしのポーリング 1 回 4.7ms。**「直近 N 日」の設定は、今は要らない**（17章の保留のまま）。実ログでの初回の時間は Phase 22-1 で測る

## [x] Phase 21: Claude Code 対応 ④ App の画面（Issue #11）
- 12.8：Source の組み立て（`App.xaml.cs`）、バッジ、エージェント絞り込み、「停止」の表示（表示用の状態で、絞り込み・件数も）、サブエージェントの表示、Claude のコミット一覧、タイムラインへのジャンプ（種類も見る）
- 12.9：設定画面（⚙ の「設定…」。停止とみなす時間）
- 完了条件：隔離環境で、Cursor と Claude が一覧に混ざって出る。絞り込みが効く。設定画面で変えた時間がすぐ効き、テーマを切り替えても消えない。ライト/ダークの両方で表示を確認する
- 状況：実装済み（21-1〜21-4）。`App.xaml.cs` に `ClaudeSessionSource` を足し、カードと詳細ヘッダーにエージェントのバッジ（枠線と文字だけ。Cursor / Claude Code の色はライト・ダークごと）。Core に `AgentFilter`（`AgentCatalog.All` の並び。件数は全カードから数える）と左ペインのチップ（全て / Cursor / Claude Code。表示名は `DisplayName`）。`StalledRule.DisplayState` を、カードの丸・文字・「実行中のみ」・件数・詳細ヘッダー・拡大時のタイトルのすべてに使う（`SessionCardViewModel` / `SessionDetailViewModel` の `State` が表示用の状態。1 秒ごとの更新で状態が変わったカードがあれば、一覧の絞り込みと件数も取り直す）。詳細の「サブエージェント」は「動いている N（説明、最大 3 件） / 動いた M」。`Capabilities` に無い項目は出さない（Compaction の無い Claude の詳細に「圧縮」の行を出さない。「閉じた」の件数は、終了の記録を持つエージェントのセッションが無ければ出さない）。コミット一覧は、head を持たないセッションをブランチと時刻から求める（`SessionCommits`。ブランチ・時刻が変わったときだけ git を実行）。完了チェックは作業ツリーのファイルを除外（すべてなら「コミットの確認なし（作業ツリーの変更のため不明）」）。タイムラインへのジャンプは、番号と種類が両方一致する行を優先（`TimelineBuilder.Find(items, seq, kind)`）。Claude の `SessionStarted` に会話ログのパスを載せ、詳細に transcript サイズを出す。設定画面（⚙ の「設定…」。`Views/AppSettingsDialog`）：「停止とみなす時間（分）」。`RunningTimeoutInput` で検査（半角の数字・0 以上・1 週間まで。違うと保存ボタンが押せず、理由を出す）。保存すると `settings.json` に書き（テーマなどの他のキーは残る）、すぐ効く。
- 確認（隔離環境。`MIHARIKUN_DATA_DIR`・`MIHARIKUN_CURSOR_DIR` は本物のコピー、`MIHARIKUN_CLAUDE_DIR` は手書きのダミー会話ログ 9 本。UI Automation とスクリーンショット）：Cursor 5 件と Claude 8 件（別プロジェクトの 1 本は出ない）が混ざって出る／チップの件数は絞り込みに関係なく 全て 13・Cursor 5・Claude Code 8／Claude で 5 件→2 件（＋「実行中のみ」で「実行中」と「実行中・46分動きなし」の 2 件。停止は含まれない）／停止の表示「停止・35分動きなし」、サブエージェント・結果待ちのツールがあれば「実行中・N分動きなし」／詳細：コミット 6 件・テスト実行 1 回・サブエージェント「動いている 1（説明） / 動いた 1」・作業ツリーは「コミットの確認なし（作業ツリーの変更のため不明）」／拡大時のタイトルに「（停止・35分動きなし）」／設定画面：数字以外・負の数・小数・空・10081・全角は保存できない、0・1・10 は保存できる。1 に変えると保存してすぐ一覧に効き、0（無効）で「停止」だった 1 件が「実行中」に戻り件数が変わる。`settings.json` はテーマを残して `runningTimeoutMinutes` が入る。再び開くと保存した値が出る／ダーク・ライトの両方で、バッジ・状態・設定画面が読める

## [x] Phase 22: Claude Code 対応 ⑤ 実機確認・仕上げ（Issue #11）
- **このプロジェクトの本物の Claude Code セッションを表示**して、成果（コミット・テスト実行）の見え方を確認する。14.2 の未確認項目（圧縮・再開・裏で動かした Agent・`ExitPlanMode` ほか）を、実機に出たものから確認する。README・`docs/release.md` を更新する
- 完了条件：目視確認 OK。未確認項目が「確認済み」または「保留」に整理されている
- 状況：**22-1 は実施済み**（14.2 の「Phase 22-1 の実機確認」に結果。会話ログは構造だけを数えた）。実物で見つけた 2 件を直した：①最初の記録（`queue-operation`）に `gitBranch` が無く、ブランチ・コミット一覧が出なかった ②開始より前にコミットが無いセッションのコミット一覧が「—」だった。未確認項目は、実物に出なかったので「保留」に整理した。**22-2 も実施済み**：配布 zip の `README.txt`（`scripts/publish.ps1`）に Claude Code（導入不要・読み取りだけ・停止の表示・⚙ の「設定…」）を追記、`docs/release.md` の出す前のチェックに Claude Code の確認を追加、`CLAUDE.md` の冒頭と「守ること」を Cursor と Claude Code に更新（`.claude` には何も書かない・会話ログの形式は公開仕様でない・構造だけを数える）。**Issue #11 の実装は、これで 15 章のフェーズ（18〜22）がすべて完了**。リリースはしていない（未リリースの変更は v0.1.5 以降に溜まっている。出すときは `docs/release.md`）。実機（Claude Code を日常的に使う PC）での通し確認と、保留にした記録の形（圧縮・Agent・`ExitPlanMode` ほか）の確認は、実物が出たときに行う

## Phase 23〜26: メモタブ（Issue #14）
12.10 / 6章。実装の分け方・ファイル構成・決めごとは実装計画 `docs/memo-tab/memo-tab-plan.md`（図解 HTML と対）。全体の完了条件：編集 → 保存で再起動後も残る。キャンセル・未保存で閉じるときの確認が効く。プレビューがドキュメントタブの md と同じに出る（タスクリスト・mermaid・相対パスの画像）。ドキュメントタブの挙動が変わらない。ライト/ダークの両方で確認する。

## [x] Phase 23: メモタブ Core
- `ProjectMemoStore`（対象フォルダ → メモのパス、読む（無ければ空）、アトミックに書く、更新日時）。テスト先行
- 完了条件：テストが通る。Core は AOT 互換（`--no-incremental` で警告 0）
- 状況：実装済み（23-1〜23-3）。`Memo/ProjectMemoStore.cs`（読めないときは例外・BOM 付きも読める・書くのは BOM なし）、`Memo/MemoEditor.cs`（編集の状態。改行コードの違いは変更とみなさない）、`Documents/DocumentLinkRule.cs`（リンク → ドキュメントタブで開けるか）、`AppPaths.ProjectMemoFile`（名前の作り方は設定の json と共通）。テスト 842 件合格（スキップ 5。Phase 22 の終わりは 793 件）。Core の `--no-incremental` ビルドは警告 0。コミットはまだしていない

## [x] Phase 24: md プレビューの共通化（挙動は変えない）
- `DocumentPreview` から、WebView2 の準備・未導入の案内・md の描画・リンクの振り分けを、ドキュメントとメモの両方で使える部品に切り出す
- 完了条件：既存のテストが全部通る。ドキュメントタブの表示（md・html・リンク・自動再読み込み・テーマ）が従来どおり
- 状況：実装済み（24-1）。`Views/MarkdownPreview`（`DocumentPreview` を改名・一般化）、`Views/IPreviewHost.cs`（`IPreviewHost`・`PreviewSource` の `File`/`Markdown`/`Message`）、`WebViewEnvironment`（環境を 1 つ共用。失敗は覚えない）。`DocumentsViewModel` は `IPreviewHost` を実装（案内の文は今のまま）。テスト 842 件合格（スキップ 5）。隔離環境（デバッグ版・DevTools プロトコル）で確認：md（チェックボックス・日本語の見出し id・コードの色付け・mermaid・`<base>`）、md→md と html→md のリンク、html の表示、ファイル保存での自動再読み込み、タブを切り替えて戻っても表示が戻る、ダーク（背景 #1E1E1E）。Runtime 未導入の案内・テーマ切り替えでの作り直し・`#` リンクは、コードの差分で確認（実機では未確認）。コミットはまだしていない

## [x] Phase 25: メモタブの画面
- `MemoView` / `MemoViewModel`：プレビュー ⇄ 編集、保存（Ctrl+S）・キャンセル（確認）、タブを移っても入力が残る、未保存で閉じるときの確認、リンクからドキュメントタブへ、外での書き換えの読み直し、空のときの案内
- 完了条件：隔離環境（`MIHARIKUN_DATA_DIR`）で 12.10 の各動作を確認する
- 状況：実装済み（25-1・25-2）。`ViewModels/MemoViewModel.cs`（`IPreviewHost` を実装。`MemoEditor` と `ProjectMemoStore`、メモのファイルの監視（300ms まとめて読み直し））、`Views/MemoView`（バー＋プレビュー＋入力欄。Ctrl+S）、`Views/UnsavedMemoDialog`（閉じるときの［保存］［保存しない］［キャンセル］）、`DocumentsViewModel.IsInAppDocument` / `OpenFromOutside`（走査の前・走査中は予定として覚え、走査の終わりに選ぶ。走査済みで索引に無ければ既定のアプリ）。`MainWindow` はメモのリンクで、ドキュメントタブに切り替えてから選ぶ。テスト 842 件合格（スキップ 5。Phase 25 は画面のみで、Core のテストの増減なし）。隔離環境（デバッグ版・UI Automation・DevTools プロトコル）で確認：空の案内・書く→保存→プレビュー・再起動後も残る・保存で「最終更新」が変わる・変更なしの保存はファイルを書かない・Ctrl+S・キャンセル（変更なし→すぐ戻る／あり→確認→「いいえ」で編集のまま・「はい」で破棄）・タブを移って戻っても入力が残る・読めないメモ（他から開いておく）→理由が出て編集できない→解放後に読み直される・外での書き換え（プレビュー中は変わる／編集中は取り込まない）・未保存で閉じる（キャンセル→閉じない／読み取り専用で保存失敗→閉じない／保存→閉じてファイルに残る）・メモのリンクからドキュメントタブへ（タブを一度も開いていないとき）・保存先は `projects` の `.memo.md`（BOM なし）・ダーク（背景 #1E1E1E・コードの色付け）。**未確認**：メモのリンクの「走査中」「走査済みで索引に無い」「予定があるうちに一覧で別のファイルを選ぶ」・除外フォルダ内の md・http のリンク（コードのみ）、外で書き換えた後の「キャンセルで新しい内容が出る」（Core のテストでは確認済み。画面では、確認用のダイアログの操作が安定せず未実施）、Runtime 未導入の案内、「保存しない」ボタン。コミットはまだしていない

## [x] Phase 26: メモタブの仕上げ
- ライト/ダーク、WebView2 未導入時（コードと案内）、配布 zip の `README.txt`・`docs/release.md`、本書の状況の更新
- 状況：実施済み。配布 zip の `README.txt`（`scripts/publish.ps1`）に「メモ」タブ（書く・保存・保存先・未保存の確認・リンク）を追記、`docs/release.md` の出す前のチェックにメモタブの確認を追加。ライト/ダークは Phase 25 で確認済み。WebView2 未導入の案内はコードのみ（実機では未確認）。`CLAUDE.md` は変えていない（守ることに変化なし）。**Issue #14 の実装は、これで 23〜26 がすべて完了**。リリースはしていない

※ Phase 27〜33 は Issue #22（macOS 対応。別ブランチ）で使う。Issue #17 は 34 から。

## [x] Phase 34: Cursor の Hook の置き場所・Hook なしの判定 Core（Issue #17）
- `settings.json` の `hookDir`、`HookInstaller` の置き場所の受け取りと「別の場所の登録」の検出、transcript の変わった分だけの取り込み、`NoHook` の判定（8.2・10・11 章）。テスト先行。計画：`docs/issue17/issue17-cursor-hook-plan.md`
- 状況：実装済み（34-1〜34-4）。`AppSettingsStore.LoadHookDir/SaveHookDir`、`HookInstaller`（`CreateDefault(…, hookDir)`・`DefaultHookDir`・`ToSettingValue`・`TryParseExePath`・`SameRegistration`・`FindRegisteredElsewhere`。設定した置き場所はフォルダを作らない。`GetState` の一致は `SameRegistration`）、`Install/HookRegistration`（`Stamp`・`Since`）、`CursorTranscriptImporter`（変わった分だけ・共有して読む・書きかけの最後の行は飛ばす・`HookMissing`・hooks.json が変わったら全部読み直す）、`CursorSessionSource`（毎回 Scan・同じ失敗のログは 1 回・transcript を監視先に）、`WatchTarget.IncludeSubdirectories`、`SessionState.NoHook`（「Hook なし」）・`SessionText.IsTranscriptOnly`。`App.xaml.cs` で組み立て済み（画面はまだ。NoHook は灰色の丸で出るだけ）。テスト 950 件合格（スキップ 5。Phase 34 の前は 842 件）。Core の `--no-incremental` ビルドは警告 0。起動中の流れ（登録あり・events なし・新しい transcript → Hook なし → 追記で増える → 登録を外すと導入前に戻る）は、`SessionMonitor` を使うテストで確認（画面での確認は Phase 35）。本物の transcript（この PC の 6 件）の末尾は、すべて改行で終わっていた（最後の 1 バイトだけ読んで数えた。中身は見ていない）。コミットはまだしていない

## [x] Phase 35: Cursor の Hook の置き場所・警告の画面（Issue #17）
- 起動時の受け入れ、設定画面の置き場所、`NoHook` の表示と一覧の上の警告（8.2・12.9・12.11）
- 状況：実装済み（35-1・35-2）。`HookSetup`（置き場所を受け取って installer を作る。起動時の受け入れ→`CanInstall` の確認→提案の順。受け入れたフォルダを直接渡して作り直す。`ChangePlacement`＝保存→導入し直すかを聞く）、`AppSettingsDialog`（置き場所の欄・「参照…」・「既定に戻す」。分と置き場所をまとめて検査する `Revalidate`）、Core の `HookDirInput`（存在しないフォルダ・完全なパスでない値は保存できない。既定の置き場所はまだ無くてもよい）、`MainViewModel`（`NoHookCount`・`HookWarningText`・`OpenHookErrorLogCommand`）、左ペイン最上段の黄色の帯（ライト/ダークの色を追加）、`SessionDetailViewModel` は `IsTranscriptOnly`。テスト 961 件合格（スキップ 5）。隔離環境（一時フォルダ・UI Automation）で確認：hooks.json を `\` 区切りの別の場所に書き換えて起動 → ダイアログなし・`hookDir` が入り・`app.log` に 1 行・次の起動でも出ない／引数なし → 「登録し直します」／別の場所の exe が同梱と違う → 「更新」→ はいで**その場所に**コピー（`data\bin` は作られない）／settings.json を排他で開いている間 → 受け入れは今回だけ（ダイアログなし・hooks.json は変わらない）／設定画面：存在しないフォルダ・相対パス・空は保存できない（分を打ち直しても保存できないまま）・既定に戻す・保存 → 導入し直す？ → いいえ（hooks.json は変わらず、次の起動で元の場所に戻る）・はい（13 件が新しい場所・exe コピー・バックアップあり・古い exe は残る）／帯：登録あり＋events なし＋新しい transcript 2 件 → 「Hook なし 2 件」・カードは灰色の丸「Hook なし」・hook-error.log は無ければ押せず、できると押せる・Hook の記録を置くと件数が減る・hooks.json から登録を外すと帯が消えて「閉じた（導入前）」に戻る。ライト/ダークの画面を確認。**未確認**：「hook を削除」メニューの実クリック（hooks.json の書き換えで代替）、「参照…」のフォルダ選択ダイアログ、「hook-error.log を開く」の実際の起動、詳細ペインの「—」の目視、本物の Cursor での確認（10.2。利用者の作業）。コミットはまだしていない

## [x] Phase 36: 仕上げ（Issue #17）
- 配布 zip の `README.txt`・`docs/release.md`、本書の状況の更新
- 状況：実施済み。配布 zip の `README.txt`（`scripts/publish.ps1`）に、会社の PC などで Hook が動かないとき（⚙ → 設定… → Hook exe の置き場所・hooks.json の登録の自動採用・記録先は変わらない）と、「Hook なし」の帯の意味（導入・置き場所の変更の直後に出たら、まず Cursor を再起動。hook-error.log を開くボタン）を追記。`docs/release.md` の出す前のチェックに、置き場所（受け入れ・設定画面・導入し直し）と「Hook なし」の警告の確認を追加。`CLAUDE.md` は変えていない（守ることに変化なし）。**Issue #17 の実装は、これで 34〜36 がすべて完了**（本物の Cursor での確認は利用者の作業。計画 10.2）。リリースはしていない。コミットはまだしていない

## 16. テスト方針

- Core は xUnit で網羅：`CursorAgent.Normalize` の対応表どおりの変換、状態遷移（再開・中断・エラー・stop 欠落）、実行中ツールの保険クリア、
  プロジェクト一致判定（大文字小文字・末尾区切り・マルチルート）、壊れた行のスキップ、追記途中の行の扱い
- テスト用 JSONL フィクスチャは Step 0 のダンプから個人情報・社内情報を除去して作成
- ドキュメント（Core）：`GitIgnoreMatcher`（コメント・`!`・末尾 `/`・`**`・大文字小文字・後勝ち）、`DocumentIndexer`（一時フォルダ。枝刈り・件数の子孫集計）、md→HTML 変換（チェックボックス 6 ケース）
- Claude Code（Core）：`ClaudeTranscriptNormalizer`（5.1 の対応表の行ごと）、`StalledRule`、`ClaudeSessionSource`（探索・`cwd`・作業ツリー・追記読み・不完全な行・壊れた行）。汎用 `ProjectEventStore` はダミーの Source でテストする

- メモタブ（Core）：`ProjectMemoStore`（一時フォルダ。パス・無いとき・読み書き・BOM なし・上書き・他のファイルを消さない）、変更の有無の判定
- Hook は「サンプル JSON を stdin に流して出力・終了コード・追記内容を検証」する結合テスト
- UI は手動確認（ワイヤーと突き合わせ）

## 17. 保留事項（MVP 後に相談）

- 実装計画 md とセッションの紐付け（添付・読み込みの検知＋フェーズ完了マーカーを afterFileEdit で検知する案）
- 一覧カードの完了判定ミニ表示
- 既存チャットを Cursor で開くボタン（公式に会話 ID 指定で開く deeplink は見当たらない）
- タスク・課題の更新表示
- 承認待ち状態の判定
- ドキュメントタブ：本文検索、md の編集、ボード表示、索引のキャッシュ
- Claude Code：セッションのタイトル（`custom-title` / `ai-title`）の取り込み、サブエージェントの詳細表示（`GetSubagentEvents`）、古いセッションの「直近 N 日」の設定（初回の読み込みが遅いとき）
