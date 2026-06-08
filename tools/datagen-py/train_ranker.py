# /// script
# requires-python = ">=3.11"
# dependencies = ["lightgbm>=4.0", "numpy"]
# ///
"""LightGBM lambdarank で n-best 並べ替えランカーを学習する（Stage 1.5）。

入力: gen-train が出した <base>.tsv（label<TAB>f0..f11）と <base>.group（群サイズ/行）。
出力: ranker.txt（LightGBM text モデル＝純C#評価器が読む）と ranker_check.tsv（C#評価器のクロスチェック用）。
群末尾 ~10% を valid に回し ndcg@1 で early stopping。binary relevance（gold=1）。

使い方:
    mise exec -- uv run tools/datagen-py/train_ranker.py data/lm/ranker_train data/lm/ranker.txt
"""

import sys
from pathlib import Path

import lightgbm as lgb
import numpy as np


def main() -> int:
    base = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("data/lm/ranker_train")
    out = Path(sys.argv[2]) if len(sys.argv) > 2 else Path("data/lm/ranker.txt")
    rows_path = Path(str(base) + ".tsv")
    group_path = Path(str(base) + ".group")

    data = np.loadtxt(rows_path, delimiter="\t", dtype=np.float64)
    y = data[:, 0].astype(np.int32)
    x = data[:, 1:]
    groups = np.loadtxt(group_path, dtype=np.int64).reshape(-1)

    # cwScore = cost + 50·charLP + 500·wordLP（製品 cw のスコア）と群相対 cwScoreMinusBest を派生列として追加。
    # C# RankingFeatures と同一式（gen-train 再生成を避けるため既存12列から計算）。
    if x.shape[1] == 12:
        cw = x[:, 0] + 50.0 * x[:, 1] + 500.0 * x[:, 2]
        cw_minus = np.empty_like(cw)
        off = 0
        for g in groups:
            seg = cw[off:off + g]
            cw_minus[off:off + g] = seg - seg.min()
            off += int(g)
        x = np.column_stack([x, cw, cw_minus])
    print(f"rows={len(y):,}, features={x.shape[1]}, groups={len(groups):,}, positives={int(y.sum()):,}", file=sys.stderr)

    # 群境界で train/valid 分割（valid=末尾10%群）。行は群順に並んでいる前提（gen-train がそう書く）。
    n_valid_groups = max(1, len(groups) // 10)
    n_train_groups = len(groups) - n_valid_groups
    train_rows = int(groups[:n_train_groups].sum())

    x_train, y_train, g_train = x[:train_rows], y[:train_rows], groups[:n_train_groups]
    x_valid, y_valid, g_valid = x[train_rows:], y[train_rows:], groups[n_train_groups:]

    dtrain = lgb.Dataset(x_train, label=y_train, group=g_train, free_raw_data=False)
    dvalid = lgb.Dataset(x_valid, label=y_valid, group=g_valid, reference=dtrain, free_raw_data=False)

    # 単調性制約：コスト・負対数尤度が増えるほどスコアは下がる（gold は低コスト/低 NLP）。
    # 関係は本質的に単調なので、cw 的な単調関数へ正則化し汎化を上げる。長さ/文節数は中立(0)。
    # 特徴順: cost,charLP,wordLP,readingLP,segCount,surfaceLen,readingLen,avgWordCost,
    #         costMinusBest,charLPMinusBest,wordLPMinusBest,readingLPMinusBest
    monotone = [-1, -1, -1, -1, 0, 0, 0, -1, -1, -1, -1, -1, -1, -1]
    params = {
        "objective": "lambdarank",
        "metric": "ndcg",
        "ndcg_eval_at": [1, 3],
        "learning_rate": 0.05,
        "num_leaves": 31,
        "min_data_in_leaf": 50,
        "feature_fraction": 0.9,
        "bagging_fraction": 0.9,
        "bagging_freq": 1,
        "max_position": 10,
        "monotone_constraints": monotone,
        "verbosity": -1,
    }

    booster = lgb.train(
        params,
        dtrain,
        num_boost_round=800,
        valid_sets=[dvalid],
        callbacks=[lgb.early_stopping(60), lgb.log_evaluation(50)],
    )

    booster.save_model(str(out), num_iteration=booster.best_iteration)
    print(f"saved: {out}  (best_iteration={booster.best_iteration}, trees={booster.num_trees()})", file=sys.stderr)

    # 特徴重要度（gain）。
    names = ["cost", "charLP", "wordLP", "readingLP", "segCount", "surfaceLen", "readingLen",
             "avgWordCost", "costMinusBest", "charLPMinusBest", "wordLPMinusBest", "readingLPMinusBest",
             "cwScore", "cwScoreMinusBest"]
    gain = booster.feature_importance(importance_type="gain")
    order = np.argsort(gain)[::-1]
    print("feature gain:", file=sys.stderr)
    for i in order:
        print(f"  {names[i]:20} {gain[i]:.1f}", file=sys.stderr)

    # クロスチェック用：valid 先頭20行の (features, 予測スコア) を出力。C#評価器が一致するか検証する。
    check = x_valid[:20] if len(x_valid) >= 20 else x[:20]
    preds = booster.predict(check, num_iteration=booster.best_iteration)
    check_path = Path(str(out).rsplit(".", 1)[0] + "_check.tsv")
    with check_path.open("w", encoding="utf-8", newline="\n") as f:
        for row, p in zip(check, preds):
            f.write("\t".join(repr(float(v)) for v in row) + "\t" + repr(float(p)) + "\n")
    print(f"cross-check: {check_path}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
