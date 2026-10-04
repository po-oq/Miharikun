# Miharikun（みはりくん）

Cursor IDE と Claude Code のチャットセッションを、プロジェクト単位で一覧・状態把握・概要/メモ管理する Windows デスクトップツール（C# / WPF）。
Cursor は Hook で記録したイベント、Claude Code は会話ログ（`~\.claude\projects\`、読み取りだけ）から読む。

## 必読
- 仕様の正：`docs/miharikun-requirements.md`（要件定義・実装方針・実装フェーズ）
- 画面配置の参考：`docs/miharikun-wire.html`（設計用ワイヤー。取得元バッジ・注記・サンプルデータは実装しない）
- Step 0 用の検証ツール：`docs/step0-dump-hook/`（本体とは別物。ソリューションに含めない）
- リリース手順（タグ push／GitHub 画面から／試運転）：`docs/release.md`

## 進め方
- `docs/miharikun-requirements.md` の 15章のフェーズ順に実装する
- フェーズ完了時は見出しの `## [ ] Phase N` を `## [x] Phase N` に更新する
- 仕様が曖昧・矛盾していると感じたら、推測で実装せず質問する
- ワイヤーと要件定義が食い違う場合は要件定義を優先する

## 守ること
- LLM 呼び出し、Cursor の state.vscdb の読み書き、トークン/コスト取得は実装しない
- **Claude Code の `~\.claude\` には何も書かない**（フォルダも作らない。読み取りだけ）。会話ログは公開された仕様ではないので、知らない種類・壊れた行は止まらずに飛ばす。`app.log` には行の中身（会話の本文）を書かない（9.1・11.1章）
- App（状態判定・画面・検索）は共通イベント `AgentEvent` のみを扱い、エージェントの生データ（Cursor の生 JSON、Claude Code の会話ログ）を直接参照しない（5.1章）。エージェントごとの違いは `ISessionSource` と `Capabilities` に閉じ込める
- Hook exe は NativeAOT（Cursor 専用）。Core はリフレクションを使わず AOT 互換で書く
- 会話ログの確認では、構造（種類・件数）だけを数え、本文・コマンド・パスの中身は引用しない。テストのフィクスチャは、実ログのコピーでなく、構造を真似て手書きする
