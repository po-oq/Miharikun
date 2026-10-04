# 図の部品（コピーして座標・文言を調整する）

SVG は手書き座標なので、`viewBox` の幅を 960 に統一し、箱の間に矢印の幅（40px 以上）を空けると崩れにくい。
日本語は 1 文字 ≒ 13px（font-size 13 のとき）で、箱の幅を見積もる。長い文は `<text>` を行ごとに分ける。

## 配色の約束（意味で色を決める。`impl-plan` スキルの図と共通）
| 用途 | fill | stroke |
|---|---|---|
| 外部データ・入力 | `#ffe9c8` | `#a70` |
| 既存の処理・ロジック | `#dff` | `#08a` |
| 新規・変更（新しい interface を含む） | `#dfd` | `#383` |
| 中核・共有部品 | `#fef` | `#a3a` |
| 利用側（画面・API・CLI など） | `#efe` | `#383` |
| 環境・外部の仕組み・補助（ファイル・OS など） | `#eee` | `#888` |
| 問題の箇所（レビュー）・失敗の分岐 | `#fde` | `#b33` |
| 強調・選択中・特別な状態 | `#e8f1fd` | `#0b63c5` |
| 注記テキスト | fill=`#c05000`（橙）／補足 `#666` |

線：通常 `stroke="#333"`／成功の戻り `stroke="#383" stroke-width="2"`／取り消し・失敗 `stroke="#b33"`／共用・参照のつながりは破線 `stroke-dasharray="5,3"`。
矢印の `<defs>`（`id="a"`）は最初の SVG に 1 回だけ置き、後の図でも使い回す。最初の図を消すときは `<defs>` を次の図に移す。

## 概念図（箱と矢印）
```html
<svg viewBox="0 0 960 300" width="960" font-size="13" font-family="sans-serif">
<defs><marker id="a" markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto"><path d="M0,0 L8,4 L0,8z" fill="#333"/></marker></defs>
<!-- 箱：タイトル（太字）＋説明（font-size 12） -->
<rect x="10" y="30" width="190" height="56" fill="#ffe9c8" stroke="#a70"/>
<text x="18" y="50" font-weight="bold">入力元</text><text x="18" y="68" font-size="12">説明</text>
<rect x="250" y="30" width="230" height="86" fill="#dfd" stroke="#383"/>
<text x="260" y="52" font-weight="bold">新規クラス</text><text x="260" y="70" font-size="12">役割の説明</text>
<!-- 矢印（始点→終点）と、矢印に添えるラベル -->
<line x1="200" y1="58" x2="250" y2="58" stroke="#333" marker-end="url(#a)"/>
<text x="205" y="50" font-size="11" fill="#555">ラベル</text>
<!-- 図の下の注記 -->
<text x="10" y="280" font-size="12" fill="#c05000">※ 補足・注意点</text>
</svg>
```
- 「いま」と「変更後」を比べるときは、`<h3>いま</h3>` `<h3>変更後</h3>` で SVG を 2 つ縦に並べる。
- 問題の箇所は `fill="#fde" stroke="#b33"` にして、箱の横に `<text fill="#b33">← 問題 #1</text>` と番号を振る（問題一覧の # と対応させる）。

## 画面イメージ（ワイヤー）
```html
<svg viewBox="0 0 960 330" width="960" font-size="13" font-family="sans-serif">
<!-- 左ペイン -->
<rect x="0" y="0" width="330" height="330" fill="#fff" stroke="#999"/>
<text x="10" y="20" font-weight="bold">左：一覧</text>
<rect x="10" y="30" width="310" height="24" fill="#eee" stroke="#aaa"/><text x="16" y="47" fill="#666">🔍 検索</text>
<!-- チップ（絞り込みなどのボタン）。選択中は fill="#cfe3ff" stroke="#08c" -->
<rect x="10" y="62" width="40" height="20" rx="10" fill="#cfe3ff" stroke="#08c"/><text x="19" y="77">全て</text>
<rect x="56" y="62" width="62" height="20" rx="10" fill="#fff" stroke="#999"/><text x="64" y="77">項目</text>
<text x="124" y="77" font-size="11" fill="#c05000">← 新規</text>
<!-- カード（選択中） -->
<rect x="10" y="92" width="310" height="70" fill="#e8f1fd" stroke="#0b63c5" stroke-width="2" rx="6"/>
<circle cx="24" cy="109" r="5" fill="#0969da"/><text x="34" y="113" font-size="11">進行中</text>
<text x="132" y="113" font-weight="bold">項目名</text>
<text x="20" y="132" font-size="11" fill="#666">12 依頼 ・ 2 分前</text>
<!-- 右ペイン（詳細） -->
<rect x="340" y="0" width="620" height="330" fill="#fff" stroke="#999"/>
<text x="350" y="20" font-weight="bold">中央：詳細</text>
<!-- 詳細の中のブロック。変わる所には橙の注記を添える -->
<rect x="350" y="30" width="600" height="86" fill="#f7f7f7" stroke="#bbb" rx="4"/>
<text x="360" y="50" font-weight="bold">ブロック名</text>
<text x="360" y="70">✓ 項目　<tspan fill="#2a7a2a">✓ 通過</tspan>　<tspan fill="#b33">✗ 失敗</tspan></text>
<text x="360" y="92" font-size="11" fill="#666">補足</text>
</svg>
```
- 状態色（必要なら）：進行中 `#0969da`、完了・OK `#1a7f37`、注意・停止 `#d4a72c`、エラー `#b33`。
- 画面の下に 1〜2 行で「変わる所／変わらない所」を書く。
- 画面全体は描かない。**変わる部分と、その周囲が分かる範囲だけ**でよい。

## 対応表・選択肢
- 対応表は `<table>`（`th` に見出し）。コードのクラス名・ファイル名は `<code>`。
- ファイル構成案は `<pre>` のツリー（`├─` `└─`）。右側に「Phase 番号」や変更内容を桁を揃えて書く。
- 未決事項は `<div class="q">`、推奨は `<span class="rec">`。決定済みは `<table>` で「Q1 …→ 決定内容」。
- 末尾に「反映先」を書くときは `<div class="sync">`。
