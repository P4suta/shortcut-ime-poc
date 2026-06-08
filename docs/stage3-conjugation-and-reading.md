# 活用合成の穴（精度の主レバー）＋「読み先行」二段化の検証（負の結果）

本書は (1) generated の gold∈n-best を 67%→83% に押し上げた**活用形オフライン語彙化**、(2) ユーザー提案「いったん正確なひらがな変換を」を data-driven に検証し、**直接 word-LM を超えないと結論づけた**記録。プラン: `~/.claude/plans/jiggly-brewing-biscuit.md`。

## Part D：活用合成の穴を埋める（dictionary98.txt）

### 問題（データで確認）
Mozc 辞書は動詞/形容詞の base 形（送る=おくる）しか持たず、活用形が辞書単位に無くラティスで作れない。`misses` サブコマンド（訓令式・フルで gold 圏外を列挙）で確認：

```
✗ 資料を送ってください〔…おくってください〕  top1=資料を多く立てください
```
→ `おくって` が無いため「多く立て」に化ける。generated 400文の **33% が gold 圏外**（gold∈n-best 67% で頭打ち、seed は 81%）。

### 手法：harvest + 再結合（オフライン専用・ランタイム純 C#）
[gen_conjugations.py](../tools/datagen-py/gen_conjugations.py)：コーパス(138,915文)を fugashi(UniDic) で解析。UniDic 短単位が過分割する活用（送っ＋て）を**動詞/形容詞の頭＋後続の助動詞・接続助詞**で1文節へ再結合し、頻度しきい値(≥3)で絞って Mozc 5列形式で出力。[dictionary99.txt](../data/dictionary_oss/dictionary99.txt)（丁寧助動詞の手作り補助辞書）の確立パターンの拡張。

- 読み：fugashi の kana（カタカナ）→ひらがな。出力 **4,339 エントリ**。
- **接続ID（advisor 助言：OOR 0コストの罠を避け in-range）**：左ID＝ベース動詞の左ID（を→動詞が自然）、右ID＝1851（dict99 の語尾と同じ＝ください/文末へ繋がる）。
- コスト＝ベース語コスト＋delta（既定0）。dict99/Mozc 既存の (読み,表層) は出さない。
- 例：`おくって→送って`(cost 2646)、`かった→買った`(1751)。

### 結果（gold∈n-best と top-1）
ゲート順序を厳守：dump 確認 → dict98 追加 → 到達性確認 → corpus 再分割 → seg-check → word.bin 再学習 → 測定。

| 指標 | 追加前 | 追加後 |
|---|---|---|
| generated 圏外（訓令式フル） | 33% | **17%** |
| generated gold∈n-best（フル） | 67% | **83%** |
| seed gold∈n-best（フル） | 81% | **84%** |
| seg-check token recall（generated） | — | **96.8% PASS** |
| word.bin 語彙 | 77,324 | 80,825 |

**リランカー成果（eval-interp, λ_char=50/λ_word=500, n=100）**：

| プロファイル | wd t1 旧 | wd t1 新 |
|---|---|---|
| generated Kunrei/Full | 55% | **69%** |
| generated Hepburn/Full | 58% | **73%** |

seed の純子音 cw は 59%→53% と微減（候補空間拡大の代償）。ただし**純子音(keepRate=0)は MANDATORY 母音の存在から非現実的な動作点**で、実打鍵の中心 mid-spectrum（seed p=0.5 cw=66%）は健全。**意識的な候補空間トレードオフ**として受容。

## Part E：「読み先行」二段化の検証 ＝ 負の結果（直接 word-LM を超えない）

ユーザー提案：「直接漢字は難しいので、いったら正確なひらがな変換を」。data-driven に検証した。

### 測ったこと
- **読み∈n-best は漢字∈n-best より高い**（フルで読み 100% vs 漢字 83%）。「正しい読みの列」はほぼ常に n-best にある。
- だが **二段化の oracle 天井（読みを完璧に当てた最終漢字 top-1）は generated Full で 67%** ＝ **既存の直接 word-LM リランカー 69% を下回る**。理由：読み所与でも同音異字（の vs 之）の選択が残り、それは word-LM が既に扱う領域。
- **読みを STAGE でなく FEATURE に**（advisor 助言：誤り伝播なし・83%天井保持）。[reading.bin](../data/lm/reading.bin)（モーラ char LM）を [LmReranker](../src/ShortcutIme.Core/LmReranker.cs) の第3成分（読み採点 `NegLogProbReading`）として補間し全スペクトラムで測定：

