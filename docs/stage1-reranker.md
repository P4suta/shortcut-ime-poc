# Stage 1（Phase 1-2）: リランカー測定基盤と安価な伸びしろの検証

Stage 0 の n-best 基盤に対し、リランカー seam を eval パスへ接続し、**天井（gold∈n-best）の正体**と**安価ヒューリスティック（文節数 prior）の有効性**を測定した記録。
実装＝[IReranker](../src/ShortcutIme.Core/IReranker.cs) seam を [tools/ShortcutIme.Eval/Program.cs](../tools/ShortcutIme.Eval/Program.cs) に配線（`args[3]`＝`identity|oracle|length`）。
追加リランカー＝[OracleReranker](../src/ShortcutIme.Evaluation/OracleReranker.cs)（評価専用・Evaluation 側）、[LengthPriorReranker](../src/ShortcutIme.Core/LengthPriorReranker.cs)（Core）。

テスト: Core 93 / Evaluation 25 緑（+6）。`TreatWarningsAsErrors` 下で 0 警告。

---

## 1. ベースライン確認（identity, seed, n=20）＝リグレッションなし

| プロファイル | top-1 | top-5 | top-10 | gold∈n-best | MRR |
|---|---|---|---|---|---|
| Kunrei/Consonant | 50% | 66% | 66% | 69% | 0.572 |
| Kunrei/Full | 75% | 78% | 81% | 81% | 0.770 |
| Hepburn/Consonant | 53% | 62% | 72% | 72% | 0.586 |
| Hepburn/Full | 78% | 78% | 81% | 81% | 0.786 |

Stage 0 の文書値（Kunrei/Consonant 50%/69% ほか）と完全一致。`IdentityReranker` は WFST コスト順を素通しするため、seam 接続は出力を変えない（**リグレッションガード合格**）。

## 2. G0: seam 配線の正しさ（oracle, seed, n=20）

oracle は gold が n-best 内にあれば先頭へ繰り上げる。**oracle top-1 が identity の gold∈n-best と厳密一致**すれば配線は正しい。

| プロファイル | identity gold∈n-best | oracle top-1 | 判定 |
|---|---|---|---|
| Kunrei/Consonant | 69% | 69% | ✅ |
| Hepburn/Consonant | 72% | 72% | ✅ |
| Kunrei/Full | 81% | 81% | ✅ |
| Hepburn/Full | 81% | 81% | ✅ |

全プロファイルで一致（seed に入力衝突なし）。**G0 合格**＝リランカーを差し替えれば確かに top-1 が動く配線になっている。

## 3. n-best スイープ＝天井カーブ（identity, gold∈n-best）

### 3a. seed（32文・丁寧長文）

| プロファイル | n=20 | n=100 | n=200 |
|---|---|---|---|
| Kunrei/Consonant | 69% | **81%** | 81% |
| Hepburn/Consonant | 72% | 78% | **81%** |
| Kunrei/Full | 81% | 81% | 81% |
| Hepburn/Full | 81% | 81% | 81% |

### 3b. generated（400文・テンプレ。より大きく公平＝検証用）

| プロファイル | n=20 | n=100 | n=200 | n=500 |
|---|---|---|---|---|
| Kunrei/Consonant | 33% | 50% | 58% | **63%** |
| Hepburn/Consonant | 34% | 51% | 57% | **63%** |
| Kunrei/Full | 67% | 67% | 67% | 67% |
| Hepburn/Full | 67% | 67% | 67% | 67% |

top-1 は n に依らずほぼ不変（seed では完全不変、generated でも 17-18%）。動くのは主に天井（と、generated では top-5/top-10 も少し）。

**構造的発見（seed の主張を generated で検証して訂正）:**
- **子音天井は n とともに Full 天井へ収束する**＝子音ペナルティは本質的な到達不能ではなく、**主にビーム深さ／順位の問題**。これは両セットで成立。
- **ただし収束に要する n はセット依存**: seed は n≈100 で子音=Full=81% に一致。generated は遅く、n=500 で 63%（Full 67% まで残り約4pt、まだ微増中）。→ **「n≈100 で完全回収」は seed 特有の楽観**。大きい公平なセットでは **n≈200–500** を要し、わずかな残差も残る。
- **Full 天井そのものがセットで違う**（seed 81% vs generated 67%）＝generated は活用形が多く**到達不能の壁（活用/辞書単位）が大きい**。これがハードなセットでの支配的な上限で、リランカー範囲外（Phase 3a の MeCab 活用語彙化＝G2 到達不能側の対象）。

→ **伸びしろは「n を上げる（天井↑）×リランカー（top-1 を天井へ）」の積**。Phase 3 の LM は **n≈100–200 を起点に、ceiling↔レイテンシのトレードオフを実測**して運用 n を決める（n=500 は天井を稼ぐが重い。レイテンシ未計測）。

## 4. length prior（文節数 prior）＝測定上の伸びしろなし（負の結果）

