# Miharikun（みはりくん）

Cursor IDE と Claude Code のチャットセッションを、プロジェクト単位で一覧・状態把握・概要/メモ管理する Windows デスクトップツール（C# / WPF）。
Cursor は Hook で記録したイベント、Claude Code は会話ログ（`~\.claude\projects\`、読み取りだけ）から読む。
※ macOS 対応（画面を Avalonia 12 に移す）を進めている：要件定義 15 章の Phase 27〜33（Issue #22）

## 必読
- 仕様の正：`docs/miharikun-requirements.md`（要件定義・実装方針・実装フェーズ）
- 画面配置の参考：`docs/miharikun-wire.html`（設計用ワイヤー。取得元バッジ・注記・サンプルデータは実装しない）
- Step 0 用の検証ツール：`docs/step0-dump-hook/`（本体とは別物。ソリューションに含めない）
- リリース手順（タグ push／GitHub 画面から／試運転）：`docs/release.md`
- macOS 対応の設計：`docs/macos-support/macos-support-design.html`、実装計画：`docs/macos-support/macos-support-plan.md`（図解 HTML と対）

## 進め方
- `docs/miharikun-requirements.md` の 15章のフェーズ順に実装する
- フェーズ完了時は見出しの `## [ ] Phase N` を `## [x] Phase N` に更新する
- 仕様が曖昧・矛盾していると感じたら、推測で実装せず質問する
- ワイヤーと要件定義が食い違う場合は要件定義を優先する

## 作業の決まり
- 返答は日本語
- コミット・push・PR・マージ・Issue を閉じる・リリース・ブランチの削除は、頼まれたときだけ
- 作業を始める前に main から作業ブランチを切る。コミットの前に、いまのブランチを確かめる
- `git add` はパスを指定する（`-A` は使わない。未追跡の文書や別セッションの編集を巻き込まないため）
- コミットメッセージは、途中は `Refs #N`、最後だけ `Fixes #N`（PR 本文に `Closes #N`）。複数の Phase は Phase ごとにコミットを分け、要件定義の `[x]` と「状況」もその Phase のコミットに入れる
- 大きい修正の流れ：図入りペライチ HTML（`peraichi`）→ 利用者の回答 → 要件定義の更新 → 実装計画（`impl-plan`。md と HTML の対）→ 別セッションのレビュー → 実装（Core のテスト先行 → ViewModel → 画面）。計画に「Phase の終わりで止まって報告」とあれば、次の Phase に勝手に進まない
- 完了報告には必ず、アプリ本体をビルドしたかと出力先（していなければ「ビルドしていない」と理由）、テストの件数、できなかった確認を書く。動作確認できるよう既定の出力先にビルドしておく（起動中で上書きできなければ `-p:OutDir=` で別の場所に作り、その場所を書く）
- システムへのインストールは自分でせず、利用者に頼む。重い作業・環境に影響する作業・本物のデータのコピーは、先に説明して了承を取る（コピーは確認後に消す）
- レビューの指摘は、実コードで事実を確かめてから採否を決める
- 文書は Issue ごと（`docs/issueN/`）か機能名（`docs/<機能名>/`）のフォルダに置く。共通の文書だけ `docs/` 直下
- 本物のデータに触れずに試すときは、環境変数 `MIHARIKUN_DATA_DIR`・`MIHARIKUN_CURSOR_DIR`・`MIHARIKUN_CLAUDE_DIR` で一時フォルダに向ける。`dotnet build`／`test` の前にアプリを止める

## 守ること
- LLM 呼び出し、Cursor の state.vscdb の読み書き、トークン/コスト取得は実装しない
- **Claude Code の `~\.claude\` には何も書かない**（フォルダも作らない。読み取りだけ）。会話ログは公開された仕様ではないので、知らない種類・壊れた行は止まらずに飛ばす。`app.log` には行の中身（会話の本文）を書かない（9.1・11.1章）
- App（状態判定・画面・検索）は共通イベント `AgentEvent` のみを扱い、エージェントの生データ（Cursor の生 JSON、Claude Code の会話ログ）を直接参照しない（5.1章）。エージェントごとの違いは `ISessionSource` と `Capabilities` に閉じ込める
- Hook exe は NativeAOT（Cursor 専用）。Core はリフレクションを使わず AOT 互換で書く
- 会話ログの確認では、構造（種類・件数）だけを数え、本文・コマンド・パスの中身は引用しない。テストのフィクスチャは、実ログのコピーでなく、構造を真似て手書きする
- 本物の環境を勝手に書き換えない・消さない：`~/.cursor/hooks.json`、Miharikun のデータ（Windows `%LOCALAPPDATA%\Miharikun\`、mac `~/Library/Application Support/Miharikun/`）
- Hook の NativeAOT は、その OS の上でしか作れない（`win-x64` は Windows か CI、`osx-arm64` は mac か CI）
