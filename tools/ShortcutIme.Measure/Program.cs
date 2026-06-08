using ShortcutIme.Core;

// ステップ0／母音絞り込み検証：子音のみキーの衝突分布と、母音追加による絞り込み効果を測る。

var dataDir = args.Length > 0 ? args[0] : Path.Combine("data", "dictionary_oss");
Console.WriteLine($"辞書ディレクトリ: {Path.GetFullPath(dataDir)}");

var files = Directory.EnumerateFiles(dataDir, "dictionary*.txt").Order().ToList();
var entries = files.SelectMany(MozcDictionaryReader.ReadFile).ToList();
Console.WriteLine($"エントリ数: {entries.Count:N0}");
Console.WriteLine();

var consonant = new ConsonantEncoder();
var romaji = new RomajiEncoder();

// --- 子音のみキーの衝突分布 ---
var byConsonant = new Dictionary<string, HashSet<string>>();
foreach (var entry in entries)
{
    var key = consonant.Encode(entry.Reading);
    if (key.Length == 0)
    {
        continue;
    }
    if (!byConsonant.TryGetValue(key, out var surfaces))
    {
        surfaces = [];
        byConsonant[key] = surfaces;
    }
    surfaces.Add(entry.Surface);
}
var counts = byConsonant.Values.Select(s => s.Count).Order().ToArray();
Console.WriteLine($"子音のみキー数: {byConsonant.Count:N0}");
Console.WriteLine($"  候補数 中央値:{counts[counts.Length / 2]}  90%ile:{counts[(int)(counts.Length * 0.9)]}  最大:{counts[^1]}");
Console.WriteLine();

// --- 母音追加による絞り込み（RomajiTrie） ---
var trie = RomajiTrie.Build(entries, romaji);
(string Word, string Reading)[] samples =
[
    ("今日", "きょう"), ("教育", "きょういく"), ("共有", "きょうゆう"),
    ("協議", "きょうぎ"), ("許容", "きょよう"), ("愛", "あい"), ("駅", "えき"),
];

Console.WriteLine("== 母音追加で候補数と正解順位がどう変わるか（子音のみ → フルローマ字） ==");
foreach (var (word, reading) in samples)
{
    var cons = consonant.Encode(reading);
    var full = romaji.Encode(reading);
    var consResult = trie.Search(cons);
    var fullResult = trie.Search(full);
    Console.WriteLine(
        $"  {word}（{reading}）: " +
        $"\"{cons}\"={consResult.Count,4}件({Rank(consResult, word)}) → " +
        $"\"{full}\"={fullResult.Count,3}件({Rank(fullResult, word)})");
}
Console.WriteLine();

// --- 連文節変換：連接コスト＋ペナルティスイープ（フルローマ字での文一致率） ---
Console.WriteLine("== 連文節変換 スイープ（フルローマ字 → 文の完全一致率） ==");
var connectionPath = Path.Combine(dataDir, "connection_single_column.txt");
var connection = File.Exists(connectionPath) ? ConnectionMatrix.Load(connectionPath) : null;
Console.WriteLine($"連接コスト: {(connection is null ? "なし" : "ロード済み")}");
(string Sentence, string Reading)[] sentences =
[
    ("今日はありがとうございました", "きょうはありがとうございました"),
    ("本日はお忙しい中ありがとうございました", "ほんじつはおいそがしいなかありがとうございました"),
    ("よろしくお願いします", "よろしくおねがいします"),
    ("お疲れ様でした", "おつかれさまでした"),
    ("ありがとうございます", "ありがとうございます"),
    ("申し訳ございません", "もうしわけございません"),
    ("承知しました", "しょうちしました"),
    ("お世話になっております", "おせわになっております"),
];
int[] segs = [0, 500, 1000, 2000];
int[] skips = [500, 1000, 2000];
var bestCorrect = -1;
var bestSeg = 0;
var bestSkip = 0;
foreach (var seg in segs)
{
    foreach (var sk in skips)
    {
        var conv = new PhraseConverter(trie, connection, seg, sk);
        var correct = sentences.Count(s => string.Concat(conv.Convert(romaji.Encode(s.Reading)).Select(c => c.Surface)) == s.Sentence);
        Console.WriteLine($"  seg={seg,5} skip={sk,5}: {correct}/{sentences.Length}");
        if (correct > bestCorrect) { bestCorrect = correct; bestSeg = seg; bestSkip = sk; }
    }
}
Console.WriteLine();
Console.WriteLine($"== ベスト設定 seg={bestSeg} skip={bestSkip}（{bestCorrect}/{sentences.Length}）での各文 ==");
var best = new PhraseConverter(trie, connection, bestSeg, bestSkip);
foreach (var (sentence, reading) in sentences)
{
    var phrases = best.Convert(romaji.Encode(reading));
    var output = string.Concat(phrases.Select(c => c.Surface));
    var ok = output == sentence;
    Console.WriteLine($"  {(ok ? "✓" : "✗")} {output}　（目標: {sentence}）");
    if (!ok)
    {
        // 失敗文の文節境界＋各文節の読み・表層をダンプ（境界誤り か 同音異義 かの分類用）
        Console.WriteLine($"      分割: {string.Join(" | ", phrases.Select(c => $"{c.Surface}〔{c.Reading}〕"))}");
    }
}

static string Rank(IReadOnlyList<Candidate> candidates, string surface)
{
    for (var i = 0; i < candidates.Count; i++)
    {
        if (candidates[i].Surface == surface)
        {
            return $"{i + 1}位";
        }
    }
    return "圏外";
}
