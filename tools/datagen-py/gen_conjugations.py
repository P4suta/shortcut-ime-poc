# /// script
# requires-python = ">=3.11"
# dependencies = ["fugashi>=1.3", "unidic-lite==1.0.8"]
# ///
"""活用合成の穴を埋める補助辞書 dictionary98.txt を生成する（オフライン専用・ランタイム Core は純 C# のまま）。

問題：Mozc 辞書は動詞/形容詞の base 形（送る=おくる）しか持たず、活用形（送って=おくって・買った=かった）が
辞書単位に無くラティスで作れない。generated.tsv の gold∈n-best が 67% で頭打ち（送ってください→「多く立て」）。

手法（harvest + 再結合）：コーパスを fugashi(UniDic) で解析し、UniDic 短単位が過分割する活用を
「動詞/形容詞の頭 ＋ 後続の助動詞・接続助詞（て/た/ます/ない/だ 等）」を1文節に再結合して復元する。
頻度しきい値で絞り、Mozc 5列形式（読み<TAB>左ID<TAB>右ID<TAB>コスト<TAB>表層）で出力。
- 読みは fugashi の kana（カタカナ）→ひらがな。
- 接続ID：左ID＝ベース動詞の左ID（を→動詞が自然）、右ID＝1851（dict99 の語尾と同じ＝ください/文末へ繋がる）。
- コスト：ベース語コスト＋delta（過剰優先を防ぐ）。dict99/Mozc に既存の (読み,表層) は出さない。

使い方:
    mise exec -- uv run tools/datagen-py/gen_conjugations.py data/dictionary_oss data/lm/corpus.tsv \
        > data/dictionary_oss/dictionary98.txt
    # 任意: 第3=min_freq(既定3) 第4=cost_delta(既定0) 第5=right_id(既定1851)
"""

import sys
from collections import defaultdict
from pathlib import Path

import fugashi

KATA_TO_HIRA = {chr(c): chr(c - 0x60) for c in range(0x30A1, 0x30F7)}  # ァ..ヶ → ぁ..ゖ


def to_hiragana(kata: str) -> str:
    return "".join(KATA_TO_HIRA.get(c, c) for c in kata)


def is_kana_reading(r: str) -> bool:
    # ひらがな＋長音符のみ（記号・英数・漢字混入は除外＝読みとして不正）。
    return len(r) > 0 and all("ぁ" <= c <= "ゖ" or c == "ー" for c in r)


def load_mozc_bases(dict_dir: Path):
    """表層 → (leftId, rightId, cost) の最小コスト辞書、および既存 (読み,表層) 集合。"""
    bases: dict[str, tuple[int, int, int]] = {}
    existing: set[tuple[str, str]] = set()
    for path in sorted(dict_dir.glob("dictionary*.txt")):
        with path.open(encoding="utf-8") as f:
            for line in f:
                parts = line.rstrip("\n").split("\t")
                if len(parts) != 5:
                    continue
                reading, lid, rid, cost, surface = parts
                try:
                    lid_i, rid_i, cost_i = int(lid), int(rid), int(cost)
                except ValueError:
                    continue
                existing.add((reading, surface))
                prev = bases.get(surface)
                if prev is None or cost_i < prev[2]:
                    bases[surface] = (lid_i, rid_i, cost_i)
    return bases, existing


def main() -> int:
    # Windows の既定 stdout は cp932。辞書は UTF-8（LF）必須なので強制する。
    sys.stdout.reconfigure(encoding="utf-8", newline="\n")

    dict_dir = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("data/dictionary_oss")
    corpus = Path(sys.argv[2]) if len(sys.argv) > 2 else Path("data/lm/corpus.tsv")
    min_freq = int(sys.argv[3]) if len(sys.argv) > 3 else 3
    cost_delta = int(sys.argv[4]) if len(sys.argv) > 4 else 0
    right_id = int(sys.argv[5]) if len(sys.argv) > 5 else 1851

    bases, existing = load_mozc_bases(dict_dir)
    print(f"Mozc base 表層: {len(bases):,}, 既存(読み,表層): {len(existing):,}", file=sys.stderr)

    tagger = fugashi.Tagger()

    # (surface, reading, base_surface) → 頻度
    counts: dict[tuple[str, str, str], int] = defaultdict(int)

    def feat(w, name):
        return getattr(w.feature, name, None)

    lines = 0
    for raw in corpus.open(encoding="utf-8"):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        lines += 1

        words = list(tagger(line))
        i = 0
        n = len(words)
        while i < n:
            w = words[i]
            pos1 = feat(w, "pos1")
            if pos1 in ("動詞", "形容詞"):
                base_surface = feat(w, "lemma") or w.surface
                # 読みは表層形の発音（kana）。lemma の読みではなく活用形の読みを使う。
                surf_parts = [w.surface]
                read_parts = [feat(w, "kana") or ""]
                j = i + 1
                attached = 0
                while j < n:
                    wj = words[j]
                    p1 = feat(wj, "pos1")
                    p2 = feat(wj, "pos2")
                    is_aux = p1 == "助動詞"
                    is_conj_particle = p1 == "助詞" and p2 == "接続助詞" and wj.surface in ("て", "で", "たり", "ば")
                    if is_aux or is_conj_particle:
                        surf_parts.append(wj.surface)
                        read_parts.append(feat(wj, "kana") or "")
                        attached += 1
                        j += 1
                    else:
                        break

                if attached > 0:
                    surface = "".join(surf_parts)
                    reading = to_hiragana("".join(read_parts))
                    if is_kana_reading(reading) and base_surface in bases:
                        counts[(surface, reading, base_surface)] += 1
                i = j if j > i else i + 1
            else:
                i += 1

    print(f"コーパス {lines:,} 文 → 活用文節候補 {len(counts):,} 種", file=sys.stderr)

    # 出力：頻度しきい値・既存除外・(読み,表層) dedup（最小コスト）。
    best: dict[tuple[str, str], tuple[int, int, int]] = {}  # (reading,surface) → (lid,rid,cost)
    emitted = 0
    for (surface, reading, base_surface), freq in counts.items():
        if freq < min_freq:
            continue
        if (reading, surface) in existing:
            continue
        lid, _rid_base, base_cost = bases[base_surface]
        cost = max(1, base_cost + cost_delta)
        key = (reading, surface)
        prev = best.get(key)
        if prev is None or cost < prev[2]:
            best[key] = (lid, right_id, cost)

    for (reading, surface), (lid, rid, cost) in sorted(best.items()):
        print(f"{reading}\t{lid}\t{rid}\t{cost}\t{surface}")
        emitted += 1

    print(f"出力エントリ: {emitted:,}（min_freq={min_freq}, cost_delta={cost_delta}, right_id={right_id}）", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
