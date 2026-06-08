# Stage 2（レジーム変更）：一発文変換 → 逐次文節確定＋左文脈

LM スコア並べ替え層を4回潰した後（`docs/stage4-lightgbm-ranker.md`）、残る最大の伸びしろは advisor 曰く「一発で文全体を変換するレジームそのもの」。本書は **一発全文 → 逐次文節確定（確定左文脈を前送り）** へ変えた記録。プラン: `~/.claude/plans/jiggly-brewing-biscuit.md`。

## 結論（先に）
- **無人 top-1（単一文）は逐次でも cw を超えない**＝構造上当然（逐次の左文脈は全文 Viterbi が既に使う情報の部分集合・全文は右文脈も見る）。look-ahead greedy ≈ cw を実測で確認。真の無人レバー（文間文脈）は手元に無い複数文データが要る＝スコープ外。
- **入力アシストの実用指標＝候補UI（各文節 top-5 から選ぶ）で大勝**：per-step gold∈top-5 **98〜100%**、全 step 命中（文節ごとに選んで正文を組める）**94〜100%**。一発 cw top-1（65〜85%）を大きく上回る。
- **look-ahead が必須**：素朴な単一セグメント逐次は myopia（長い文節が短断片に負ける）で候補UI 7%。残り入力を再パースして先頭文節を採る look-ahead で 94〜100% に。

## 設計（Core 純 C#・既存再利用）
- **継続採点** [WordNGramLm.NegLogProbContinuation](../src/ShortcutIme.Core/WordNGramLm.cs)：確定左文脈を prev に積み、EOS を踏まず次文節を採点（＝leftContext を実信号にする本体）。恒等式/左文脈効果をテスト。
- **次文節 seam** [INextSegmentSource](../src/ShortcutIme.Core/IncrementalConverter.cs)：
  - [IncrementalConverter](../src/ShortcutIme.Core/IncrementalConverter.cs)（単一セグメント・素朴版＝baseline。myopia あり）。
  - [LookaheadConverter](../src/ShortcutIme.Core/LookaheadConverter.cs)（**本命**）：各 step で残り入力を [PhraseConverter.ConvertNBest](../src/ShortcutIme.Core/PhraseConverter.cs) で再パースし、全経路を「経路コスト＋確定左文脈で条件付けた cw」で採点、その**先頭文節**を候補に。先読みが myopia を解消、継続 LM が左文脈を加える。
- **位置整合**：[Hypothesis](../src/ShortcutIme.Core/Hypothesis.cs) に `SegmentLengths`（各文節の入力長＝ConvertNBest が計算済みで捨てていた情報）を追加し、gold 文節の<b>正確な長さ</b>で前進＝母音スキップ変異の長さ曖昧による desync を排除（これが無いと per-step が偽の低値になる）。
- **評価** [IncrementalSimulator](../src/ShortcutIme.Evaluation/IncrementalSimulator.cs)：`RunGreedy`（無人 top-1 commit）/`RunOracleTopK`（候補UI：各 step で gold∈top-k、gold 長で前進・cascade 無し）。`incremental` サブコマンド（tools/ShortcutIme.Eval）。

## 測定（訓令式・n=100・cw λ50/500・segPen=3000・対象=gold∈whole-n-best）
| セット・keepRate | 一発 cw top-1 | 逐次 greedy(無人) | **候補UI 全step命中** | per-step | 対象/全 |
|---|---|---|---|---|---|
| seed p=0.25 | 79% | 83% | **96%** | 98% | 24/32 |
| seed p=0.5 | 85% | 85% | **96%** | 99% | 27/32 |
| seed p=0.75 | 85% | 85% | **100%** | 100% | 27/32 |
| generated p=0.5 | 65% | 62% | **94%** | 99% | 271/400 |
| （対照）seed p=0.5 single | — | 0% | 7% | 46% | — |

- **無人 greedy ≈ cw**（時に僅かに上＝look-ahead が first-bunsetsu で稀に有利）。単一文では予想どおり超えない。
- **候補UI 94〜100% / per-step 98〜100%**＝文節ごとに top-5 を見せれば、ユーザーはほぼ確実に正文を組める。全集合換算の下界は seed ≈ 0.84×0.96=81%、generated ≈ 0.68×0.94=64%（一発 cw 換算 71%/44%）＝**実アシストで +10〜20pt**。

## App 配線（製品化・要実機検証）
[MainPageViewModel](../src/ShortcutIme.App/ViewModels/MainPageViewModel.cs) を全文一括から**逐次候補UI**へ変更：読みを打つ→`LookaheadConverter`（segPen=3000・char+word）で先頭文節候補→Enter/ダブルクリックで確定→左文脈に積み残り入力の次文節候補へ更新→消費し切ったら入力欄クリア。`InputConsumed` で view がクリア判断（確定中は読みを保持）。ビルド緑（App 含む 0 警告）。**WinUI ランタイムは本環境で起動不可＝実機での操作確認は要**（ロジックは IncrementalSimulator のテストで担保）。

## 絶対指標（全ケース基準・task3）
`incremental` 出力に「対象（gold∈n-best）」と「全（圏外も分母）」の両方を表示。generated p=0.5：候補UI **対象 94% / 全 64%**（一発 cw top-1 対象65%/全44%）＝全集合で **+20pt**。seed p=0.5：候補UI 全 81%（cw 全71%）。「全」基準も per-step 列挙が全文 n-best 超えで拾う上振れは未計上＝下界。

## 語彙拡大は negative（task2・min_freq）
活用辞書 dictionary98 の min_freq を 3→2 にして再生成（4,339→6,430）しても、**seed/generated の構造欠落は不変**（seed16%/gen26%、同じ例「〜送ります」「変更してください」等）。これらは livedoor ニュースに乏しい業務メール調の語＝**コーパス領域ミスマッチ**で、harvest の深さ（min_freq）では埋まらない。pollution が増えるだけなので min_freq=3 に復帰。真の vocab lever は**領域一致コーパス**（別タスク）、組合せ beam 由来分は逐次 per-step 列挙が拾う。

## 含意・申し送り
- **入力アシストの正しい UX＝逐次候補確定**：本データで構造的に効く（App 配線済み）。実機で操作確認し、確定中の読み表示（消費済み prefix のグレーアウト等）を磨くと良い。
- **さらなる上振れ（未計測）**：候補UIの分母は gold∈whole-sentence-n-best。per-step 列挙は全文 n-best の beam が無いため、**全文 n-best が落とした文節も拾える**（Step0 で見た「組合せ beam 由来の構造欠落」）。独立に gold 文節分割を与えれば候補UI天井はさらに上がる見込み。
- **無人精度の真フロンティアはモデルでなくデータ/評価**：文間 leftContext（確定済み左文＝今は単一文評価で測れない）＋実打鍵キーログ。これが揃って初めて Transformer 等が正当化（[[reranking-roadmap]]）。
- Step4（PhraseConverter と IncrementalConverter の emission 共有ヘルパ抽出）は、本命の look-ahead が PhraseConverter を直接再利用するため重複は IncrementalConverter（baseline）の数行のみ＝保留。

## 再現
```
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/seed.tsv 100 incremental data/lm/char.bin data/lm/word.bin 50 500 5 0.5 0 3000 lookahead
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/generated.tsv 100 incremental data/lm/char.bin data/lm/word.bin 50 500 5 0.5 0 3000 lookahead
# 対照（素朴単一セグメント）: 末尾を single に
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/seed.tsv 100 incremental data/lm/char.bin data/lm/word.bin 50 500 5 0.5 0 3000 single
```