仮説：`segmentPenalty=0` が文節数を未価格にするため、過分割を抑える prior で安価に top-1 が取れるはず。**dev=generated でチューニング → test=seed で報告**（リーク防止）。

dev（generated, n=20, Kunrei/Consonant）:

| penalty | top-1 | MRR |
|---|---|---|
| identity | 18% | 0.234 |
| 1500 | 17% | 0.224 |
| 3000 | 14% | 0.204 |
| 6000 | 14% | 0.203 |

penalty を上げるほど単調に悪化。**dev 最適は penalty≈0（＝prior を採用しない）**。test（seed, n=20, Kunrei/Consonant）でも追認：

| penalty | top-1 | top-5 | MRR |
|---|---|---|---|
| identity | 50% | 66% | 0.572 |
| 1500 | 50% | 59% | 0.551 |
| 3000 | 47% | 56% | 0.521 |
| 6000 | 44% | 56% | 0.506 |

最小 penalty で top-1 据え置き、それ以外は top-1・top-5・MRR とも悪化（全プロファイルで同様）。

**結論**: WFST コスト（unigram＋連接）が既に分割を十分価格付けしており、過分割は自然にコスト高で沈む。素朴な文節数 prior は**正しい多文節文（seed の丁寧長文）まで巻き添えで沈める**ため有害。安価ヒューリスティックでの top-1 向上は無い。top-1 を上げる唯一の道は**新規信号（Phase 3 の語 bigram LM）**。

> `LengthPriorReranker`（および Eval の `length` オプション）は**この測定で否定されたため tree から撤去**した。負の結果は本節の数値が記録（git 履歴から復元可能）。Eval の選択肢は `identity` / `oracle` のみ。`OracleReranker` は天井を測る診断器として保持。

## 5. char n-gram LM リスコアラ（Phase 3）＝子音条件で top-1 改善（正の結果）

length prior（§4）が否定した後の唯一の道＝**新規信号（語/文字 bigram LM）**。`Hypothesis.Cost`（WFST＝POS-bigram 内包）に `λ·NegLogProb` を足す純加算項で、**λ=0 がそのままベースライン**（length prior と同型の自己校正）。

### 5.1 語 vs 文字の決定（vocab-dump 診断・LM 構築前）
LM を作らず、seed gold を `ConvertNBest` に通した **Mozc 文節境界**（`vocab-dump`）と **fugashi 分かち書き境界**（`tools/datagen-py/segment.py`）を 32 文で目視比較した。fugashi（unidic 短単位）が Mozc 文節より一貫して **1.5〜5倍過分割**（例: 「よろしくお願いします」＝Mozc 1 文節 vs fugashi 5 語、「ありがとうございます」＝Mozc 1 vs fugashi 3）。

→ fugashi コーパスで学習した **word vocab には Mozc 文節候補が存在せず OOV floor だらけ**になる。よって **char を plan A**（文字単位は語境界不変で OOV ゼロ）。`WordNGramLm` は `TokenMode.Word/Char` 両対応で実装し、char で測定。

### 5.2 実装（純ランタイム Core）
- [WordNGramLm](../src/ShortcutIme.Core/WordNGramLm.cs): BOS/EOS 境界（EOS 項が文長公平性を担保）、**三段バックオフ**（tier1 観測 bigram／tier2 未観測＝`(1−λ_bi)·unigram`／tier3 真 OOV＝floor）、確率空間で補間してから log を事前計算、CSR（`_biRowStart`/`_biNextId` 行内昇順＝二分探索/`_biLogP`）、float 格納・double 累積、Magic `"SILM"` 直列化（[RomajiTrie](../src/ShortcutIme.Core/RomajiTrie.cs) の規律踏襲）。
- [LmReranker](../src/ShortcutIme.Core/LmReranker.cs): `IReranker`、`OrderBy(h.Cost + λ·NegLogProb)` の安定ソート（λ=0=identity）。表層しか見ないため方式/母音レベル非依存。
- ツール: build＝[ShortcutIme.Lm](../tools/ShortcutIme.Lm)（corpus→blob、Core のみ依存）。Eval に `vocab-dump`/`tune`/`eval-lm`/`lm` を追加。
- テスト: Core 107 件緑（tier2≠floor／OOV=floor／BOS·EOS 公平性／Save-Load round-trip／Word·Char 両モード）。0 警告。

### 5.3 コーパス & G1（dev チューニング）
コーパス＝livedoor ニュース（[build_corpus.py](../tools/datagen-py/build_corpus.py)、mise の uv／PEP723）: **corpus 138,923 文**（char 生文）/ **dev 77 文**（`表層⇥読み`、読みは fugashi `kana`→ひらがな、長音符ー・非かなを含む文は除外＝[[eval-input-must-be-faithful]]）。train/dev は SHA1 決定的分割、seed/generated と重複排除。char vocab=3,624、distinct-bigrams=182,725。CC BY-ND のため `data/lm/` は gitignore＋決定的再生成。

tune（dev のみ・n-best キャッシュ・Kunrei/Consonant）:

