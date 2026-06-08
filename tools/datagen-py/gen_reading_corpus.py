# /// script
# requires-python = ">=3.11"
# dependencies = ["fugashi>=1.3", "unidic-lite==1.0.8"]
# ///
"""コーパス生文を「読み（ひらがな）」列へ変換する（読みステージの char/モーラ LM 用）。

二段化（子音→かな→漢字）の Stage A（子音→かな）を採点する読み LM を学習するための前処理。
fugashi の kana（カタカナ）→ひらがな。各文を1行のひらがな列として出力（char モードの WordNGramLm が
文字＝モーラ分割して bigram を学習）。非かなトークン（記号・英数）はスキップ＝読みの流れだけを残す。

使い方:
    mise exec -- uv run tools/datagen-py/gen_reading_corpus.py data/lm/corpus.tsv > data/lm/corpus_reading.tsv
"""

import sys
from pathlib import Path

import fugashi

KATA_TO_HIRA = {chr(c): chr(c - 0x60) for c in range(0x30A1, 0x30F7)}


def to_hiragana(kata: str) -> str:
    return "".join(KATA_TO_HIRA.get(c, c) for c in kata)


def is_kana(s: str) -> bool:
    return len(s) > 0 and all("ぁ" <= c <= "ゖ" or c == "ー" for c in s)


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8", newline="\n")
    corpus = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("data/lm/corpus.tsv")
    tagger = fugashi.Tagger()

    lines = 0
    emitted = 0
    for raw in corpus.open(encoding="utf-8"):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        lines += 1
        parts = []
        for w in tagger(line):
            kana = getattr(w.feature, "kana", None)
            if kana:
                hira = to_hiragana(kana)
                if is_kana(hira):
                    parts.append(hira)
        reading = "".join(parts)
        if len(reading) >= 2:
            print(reading)
            emitted += 1

    print(f"{lines:,} 文 → 読み列 {emitted:,} 行", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
