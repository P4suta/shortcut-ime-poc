# Stage 1（着手）: n-best ＋ gold∈n-best ＋ IReranker seam（2026-06-07）

[ロードマップ](../../../.claude/plans/wfst-mozc-mozc-embedding-bert-ime-encode-adaptive-cloud.md) の Stage 1。リランカーの**インフラ**（n-best 化・順位指標・seam）を入れ、リランカーの伸びしろを定量化した。実際のリランカー・モデル（n-gram / LightGBM / ニューラル）は Stage 1.5/2＝次セッション。

## 実装
- **n-best**: `PhraseConverter.ConvertNBest(input, n)` = 位置ごとに上位 K=n の部分経路を保持する k-best ビタビ（`Hypothesis(Segments, Cost)` を返す）。連接コストを部分経路の末尾語で見るため 1-best より精緻（全辞書で top-1 が 47%→50% に微増）。既存 `Convert`（1-best）は据え置き。
- **IReranker seam**: `IReranker.Rerank(input, leftContext, hypotheses)` ＋ `IdentityReranker`。Stage 1.5/2 がこれを実装する。
- **指標**: Eval を n-best 化し top-1/top-5/top-10/**gold∈n-best**/MRR を方式×母音レベルで測定（`tools/ShortcutIme.Eval`、n-best サイズは第3引数で可変）。

## 結果（seed 32文・20-best）
| プロファイル | top-1 | top-5 | gold∈n-best | MRR |
|---|---|---|---|---|
| 訓令式/子音 | 50% | 66% | **69%** | 0.572 |
| 訓令式/フル | 75% | 78% | 81% | 0.770 |
| ヘボン式/子音 | 53% | 62% | 72% | 0.586 |
| ヘボン式/フル | 78% | 78% | 81% | 0.786 |

→ **gold∈n-best 69% vs top-1 50% ＝ リランカーの伸びしろ 約19pt**。誤りの多くで gold が 2〜4 位（申し訳ございません 2位、了解 2位、添付 2位、週末 2位）＝**文脈リランカーで拾える**。圏外は活用合成の穴（買った 等）。

## テスト大幅拡充（機械生成）と「どれだけ生成すべきか」
- **テンプレ＋スロット生成**（`SentenceTemplateGenerator`、読みは構成上正確）で 400 文→`data/eval/generated.tsv`。これは難しく、訓令式/子音 top-1 18%・gold∈n-best 33%(N=20)→58%(N=200)。フルは 67% で N を増やしても不変。
- **重要な発見**: フルの未到達 33% は**活用形（送って=おくって 等）が辞書の単位に無く格子で作れない**＝活用合成の穴。テンプレ文の約1/3は現状アーキで到達不能な出題。→ **量より質（gold 到達性）**。
- **語レベル大規模測定**（辞書語＝gold 到達保証・読み正確、`tools/ShortcutIme.DataGen` でサンプル可変。51,564語×方式）:

| プロファイル | top-1 | top-5 | top-10 | MRR |
|---|---|---|---|---|
| 訓令式/子音 | 33% | 51% | 58% | 0.415 |
| 訓令式/フル | 59% | 82% | 87% | 0.694 |
| ヘボン式/フル | 59% | 83% | 88% | 0.696 |

- **指針**: テスト用は数千文で誤差十分小（百万は過剰）。百万級は**学習用**で活き、その本命は**辞書語レベル**（到達保証・読み正確）。文単位の大量生成は活用合成の穴を埋めるまで gold 到達性に注意。

## 次セッション（Stage 1.5 へ）
- 実リランカー（まず n-gram LM か特徴量＋LightGBM）で gold∈n-best の伸びしろ（約19pt）を実際に top-1 へ。
- 活用合成の穴（送って/買った 等が単位に無い）への対処：高頻度活用形の語彙化（Stage 3）か、活用テーブルでの格子合成。
- 学習データは辞書語レベル＋（到達性を直した）文生成で大量供給。hard negatives は lattice n-best。