| keepRate | seed Δt1 | generated Δt1 |
|---|---|---|
| Consonant | -3% | -9% |
| p=0.5 | 0% | -3% |
| Full | 0% | 0% |

dev mid でも best λ_reading=0（改善なし）。

### 結論
**読み feature は改善せず、むしろ劣化。理由：表層 word-LM が読み情報を既に内包する**（語の表層→読みは一意なので語列尤度が読み尤度を encode）。「読み∈n と漢字∈n の差」は読みの不確かさでなく同音異字選択で、読みLMでは埋まらない。**重い語-読みLM建設を回避**（advisor の stepped-back 批判が的中）。reading.bin は実験成果物として残すがランタイム不使用。

## Part F：vowelSkipPenalty の再掃引＝カナ/ローマ字層の最後の信号（既に最適）

「子音→一発文章は強引、ローマ字処理を健全に」というユーザー提案を検証。フル入力で `の`(no) が `なお`(nao) に化ける例は**不健全でなく設計どおり**＝`ConsonantEncoder("なお")="no"`（な の a は OPTIONAL、お の o は MANDATORY）で **なお→no は忠実なエンコード**。マッチャを締めると違反0まで監査した忠実性不変条件を壊す（なお語が現実的な省略入力から到達不能に）。これは**ランキング**問題で、レバーは `vowelSkipPenalty`（の=0スキップ、なお=1スキップ）。

`tune-skip` で seed を掃引（cw, λ=50/500）：

| skip\keep | Consonant | p=0.5 | Full |
|---|---|---|---|
| 0 | 53% | 62% | 69% |
| 250 | 53% | 66% | 72% |
| **500（現状）** | **53%** | **66%** | **72%** |
| 1000 | 47% | 62% | 72% |
| 4000 | 28% | 59% | 75% |

**現状 500 が実打鍵の中心 p=0.5 で既に最適**。上げると純子音が崩壊し Full が僅増のみ、下げると mid 劣化。**カナ/ローマ字層はこれで探索し尽くした**（読み先行 stage→読み feature→vowelSkipPenalty の3度の探索が全て「この層は復元可能でボトルネックでない／word-LM が読みを内包」に帰着）。

**なぜユーザー直観が効かないか**：標準IMEのローマ字→カナは決定的だが、本製品は**子音+α→カナが構成上 many-to-many**（圧縮こそ存在理由）。一意な健全カナ層は作れない。**「カナ先行」が真に活きるのは精度でなく UX**＝確定前に top 読み（カナ∈n=100%）を提示してユーザー確認する interface（精度の壁を回避する別軸）。次のフロンティアは reranker 強化（roadmap の LightGBM/より強いLM）＝[[reranking-roadmap]]。

## 確定構成
- トライ＝`ExpandReadingWithHabits`（多方式＋癖＋ー）＋ dictionary98（活用）。
- リランカー＝char+word（読み成分なし）、λ_char=50/λ_word=500（[MainPageViewModel](../src/ShortcutIme.App/ViewModels/MainPageViewModel.cs)）。
- App は dict/ に最新 char.bin/word.bin/dictionary*.txt を要配置、古い trie.bin キャッシュは破棄して再構築（habits/dict98 反映）。

## 再現
```powershell
# 活用辞書生成 → 到達性確認
mise exec -- uv run tools/datagen-py/gen_conjugations.py data/dictionary_oss data/lm/corpus.tsv 3 0 1851 > data/dictionary_oss/dictionary98.txt
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/generated.tsv 100 misses
# corpus 再分割 → seg-check → word.bin 再学習
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/lm/corpus.tsv 1 segment-corpus data/lm/corpus_word.tsv 3000
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/generated.tsv 100 seg-check 3000
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Lm   -- data/lm/corpus_word.tsv data/lm/word.bin word 0.7 15
# 成果測定
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/generated.tsv 100 eval-interp data/lm/char.bin data/lm/word.bin 50 500
# 読み先行の検証（負の結果の再現）
mise exec -- uv run tools/datagen-py/gen_reading_corpus.py data/lm/corpus.tsv > data/lm/corpus_reading.tsv
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Lm   -- data/lm/corpus_reading.tsv data/lm/reading.bin char 0.5 10
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/seed.tsv 100 eval-reading data/lm/char.bin data/lm/word.bin data/lm/reading.bin 50 500 300
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/generated.tsv 100 two-stage   # 二段化天井
```
