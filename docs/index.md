---
---

子音だけ（または子音＋任意の母音）で日本語を高速入力する **入力補助ツール** の設計ドキュメント集です。
中国語ピンインの「簡拼（声母だけ入力 → 候補選択）」を日本語でやる試み。

```
kyy   → 共有 / 許容 …      （子音だけ）
kyou  → 今日 / 共有 …      （母音を足して絞り込み）
hnjt  → 本日               （文節ごとに補完して長文をつなぐ）
```

単発の単語より、**長文をテンポよく書く** ことに価値を置いています。

- リポジトリ: [github.com/P4suta/shortcut-ime-poc](https://github.com/P4suta/shortcut-ime-poc)
- 概要・ビルド手順: [README](https://github.com/P4suta/shortcut-ime-poc/blob/main/README.md)

## 開発ステージ・ドキュメント

実データでの計測に基づき、段階的に積み上げた記録です（各リンクは GitHub 上のレンダリング表示）。

| ステージ | 内容 |
| --- | --- |
| [Step 0: 計測](https://github.com/P4suta/shortcut-ime-poc/blob/main/docs/step0-measurement.md) | 実データでの衝突率・絞り込み効果の計測 |
| [Stage 0: 評価基盤](https://github.com/P4suta/shortcut-ime-poc/blob/main/docs/stage0-eval.md) | 評価ハーネス |
| [Stage 1: N-best](https://github.com/P4suta/shortcut-ime-poc/blob/main/docs/stage1-nbest.md) | N-best 候補生成 |
| [Stage 1: Word LM](https://github.com/P4suta/shortcut-ime-poc/blob/main/docs/stage1-wordlm.md) | 単語 N-gram 言語モデル |
| [Stage 1: リランカー](https://github.com/P4suta/shortcut-ime-poc/blob/main/docs/stage1-reranker.md) | char + word LM によるリランキング |
| [Stage 2: 現実的入力](https://github.com/P4suta/shortcut-ime-poc/blob/main/docs/stage2-realistic-input.md) | 訓令式／ヘボン式の混在入力対応 |
| [Stage 3: 活用と読み](https://github.com/P4suta/shortcut-ime-poc/blob/main/docs/stage3-conjugation-and-reading.md) | 活用語彙化・読み格子 |
| [Stage 4: LightGBM ランカー](https://github.com/P4suta/shortcut-ime-poc/blob/main/docs/stage4-lightgbm-ranker.md) | 学習ランカーの検討（負の結果含む） |
| [Stage 5: 逐次文節確定](https://github.com/P4suta/shortcut-ime-poc/blob/main/docs/stage5-incremental-commit.md) | 逐次確定＋左文脈レジーム |

> 辞書データ（Mozc `dictionary_oss`）と LM コーパスはライセンス・サイズの都合でリポジトリには含めず、
> オフラインで取得・生成します。詳細は各ドキュメントを参照してください。
