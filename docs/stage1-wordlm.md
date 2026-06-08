# Stage 1（Phase 3 続）: 語 bigram LM ＝子音条件で char を明確に上回る（正の結果）

char LM（[stage1-reranker.md](stage1-reranker.md) §5）は子音で top-1 +3〜6pt だったが、丁寧形 dev の正味改善は弱く（≈+1pt 級の体感）、頭打ちだった。本書は **語（文節）bigram LM** を、reranker のトークン単位（`Candidate.Surface`＝Mozc 辞書単位）に揃えて学習し、**子音条件で char を MRR で一貫して上回る**ことを実証した記録（利得はセット依存＝実筆 seed で穏当・templated generated で大、§6）。

## 1. 本丸＝語彙整合（コーパスを reranker の単位で分割する）

reranker `LmReranker` は `Hypothesis.Segments`（=`Candidate.Surface`＝Mozc 辞書単位）上でスコアする。一方コーパス `corpus.tsv` は生文。fugashi（unidic 短単位）で分かち書きすると Mozc 文節より 1.5〜5 倍過分割し OOV 化（§5.1 で診断済み）。

**解**：コーパス表層を **reranker と同じ Mozc 辞書（単語コスト＋連接コスト）で最小コストビタビ分割**する [SurfaceSegmenter](../src/ShortcutIme.Evaluation/SurfaceSegmenter.cs)（オフライン専用＝Evaluation に置き runtime Core は不汚染。`Dictionary<surface, SegEntry[]>`＋span プローブ、1文字フォールバック常設、NFC 正規化、`PhraseConverter` はリファクタせず小さい専用 surface ビタビ）。emit は **生 surface**（正規化形でなく＝スコア時の `Candidate.Surface` と同一）。

[LmReranker](../src/ShortcutIme.Core/LmReranker.cs) は **N-LM 線形補間**（`Cost + Σ λ_i·NegLogProb_i`）へ一般化（単一は縮退ケースで温存）。char+word 補間を1つのリランカーで扱える。

## 2. 整合性ゲート（seg-check）＝全コーパス学習前の検証

新セグメンタの分割と gold hypothesis（reading-lattice の Mozc 文節境界）の **token 列一致**（LCS ベース）を gold∈n-best で測る。**事前登録ゲート：token agreement(recall) ≥ 80%**。

surface だけだと安い homograph で**活用語尾が過分割**（例「しました」→「し/まし/た」「ございません」→「ござい/ませ/ん」）。これは word bigram の最頻機能語列が reranker 単位と食い違う＝致命的。**segmentPenalty で長い辞書単位（reading-lattice が作る単位）へ寄せる**ことで解消：

| | seed exact | seed F1 | generated exact | generated F1 |
|---|---|---|---|---|
| segPenalty=0 | 53.8% | 73.7% | 56.9% | 86.6% |
| **segPenalty=3000** | **80.8%** | **89.2%** | **100.0%** | **100.0%** |

segPenalty=3000 で generated は完全一致、seed は F1 89%（残差は丁寧長文の少数）。**ゲート PASS（強）**。8000/15000 でも同値＝プラトー。

## 3. コーパス & 学習

```
segment-corpus（segPenalty=3000）: 138,915 文 → 2,411,392 トークン、フォールバック 10.40%
word.bin: vocab=77,324, distinct-bigrams=588,491（char は 3,624 / 182,725＝word は大きく疎）
```

## 4. dev チューニング（G1, dev 77文, Kunrei/Consonant, n=100）

| | MRR |
|---|---|
| baseline（λ=0＝POS-bigram のみ） | 0.1278 |
| **char** best（λ_bi=0.5, floor=10, λ=100） | 0.1405 |
| **word** best（λ_bi=0.7, floor=15, λ=300） | **0.1773** |
| **char+word 補間** best（λ_char=50, λ_word=300） | **0.1789** |

word 単独が利得の大半を稼ぎ、char の上乗せは僅少。best はグリッド中央（char は端寄りだった）＝健全。**G1 PASS**。

## 5. test 測定（n=100、frozen λ_char=100 / λ_word=300）

`eval-interp`：id（identity）/ ch（char単独）/ wd（word単独）/ cw（char+word補間）。

**seed（32文・丁寧長文）**

| プロファイル | id t1 | ch t1 | wd t1 | cw t1 | id MRR | ch MRR | wd MRR | cw MRR |
|---|---|---|---|---|---|---|---|---|
| Kunrei/Consonant | 50% | 56% | 59% | **59%** | 0.574 | 0.607 | 0.641 | **0.652** |
| Kunrei/Full | 75% | 72% | 72% | **75%** | 0.770 | 0.755 | 0.760 | **0.776** |
| Hepburn/Consonant | 53% | 56% | 59% | **59%** | 0.586 | 0.618 | 0.650 | **0.659** |
| Hepburn/Full | 78% | 78% | 75% | **78%** | 0.786 | 0.792 | 0.781 | **0.797** |

