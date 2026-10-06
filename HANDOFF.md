# 引き継ぎ（Issue #22：macOS 対応）— 2026-10-07

## 現状
- 作業ブランチ：`feature/issue22-macos-support`（push 済み。最新コミット `0612264`「Phase 28: ViewModel を Miharikun.Presentation に切り出す」）。`HANDOFF.md` もコミット済み（引き継ぎ用）。
- **Phase 28（Windows）は完了**。利用者の決定で順番を入れ替え、27 より先に行った。要件定義の Phase 28 は `[x]`、状況も記入済み。
- 次は **mac で Phase 27（mac の実機確認）を単独で行う**（27 の結果で Phase 29 の中身が決まるため。Hook が呼ばれるか・空白を含むパス・`workspace_roots` の形・日本語・slug など。計画 7.1）。終わったら止まり、利用者にコミットと push を頼む。Phase 29 以降はそのあと。

## 守ること
- `CLAUDE.md`（作業の決まり・守ること）、`docs/macos-support/macos-support-plan.md`（1 章・7 章・10 章）、`docs/miharikun-requirements.md` の 15 章。仕様の正は要件定義。
- 1 回の作業で 1 Phase だけ。コミット・push は頼まれたときだけ。`git add` はパス指定。完了報告に、ビルドの有無と出力先・テスト件数（OS ごと）・できなかった確認を書く。
- Phase 27 は Miharikun のコードとテストを変えない。結果は新規ファイル `docs/macos-support/phase27-mac-check.md` に書き、要件 14.3 に要約と参照、計画 7.1 の表に「結果（Phase 27）」の列（md と html の両方）を足す。
- 本物の `~/.cursor/hooks.json`・`~/Library/Application Support/Miharikun/` は、先に説明して了承を取り、バックアップしてから。`~/.claude/` は読むだけ。会話ログは構造（種類・件数）だけ数える。SDK 等のインストールは利用者に頼む。

## Phase 28 でやったこと・分かったこと（詳細は成果物を参照）
- 内容と結果：`docs/macos-support/phase28-wpf-record.md`（切り出し前後の WPF 版を隔離環境で UI Automation により照合した記録）。要件定義の Phase 28「状況」。
- テスト：Windows で 1047 件合格・スキップ 5（基準は 961 だった）。mac は未実施（29-2 の始めに基準を取り直す）。
- 計画との違い：`MainViewModel` のコンストラクターは `GitClient` ではなく `Func<GitStatus?>` と `Func<SessionSummary, …>` を受け取る。コミットは 28-1〜28-3 に分けず 1 つにした。
- 未確認（目視）：詳細の概要・3 行サマリー・成果・ドキュメントのプレビュー、「Hook なし」の帯の見た目、テーマの切り替え。
- 確認ハーネス（UI Automation の使い捨て。リポジトリ外）は、このセッションのスクラッチ領域にあり、引き継がれない。Phase 30・31 の Windows での確認で同種のものが要るなら、作り直す。ハマりどころ：UIA の `Invoke` はダイアログを開く操作をブロックする（別スレッドで呼ぶ）、モーダルのダイアログは UIA ではメインウィンドウの子として見える、WPF の `AutomationId` は `x:Name` でも付く。

## mac で始めるときの注意
- Windows でコミットした内容は `git pull` で取る（未追跡ファイルは mac に無い）。
- Phase 30-1 までは `dotnet build Miharikun.slnx` が WPF のせいで失敗する。`dotnet test tests/Miharikun.Tests` のようにプロジェクトを指定する。
- Windows の Hook（NativeAOT）は mac では作れない。mac の Hook は `dotnet publish src/Miharikun.Hook -c Release -r osx-arm64 -o <一時>`。

## 推奨スキル
- `superpowers:using-superpowers`（開始時）
- `superpowers:verification-before-completion`（完了報告の前）
- `impl-plan`（計画を直すとき。md と html は対）

## 未決
- Phase 28 の差分と品質ゲートの結果のレビュー（計画 10.2。利用者が望めば別セッションで）。