| | MRR |
|---|---|
| baseline（λ=0＝POS-bigram のみ） | 0.1278 |
| best（λ_bi=0.5, floor=10, **λ=100**） | **0.1405** |

**G1 PASS**（dev 最適 λ>0 で改善）。ただし dev=77 文は小さく改善は弱い兆候＝test で確認。best が grid 端寄り（λ_bi/floor/λ とも最小側）。

### 5.4 test 測定（凍結 λ=100, n=100、identity vs char LM）

seed（32 文・丁寧長文）:

| プロファイル | id top1 | lm top1 | id MRR | lm MRR |
|---|---|---|---|---|
| Kunrei/Consonant | 50% | **56%** | 0.574 | **0.607** |
| Kunrei/Full | 75% | 72% | 0.770 | 0.755 |
| Hepburn/Consonant | 53% | **56%** | 0.586 | **0.618** |
| Hepburn/Full | 78% | 78% | 0.786 | 0.792 |

generated（400 文・テンプレ・活用多）:

| プロファイル | id top1 | lm top1 | id MRR | lm MRR |
|---|---|---|---|---|
| Kunrei/Consonant | 17% | **21%** | 0.252 | **0.287** |
| Kunrei/Full | 49% | **51%** | 0.542 | **0.561** |
| Hepburn/Consonant | 15% | **19%** | 0.240 | **0.279** |
| Hepburn/Full | 51% | **52%** | 0.558 | **0.567** |

### 5.5 結論
- **子音条件（実打鍵に近い＝本プロジェクトの本丸）で char LM が一貫して top-1 +3〜6pt / MRR 改善**（seed・generated 両方）。length prior（§4・負）と対照的な**正の結果**。
- generated は全プロファイル改善。seed の Full のみ微減（既に高精度で LM が稀に正解を崩す＝子音最適 λ が Full にはやや強い）。
- char LM は POS-bigram（WFST 内包）の上に**新規信号**を載せ、子音入力の曖昧性解消に効く＝Phase 3 の核を実証。
- **青空文庫 ablation（実施済み・中立）**: 新字新仮名（P4suta リポジトリのファイル名「（新字新仮名）」で機械選別）**15,004 文**を corpus に追加（[build_corpus.py](../tools/datagen-py/build_corpus.py) の `--aozora`）→ dev best MRR **0.1405→0.1403（中立・微減）**。char vocab は livedoor で既にカバー済み＋青空の文学ドメインが dev（ニュース調）とずれるため上乗せなし。**→ livedoor 単独を採用**。
- 申し送り: ① dev が小さい（77 文・faithful フィルタが厳しい）＝dev 拡充の余地。② grid 端に best＝λ_bi/floor をさらに下げる余地。③ Full 条件は子音最適 λ で微減＝**プロファイル別 λ** の余地。④ レイテンシ未計測。

---

## 判断ゲートの状態

- **G0**（seam 配線）: ✅ 合格（§2）。
- **G1**（語 bigram が dev MRR を POS-bigram 超えで改善か）: Phase 3 で判定。
- **G2**（gold は n-best 内だが誤順位 vs 到達不能）: §3 より、**到達不能の壁はセット依存**＝seed ~19%（Full 天井 81%）／generated ~33%（Full 天井 67%、活用形が多く壁が厚い）。それより上は順位問題＝リランカーで回収可能。

## Phase 3 への申し送り

1. **運用 n は n≈100–200 を起点に ceiling↔レイテンシを実測して決める**。n を上げるほど天井↑（generated は n=500 で +30pt）だが重い。レイテンシ未計測。
2. **top-1 向上は LM が本丸**。length prior 等の安価ヒューリスティックは打ち止め（測定で否定済み）。
3. **到達不能の壁（seed ~19% / generated ~33%）は別問題**＝Phase 3a（MeCab 忠実データ／活用語彙化）。リランカーとは独立に天井そのものを上げる。generated 側の厚い壁＝活用形なので、ここが最も効く。

## 再現コマンド

```powershell
# ベースライン（identity）／G0（oracle）
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/seed.tsv 20 identity
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/seed.tsv 20 oracle
# 天井スイープ（seed と generated）
foreach ($n in 20,100,200)     { mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/seed.tsv      $n identity }
foreach ($n in 20,100,200,500) { mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/generated.tsv $n identity }
# ※ length prior（負の結果, §4）はリランカー撤去済み。再現は git 履歴から。

# §5 char LM: 診断 → コーパス生成（mise の uv）→ build → tune(G1) → eval-lm(test)
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/seed.tsv 100 vocab-dump
mise exec -- uv run tools/datagen-py/build_corpus.py            # data/lm/corpus.tsv, dev.tsv（--aozora で青空 ablation）
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Lm -- data/lm/corpus.tsv data/lm/char.bin char 0.5 10
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/lm/dev.tsv 100 tune
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/seed.tsv      100 eval-lm data/lm/char.bin 100
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/generated.tsv 100 eval-lm data/lm/char.bin 100
```