**generated（400文・テンプレ・活用多）**

| プロファイル | id t1 | ch t1 | wd t1 | cw t1 | id MRR | ch MRR | wd MRR | cw MRR |
|---|---|---|---|---|---|---|---|---|
| Kunrei/Consonant | 17% | 21% | **34%** | **36%** | 0.252 | 0.287 | 0.386 | **0.397** |
| Kunrei/Full | 49% | 51% | 55% | 55% | 0.542 | 0.561 | **0.605** | 0.602 |
| Hepburn/Consonant | 15% | 19% | **33%** | **35%** | 0.240 | 0.279 | 0.394 | **0.404** |
| Hepburn/Full | 51% | 52% | 58% | 58% | 0.558 | 0.567 | **0.621** | 0.619 |

## 6. 結論

- **子音条件（本丸）で word > char が両セットの MRR で一貫**。word 単独 MRR: seed Kunrei/Cons 0.641 vs char 0.607、generated 0.386 vs char 0.287。識別すべきは**利得の大きさがセット依存で約4倍ちがう**こと：char 比の上乗せは **seed +3pt / generated +13〜14pt**（top-1）、identity 比の MRR 利得倍率は **seed ~2× / generated ~3.8×**。
  - **generated は最良ケースで典型ではない**：テンプレ生成＝定型コロケーション＝word bigram の得意分野。seg-check が generated で 100% exact（seed 81%）になった同じ規則性が、word LM が generated を稼ぐ理由。実筆寄りの seed（穏当だが確実な +3pt）と templated generated（大きい +13pt）の中間が実打鍵の見込み。**いずれも実打鍵そのものではない**（レイテンシ・実分布は未計測）。
- **word 単独が主役、char は小さな保険**：補間（cw）の上乗せは dev で +0.0016（77文＝ノイズ）。test では主に **char/word 単独が起こした seed Full の退行（id 75/78%→72/75%）を identity 水準へ戻す**働き。「補間がわずかに安全」であって「明確に最良」ではない。
- **利得は全て子音条件**。Full は LM 中立〜やや負（補間で退行は解消するが純利得は無い）。
- 要旨：語 bigram は char より語彙的文脈を捉え、子音入力の曖昧性解消で **char を明確に上回る（穏当〜大）**。char=「頑張って+1pt」より確実に良い。

## 7. 申し送り

1. **runtime 投入はレイテンシ次第（未計測）**：勝ち筋だが、配線判断の前に n=100 の `ConvertNBest`＋2-LM スコアの実測が要る。投入時は App の reranker に char+word 補間（word.bin 6.3MB + char.bin 1.5MB 同梱、`LmReranker` の N-LM コンストラクタ）。word 単独でもほぼ同等＝char 省略も選択肢。
2. **フォールバック 10.4%** の内訳精査（記号・数字・希少漢字・正規化）。下げれば word vocab の質が上がる余地。
3. dev 拡充（77文＝ノイジー、補間の真の上乗せは測れていない）。プロファイル別 λ。
4. segPenalty=3000 は seg-check 一致率で選定。reading 制約が無い surface 分割が安い homograph を拾うのを矯正する knob（reranker の seg=0 とは別物・別用途）。
5. `SurfaceSegmenter` はオフライン一回限りなので `Dictionary+span` のまま（CSR トライ化は不要＝[[compact-dictionary-dsa]] は resident runtime 辞書の話で本ツールは非該当）。

## 再現コマンド

```powershell
# 整合性ゲート（segPenalty を第5引数で掃引）
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/seed.tsv      100 seg-check 3000
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/generated.tsv 100 seg-check 3000
# word コーパス生成 → blob 構築（best λ_bi=0.7, floor=15）
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/lm/corpus.tsv 1 segment-corpus data/lm/corpus_word.tsv 3000
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Lm   -- data/lm/corpus_word.tsv data/lm/word.bin word 0.7 15
# G1（word）/ 補間 joint tune
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/lm/dev.tsv 100 tune data/lm/corpus_word.tsv word
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/lm/dev.tsv 100 tune-interp data/lm/char.bin data/lm/word.bin
# test（id / char / word / char+word）
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/seed.tsv      100 eval-interp data/lm/char.bin data/lm/word.bin 100 300
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/generated.tsv 100 eval-interp data/lm/char.bin data/lm/word.bin 100 300
```
