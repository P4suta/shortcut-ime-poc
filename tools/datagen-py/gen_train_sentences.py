# /// script
# requires-python = ">=3.11"
# dependencies = ["fugashi>=1.3", "unidic-lite==1.0.8"]
# ///
"""LightGBM ランカー学習用の (表層<TAB>読み) を corpus から生成する（seed/generated/dev と素・忠実）。

子音→文の reranking を学ぶには (文, 読み) の大規模・現実的・gold 到達可能な集合が要る。corpus 生文を
句読点で清浄な節に分割し、漢字/かなのみの節だけ採り、fugashi の kana（綴り読み＝は/を/へ は写し）で読み付与する。
評価セット（seed/generated/dev）の表層と重複する節は除外（リーク防止）。出力は決定的（corpus 出現順）。

使い方:
    mise exec -- uv run tools/datagen-py/gen_train_sentences.py data/lm/corpus.tsv 12000 > data/lm/train_sentences.tsv
"""

import re
import sys
from pathlib import Path

import fugashi

KATA_TO_HIRA = {chr(c): chr(c - 0x60) for c in range(0x30A1, 0x30F7)}
SPLIT = re.compile(r"[、。，．！？!?「」『』（）()\[\]【】・…〜~\s　:：;；/／\-—]+")


def to_hiragana(kata: str) -> str:
    return "".join(KATA_TO_HIRA.get(c, c) for c in kata)


def is_clean_surface(s: str) -> bool:
    # 漢字・ひらがな・カタカナ・長音符のみ（記号/英数/句読点なし）。
    for c in s:
        if (
            "぀" <= c <= "ゟ"  # hiragana
            or "゠" <= c <= "ヿ"  # katakana
            or "一" <= c <= "鿿"  # kanji
            or c in "々ー"
        ):
            continue
        return False
    return True


def is_kana_reading(r: str) -> bool:
    return len(r) > 0 and all("ぁ" <= c <= "ゖ" or c == "ー" for c in r)


def load_eval_surfaces() -> set[str]:
    surfaces: set[str] = set()
    for name in ("eval/seed.tsv", "eval/generated.tsv", "lm/dev.tsv"):
        p = Path("data") / name
        if not p.exists():
            continue
        for line in p.open(encoding="utf-8"):
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            surfaces.add(line.split("\t")[0])
    return surfaces


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8", newline="\n")
    corpus = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("data/lm/corpus.tsv")
    cap = int(sys.argv[2]) if len(sys.argv) > 2 else 12000

    exclude = load_eval_surfaces()
    tagger = fugashi.Tagger()
    seen: set[str] = set()
    emitted = 0

    def emit(surface: str, lo: int, hi: int) -> bool:
        nonlocal emitted
        if not (lo <= len(surface) <= hi) or not is_clean_surface(surface):
            return False
        if surface in exclude or surface in seen:
            return False
        parts = []
        for w in tagger(surface):
            kana = getattr(w.feature, "kana", None)
            if not kana:
                return False
            parts.append(to_hiragana(kana))
        reading = "".join(parts)
        if not parts or not is_kana_reading(reading):
            return False
        seen.add(surface)
        print(f"{surface}\t{reading}")
        emitted += 1
        return True

    # 評価セット（全文）の分布に合わせ、句読点を除いた全文を優先。長すぎる/汚い場合は節に分割。
    for raw in corpus.open(encoding="utf-8"):
        if emitted >= cap:
            break
        line = raw.strip()
        stripped = "".join(SPLIT.sub("", line))  # 記号/句読点を除去した全文。
        if 4 <= len(stripped) <= 40 and emit(stripped, 4, 40):
            continue
        for clause in SPLIT.split(line):
            if emitted >= cap:
                break
            emit(clause.strip(), 4, 40)

    print(f"出力: {emitted:,} 節（除外集合 {len(exclude):,}）", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
