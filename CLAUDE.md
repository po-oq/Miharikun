# Miharikun（みはりくん）

Cursor IDE のチャットセッションを、プロジェクト単位で一覧・状態把握・概要/メモ管理する Windows デスクトップツール（C# / WPF）。

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
- App（状態判定・画面・検索）は共通イベント `AgentEvent` のみを扱い、Cursor の生 JSON を直接参照しない（5.1章）
- Hook exe は NativeAOT。Core はリフレクションを使わず AOT 互換で書く
