# /// script
# requires-python = ">=3.11"
# dependencies = ["fugashi>=1.3", "unidic-lite==1.0.8"]
# ///
"""seed.tsv の gold 文を fugashi(unidic-lite) で分かち書きし、Mozc 文節境界との目視比較に使う。

語 LM（word）を選ぶには「Mozc 文節境界（vocab-dump 出力）」と「fugashi 分かち書き境界（本スクリプト）」が
概ね一致している必要がある。乖離が大きければ word bigram は当たらず、char bigram を plan A にする。

使い方:
    mise exec -- uv run tools/datagen-py/segment.py [data/eval/seed.tsv]
"""

import sys
from fugashi import Tagger


def main() -> None:
    path = sys.argv[1] if len(sys.argv) > 1 else "data/eval/seed.tsv"
    tagger = Tagger()
    with open(path, encoding="utf-8") as handle:
        for raw in handle:
            line = raw.rstrip("\n")
            if not line or line.startswith("#"):
                continue
            surface = line.split("\t")[0]
            words = list(tagger(surface))
            joined = " / ".join(word.surface for word in words)
            print(f"gold: {surface}")
            print(f"  fugashi: {joined}   ({len(words)}語)")


if __name__ == "__main__":
    main()
