# 引き継ぎ（次セッションへ） — 2026-06-07

## このプロジェクト
`my-shortcut-ime`：子音主体のローマ字入力で**長文をテンポよく**書く日本語入力アシスト（C#/.NET 10/WinUI 3）。最優先は**アーキテクチャの美しさ＋DS&A 意識**。研究の本丸は「候補生成」でなく**候補順位付け（reranking）**。詳細プラン: `~/.claude/plans/wfst-mozc-mozc-embedding-bert-ime-encode-adaptive-cloud.md`。

## 状態：pre-Stage-1（土台）は完成。Stage 1 は次セッション。
- 全テスト緑：**Core 90 / Evaluation 22**。`mise exec -- dotnet test tests/<proj>` で再現。App もビルド緑（実行は WinUI 環境が要）。
- 環境：dotnet は **mise 経由**（`mise exec -- dotnet ...`）。辞書は `data/dictionary_oss`（Mozc oss、129万エントリ＋connection）。

## 完了した土台
1. **測定ハーネス**：`src/ShortcutIme.Evaluation`（純粋指標 `ConversionMetrics`／`EvaluationHarness`／`EvalDataset`／`SentenceTemplateGenerator`）＋ `tools/ShortcutIme.Eval`（CLI）。seed `data/eval/seed.tsv`（32文）。
2. **逆変換器の正確化**（＝テスト生成器）：音節母音は残す／長音「う」は保守的に省略／**促音「っ」**は次子音重ね。`ConsonantEncoder`・`RomajiEncoder`・`KanaRomanization`。不変条件「子音入力⊆フルの母音削除部分列」をテスト＋全辞書監査で担保。
3. **多方式＋混在**：`RomajiScheme`(訓令/ヘボン)、`RomajiVariants.ExpandReading`（モーラ異形の**直積**）。`RomajiTrie.Build` が全異形を索引→ `shi+tu` 等の混在打鍵も引ける。入力正規化は不可（子音だけだと sh が し/す+は で曖昧）。
4. **メモリ圧縮（DS&A）**：`RomajiTrie` を **CSR フラット配列**（per-node Dictionary 廃止）＋候補 dedup＋文字列 intern。**1,237MB→~130MB**。バイナリ**直列化** `Save/Load`（blob 121MB、**ロード427ms** vs 再構築17s）。App は blob キャッシュ（無ければ構築→保存、あればロード）。
5. **テスト大幅拡充（機械生成）**：`tools/ShortcutIme.DataGen` がテンプレ文 `data/eval/generated.tsv`（400）と**語レベル大規模測定**（5万語/方式）と不変条件監査を実行。

## 主要数値（参考）
- 文（seed, 1-best→n-best, 20-best）：訓令式/子音 top-1 50% / **gold∈n-best 69%（=リランカー伸びしろ約19pt）**、フル top-1 75% / gold∈n-best 81%。
- 語レベル（5万語）：訓令式/子音 top-1 33% / top-10 58%、フル top-1 59% / top-10 87%。
- 詳細: `docs/stage0-eval.md`、`docs/stage1-nbest.md`。

## Stage 1 のインフラは“先行して”完成（モデルだけ次回）
本セッション中に Stage 1 の**インフラ**まで入れてある（テスト緑・要れば破棄可）：
- `PhraseConverter.ConvertNBest(input, n)`（k-best ビタビ、`Hypothesis` を返す。連接コストを部分経路の末尾語で見るため 1-best より精緻）。
- `IReranker` seam ＋ `IdentityReranker`（`src/ShortcutIme.Core/IReranker.cs`、`Hypothesis.cs`）。
- Eval が n-best で gold∈n-best/top-k/MRR を測る（n-best サイズは第3引数）。
- ※中途半端だった LM リランカーは削除済み（ビルドを汚さないため）。

## 正直な“積み残し／注意点”（土台の既知ギャップ）
1. **活用合成の穴（最重要）**：`送って=おくって`・`買った=かった→刈田/勝田` 等、**活用形が辞書の単位に無く格子で作れない**。生成テンプレ文の約1/3は gold 到達不能＝出題として不公平。リランカー研究の前に効く。対処は Stage 3（高頻度活用形の語彙化）か活用テーブルでの格子合成。
2. **大規模で“公平な文”コーパスが無い**：本環境に python/MeCab 等の形態素解析器が無く、生文→読みの自動付与ができない（ネットと NuGet は可）。語レベルは公平・大規模だが文脈なし。文の大量・公平生成は #1 解消＋解析器導入（NMeCab 等）が要。
3. **長音記号「ー」**：まだ簡易化（脱落）。`う`長音は保持なのに `ー`は脱落で不整合（外来語に影響、軽微）。
4. **打鍵癖 `を→o`・`ん→nn`**：未対応（訓令/ヘボン軸のみ）。`RomajiVariants.ForMora` に足せる。
5. **`ゔ`・外来小書き音（ふぁ/てぃ/うぃ）**：未対応（全辞書監査で違反は1エントリのみ）。
6. **App は実行時未検証**（WinUI をここで起動できず、blob キャッシュはコンパイルのみ確認）。
7. 多方式直積の cap=64（超高異形語3141件は2方式に退避）。mmap ゼロコピー/パス圧縮は未（130MB で不要判断）。

## 次セッション（Stage 1 本体）
- **実リランカー**で gold∈n-best の伸びしろ（約19pt）を top-1 へ。まず n-gram LM（**コーパスが要**。char n-gram なら解析器不要で着手可）か特徴量＋LightGBM（`Microsoft.ML.LightGbm`、Core 非依存の別プロジェクト）。
- hard negatives は lattice の n-best（実分布）。学習データは語レベル（大規模・公平）＋（#1 を直した上での）文生成。
- 余裕があれば #1（活用）に着手すると文テストの公平性が一気に上がる。

## よく使うコマンド
- テスト：`mise exec -- dotnet test tests/ShortcutIme.Core.Tests/ShortcutIme.Core.Tests.csproj -c Release`
- 文評価：`mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/seed.tsv [n-best]`
- 生成＋メモリ/監査/語レベル測定：`mise exec -- dotnet run -c Release --project tools/ShortcutIme.DataGen -- data/dictionary_oss [語サンプル数]`

## 作業スタイル（記憶済み）
土台を徹底的にやりきってから次へ／生成テスト入力の忠実性を厳しく見る（[[foundation-first-working-style]] [[eval-input-must-be-faithful]] [[multi-scheme-mixed-input]] [[compact-dictionary-dsa]] [[reranking-roadmap]]）。
