# /// script
# requires-python = ">=3.11"
# dependencies = ["fugashi>=1.3", "unidic-lite==1.0.8"]
# ///
"""livedoor（＋任意で青空文庫）から char LM 用の corpus.tsv と dev.tsv を決定的に生成する。

- corpus.tsv: 1行1文の生文（char モードの WordNGramLm が文字分割して学習）。
- dev.tsv: 表層<TAB>読み。読みは fugashi の kana→ひらがな、長音符ーや非かなを含む文は除外（faithful）。

リーク防止: train/dev は文の SHA1 ハッシュで決定的に分割。corpus・dev とも seed.tsv / generated.tsv と重複排除。
青空文庫はファイル名に「（新字新仮名）」を含むものだけ採用（旧仮名・旧字を機械排除）し corpus 補強に限定（dev には入れない）。

使い方:
    mise exec -- uv run tools/datagen-py/build_corpus.py            # livedoor 単独 → corpus.tsv / dev.tsv
    mise exec -- uv run tools/datagen-py/build_corpus.py --aozora   # livedoor+青空 → corpus_aozora.tsv（dev は共通）
"""

import hashlib
import io
import re
import sys
import tarfile
import urllib.request
from pathlib import Path

from fugashi import Tagger

LIVEDOOR_URL = "https://www.rondhuit.com/download/ldcc-20140209.tar.gz"
OUT_DIR = Path("data/lm")
EVAL_DIR = Path("data/eval")
AOZORA_REPO = Path("data/lm/aozora_repo")
DEV_BUCKET = 20  # 約 1/20 を dev に回す（決定的）。

JP = re.compile(r"[ぁ-ゟ゠-ヿ一-鿿]")           # 日本語文字を含む文だけ採用。
HIRAGANA_ONLY = re.compile(r"[ぁ-ゖ]+")         # 読みは長音符ー・非かなを含まないひらがな列のみ faithful。
SENTENCE_SPLIT = re.compile(r"(?<=[。！？])")
RUBY = re.compile(r"《[^》]*》")
ANNOTATION = re.compile(r"［＃[^］]*］")


def kata_to_hira(text: str) -> str:
    return "".join(chr(ord(ch) - 0x60) if "ァ" <= ch <= "ヶ" else ch for ch in text)


def split_sentences(text: str):
    for chunk in SENTENCE_SPLIT.split(text):
        stripped = chunk.strip()
        if stripped:
            yield stripped


def fetch_livedoor() -> bytes:
    cache = OUT_DIR / "ldcc-20140209.tar.gz"
    if cache.exists():
        return cache.read_bytes()
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    data = urllib.request.urlopen(LIVEDOOR_URL, timeout=120).read()
    cache.write_bytes(data)
    return data


def iter_livedoor_sentences(archive: bytes):
    with tarfile.open(fileobj=io.BytesIO(archive), mode="r:gz") as tar:
        for member in tar.getmembers():
            if not member.isfile() or not member.name.endswith(".txt"):
                continue
            base = member.name.rsplit("/", 1)[-1]
            if base in ("README.txt", "CHANGES.txt", "LICENSE.txt"):
                continue
            handle = tar.extractfile(member)
            if handle is None:
                continue
            lines = handle.read().decode("utf-8").splitlines()
            for line in lines[3:]:  # 1:URL 2:日付 3:タイトル 4+:本文
                yield from split_sentences(line)


def clean_aozora(text: str) -> str:
    lines = text.split("\n")
    separators = [i for i, line in enumerate(lines) if line.startswith("----------")]
    start = separators[1] + 1 if len(separators) >= 2 else 0
    end = len(lines)
    for i in range(start, len(lines)):
        if lines[i].startswith("底本：") or lines[i].startswith("底本:"):
            end = i
            break
    body = "\n".join(lines[start:end])
    body = RUBY.sub("", body)
    body = body.replace("｜", "")
    body = ANNOTATION.sub("", body)
    return body


def iter_aozora_sentences(repo: Path):
    for path in sorted(repo.glob("作品/*/*（新字新仮名）.txt")):
        try:
            text = path.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue
        for line in clean_aozora(text).split("\n"):
            yield from split_sentences(line)


def bucket(sentence: str) -> int:
    digest = hashlib.sha1(sentence.encode("utf-8")).hexdigest()
    return int(digest[:8], 16) % DEV_BUCKET


def load_exclusions() -> set[str]:
    excluded: set[str] = set()
    for name in ("seed.tsv", "generated.tsv"):
        path = EVAL_DIR / name
        if not path.exists():
            continue
        for line in path.read_text(encoding="utf-8").splitlines():
            if not line or line.startswith("#"):
                continue
            surface = line.split("\t")[0].strip()
            if surface:
                excluded.add(surface)
    return excluded


def reading_of(tagger: Tagger, surface: str):
    parts = []
    for word in tagger(surface):
        kana = getattr(word.feature, "kana", None)
        if not kana or kana == "*":
            return None
        parts.append(kata_to_hira(kana))
    reading = "".join(parts)
    if not reading or not HIRAGANA_ONLY.fullmatch(reading):
        return None
    return reading


def accept(sentence: str, excluded: set[str], seen: set[str]) -> bool:
    if not (5 <= len(sentence) <= 60):
        return False
    if not JP.search(sentence):
        return False
    if sentence in excluded or sentence in seen:
        return False
    return True


def main() -> None:
    use_aozora = "--aozora" in sys.argv
    tagger = Tagger()
    excluded = load_exclusions()
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    archive = fetch_livedoor()

    seen: set[str] = set()
    corpus_path = OUT_DIR / ("corpus_aozora.tsv" if use_aozora else "corpus.tsv")
    dev_path = OUT_DIR / "dev.tsv"
    n_corpus = n_dev = n_aozora = 0
    with corpus_path.open("w", encoding="utf-8", newline="\n") as corpus_file, \
            dev_path.open("w", encoding="utf-8", newline="\n") as dev_file:
        for sentence in iter_livedoor_sentences(archive):
            if not accept(sentence, excluded, seen):
                continue
            seen.add(sentence)
            if bucket(sentence) == 0:
                reading = reading_of(tagger, sentence)
                if reading is None:
                    continue
                dev_file.write(f"{sentence}\t{reading}\n")
                n_dev += 1
            else:
                corpus_file.write(sentence + "\n")
                n_corpus += 1

        if use_aozora:
            for sentence in iter_aozora_sentences(AOZORA_REPO):
                if not accept(sentence, excluded, seen):
                    continue
                seen.add(sentence)
                corpus_file.write(sentence + "\n")  # 青空は corpus 補強のみ（dev には入れない）
                n_aozora += 1

    print(f"corpus: {n_corpus} livedoor + {n_aozora} aozora -> {corpus_path}")
    print(f"dev:    {n_dev} sentences -> {dev_path}")


if __name__ == "__main__":
    main()
