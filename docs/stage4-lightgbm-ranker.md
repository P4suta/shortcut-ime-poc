# Stage 1.5 LightGBM ランカー ＝ ML 最低勝利条件は不達（明確な負の結果）

[[reranking-roadmap]] の **Stage 1.5（LightGBM Ranker、★ML 最低勝利条件。これを超えなければ Transformer 不採用）** を完全に実装・評価した記録。結論：**LightGBM ランカーは cw（char+word 補間）を超えられず、ゲート不達**。ただし「ML が効かない」ではなく **(1) 新しい信号がない・(2) 合成データ転移の失敗** という、Transformer を正しくゲートしつつ次に何が要るかを示す結果。

## アーキテクチャ（美しさ優先：Core は純 C# のまま）
- 学習＝Python `lightgbm` lambdarank（オフライン、[[mecab-offline-tooling-allowed]]）→ `booster.save_model` の **text モデル `ranker.txt`**。
- 推論＝**純 C# の GBDT 評価器** [GradientBoostedTrees](../src/ShortcutIme.Core/GradientBoostedTrees.cs)（model.txt をパースし葉値総和。外部依存ゼロ）＋ [RankingFeatures](../src/ShortcutIme.Core/RankingFeatures.cs)（学習/推論で同一の特徴抽出＝train/serve skew 防止）＋ [LgbmReranker](../src/ShortcutIme.Core/LgbmReranker.cs)（IReranker）。
- **正当性クロスチェック**：`lgbm-check` で C# 評価器 vs Python 予測の**最大絶対差 = 0.000（PASS）**。パーサ/評価器は厳密一致。

## 学習データ（リーク防止・忠実）
- [gen_train_sentences.py](../tools/datagen-py/gen_train_sentences.py)：corpus を句読点除去/節分割し清浄（漢字/かな）な文を fugashi の `kana`（綴り読み＝は/を/へ は写し、発音 pron とは別＝助詞問題なし）で読み付与。seed/generated/dev と素（リーク防止）。全文寄り（平均15.8字）。
- `gen-train` サブコマンド：各 (文×keepRate{0,0.5,1}) を1群、各仮説に RankingFeatures＋gold ラベル（gold∈n-best の群のみ）。全文版で **8,207 群 / 82万行 / 正例14,710**。
- 特徴14：cost, charLP, wordLP, readingLP, segCount, surfaceLen, readingLen, avgWordCost, ＋群相対(MinusBest)4, ＋ **cwScore=cost+50·charLP+500·wordLP** と cwScoreMinusBest。

## 結果（eval-lgbm：LightGBM vs cw、訓令式 n=100）
valid ndcg@1 は学習分布(corpus)で 0.81〜0.84 と高いが、**held-out の seed/generated で cw に一貫して負ける**：

| keepRate | seed cw t1 | seed lgbm t1 | Δ | generated cw t1 | generated lgbm t1 | Δ |
|---|---|---|---|---|---|---|
| Consonant | 53% | 53% | 0 | 29% | 23% | -7 |
| p=0.5 | 66% | 59% | **-6** | 52% | 46% | -5 |
| Full | 72% | 62% | **-9** | 69% | 65% | -4 |

**4構成すべてで負け**：短節学習／全文学習／＋単調性制約（cost/NLP 増→スコア減）／＋**cwScore を特徴に投入**。cw スコアを入力として渡しても超えられない（木が連続 cw スコアを離散化＝ビン化し合成分布へ過適合、厳密な線形 cw の連続順序に勝てない）。特徴重要度は wordLPMinusBest/charLPMinusBest が支配、readingLP は ≈0（読みLM負の結果と整合）。

## 結論の framing（重要・advisor の stepped-back 批判）
公正な勝負だった（LightGBM の方が特徴も学習データも容量も多く、単調制約も cw を表現可能で、**むしろ有利**）。それでも負けた＝**強い負の結果**。意味は2つ：

1. **新しい信号がない**：特徴は cw のスコアそのもの＋死んだ readingLP。天井ギャップの残差は同音異字/文脈（の vs 之）で、**4特徴のどれもこの tie を切れない**。これは LM スコア層の**4度目の探索**（読み先行 stage→読み feature→vowelSkipPenalty→LightGBM）＝同じ壁。
2. **合成データ転移の失敗（本質）**：柔軟なモデルが**合成学習分布(corpus)に過適合**し、2パラメータの cw（過適合できない）に汎化で負けた。これは roadmap の**最大リスク「モデルより合成データ学習が実打鍵分布に乗るか」が具体的証拠として顕在化**したもの。Transformer はパラメータが多く**もっと過適合する**＝ゲート論理は成立するが、真の blocker は**モデル種でなくデータ realism**。

## 申し送り（次フロンティア＝モデルでなくデータ/評価）
- **未活用の唯一の実信号＝`leftContext`（確定済み左文脈）**：全リランカーで `_ = leftContext` と無視。確定文脈は今まで試した全リランカーに欠けている実信号で、単文評価では測れない。**実打鍵データ＋複数文の評価**が揃って初めて活きる。これと realism が真のフロンティア（どちらもデータ/評価の問題）。
- **scaffolding は残す**：GradientBoostedTrees/RankingFeatures/LgbmReranker/gen-train/train_ranker は、実打鍵データが手に入った時の harness として温存（テスト緑・App 未配線）。
- 本番は **cw（char+word, λ=50/500）のまま**。今セッションの確定的勝ちは活用辞書（gen Full 55→69%、gold∈n 67→83%）＝直接 cw アーキの上。

## 再現
```powershell
mise exec -- uv run tools/datagen-py/gen_train_sentences.py data/lm/corpus.tsv 12000 > data/lm/train_sentences.tsv
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/lm/train_sentences.tsv 100 gen-train data/lm/char.bin data/lm/word.bin data/lm/reading.bin data/lm/ranker_train 0
mise exec -- uv run tools/datagen-py/train_ranker.py data/lm/ranker_train data/lm/ranker.txt
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- _ data/lm/ranker.txt 100 lgbm-check data/lm/ranker_check.tsv          # パーサ正当性（差0）
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/seed.tsv 100 eval-lgbm data/lm/char.bin data/lm/word.bin data/lm/reading.bin data/lm/ranker.txt 50 500
```
