using System.Diagnostics;
using ShortcutIme.Core;
using ShortcutIme.Evaluation;

// 多方式（混在）索引の計測＋逆変換器の不変条件監査。
// 使い方: dotnet run -c Release --project tools/ShortcutIme.DataGen -- [辞書dir]

var dataDir = args.Length > 0 ? args[0] : Path.Combine("data", "dictionary_oss");
Console.WriteLine($"辞書: {Path.GetFullPath(dataDir)}");
var entries = Directory.EnumerateFiles(dataDir, "dictionary*.txt").Order()
    .SelectMany(MozcDictionaryReader.ReadFile).ToList();
Console.WriteLine($"エントリ数: {entries.Count:N0}");
Console.WriteLine();

// --- 異形展開の統計 ---
long totalVariants = 0;
var maxVariants = 0;
var hist = new int[12];
foreach (var entry in entries)
{
    var count = RomajiVariants.ExpandReadingWithHabits(entry.Reading).Count;
    totalVariants += count;
    maxVariants = Math.Max(maxVariants, count);
    hist[Math.Min(count, hist.Length - 1)]++;
}

Console.WriteLine("== 異形展開（モーラ直積） ==");
Console.WriteLine($"  総異形数: {totalVariants:N0}（1語平均 {(double)totalVariants / entries.Count:F2}、最大 {maxVariants}）");
Console.WriteLine($"  異形数の分布: {string.Join("  ", hist.Select((c, i) => $"{(i == hist.Length - 1 ? $"{i}+" : i.ToString())}:{c:N0}"))}");
Console.WriteLine();

// --- ビルド時間・メモリ比較 ---
Console.WriteLine("== トライ構築 比較 ==");
BuildAndMeasure("単一方式（訓令式）", () => RomajiTrie.Build(entries, new RomajiEncoder()));
BuildAndMeasure("多方式（混在直積）", () => RomajiTrie.Build(entries, reading => RomajiVariants.ExpandReading(reading)));
BuildAndMeasure("多方式＋癖（本番）", () => RomajiTrie.Build(entries, reading => RomajiVariants.ExpandReadingWithHabits(reading)));
Console.WriteLine();

// --- メモリ内訳（本番トライ＝多方式＋癖の構造統計＋文字列重複） ---
var multiTrie = RomajiTrie.Build(entries, reading => RomajiVariants.ExpandReadingWithHabits(reading));
var stats = multiTrie.ComputeStats();
GC.KeepAlive(multiTrie);
var distinctSurfaces = entries.Select(e => e.Surface).Distinct().Count();
var distinctReadings = entries.Select(e => e.Reading).Distinct().Count();
Console.WriteLine("== メモリ内訳（多方式トライ） ==");
Console.WriteLine($"  ノード数: {stats.Nodes:N0}（子マップ保持 {stats.NodesWithChildren:N0}、辺 {stats.ChildEdges:N0}）");
Console.WriteLine($"  候補リスト数: {stats.CandidateLists:N0}、候補総数: {stats.Candidates:N0}");
Console.WriteLine($"  辞書のユニーク表層: {distinctSurfaces:N0}、ユニーク読み: {distinctReadings:N0}");
Console.WriteLine($"  候補スロット {stats.Candidates:N0} は dedup＋intern 済み Candidate を id 参照（表層は {distinctSurfaces:N0} 種で共有）");
Console.WriteLine($"  ノード/子辺は CSR フラット配列で保持（per-node Dictionary は廃止済み）");
Console.WriteLine();

// --- 語レベル大規模測定（辞書語＝gold は必ず到達可能・読み正確。百万級のテストデータが即生成できる） ---
var wordSample = args.Length > 1 && int.TryParse(args[1], out var ws) ? ws : 50_000;
var stride = Math.Max(1, entries.Count / wordSample);
(RomajiScheme Scheme, EvalInputMode Mode, IReadingEncoder Encoder)[] wordProfiles =
[
    (RomajiScheme.Kunrei, EvalInputMode.Consonant, new ConsonantEncoder()),
    (RomajiScheme.Kunrei, EvalInputMode.Full, new RomajiEncoder()),
    (RomajiScheme.Hepburn, EvalInputMode.Consonant, new ConsonantEncoder(RomajiScheme.Hepburn)),
    (RomajiScheme.Hepburn, EvalInputMode.Full, new RomajiEncoder(RomajiScheme.Hepburn)),
];
Console.WriteLine($"== 語レベル大規模測定（サンプル stride={stride}、約 {entries.Count / stride:N0} 語/方式） ==");
Console.WriteLine($"  {"プロファイル",-20} {"top-1",7} {"top-5",7} {"top-10",7} {"MRR",6}");
foreach (var (scheme, mode, encoder) in wordProfiles)
{
    long n = 0, top1 = 0, top5 = 0, top10 = 0;
    var mrr = 0.0;
    for (var i = 0; i < entries.Count; i += stride)
    {
        var entry = entries[i];
        var input = encoder.Encode(entry.Reading);
        if (input.Length == 0)
        {
            continue;
        }

        var results = multiTrie.Search(input);
        var rank = 0;
        for (var r = 0; r < results.Count; r++)
        {
            if (results[r].Surface == entry.Surface)
            {
                rank = r + 1;
                break;
            }
        }

        n++;
        if (rank == 1) { top1++; }
        if (rank is >= 1 and <= 5) { top5++; }
        if (rank is >= 1 and <= 10) { top10++; }
        if (rank >= 1) { mrr += 1.0 / rank; }
    }

    Console.WriteLine($"  {scheme + "/" + mode,-20} {top1 / (double)n,7:P0} {top5 / (double)n,7:P0} {top10 / (double)n,7:P0} {mrr / n,6:F3}");
}

Console.WriteLine();

// --- 直列化 / 高速ロード（Tier2） ---
var blobPath = Path.Combine(Path.GetTempPath(), "shortcutime-trie.bin");
var built = RomajiTrie.Build(entries, reading => RomajiVariants.ExpandReadingWithHabits(reading));
var swSave = System.Diagnostics.Stopwatch.StartNew();
built.Save(blobPath);
swSave.Stop();
GC.KeepAlive(built);
var blobMb = new FileInfo(blobPath).Length / 1024.0 / 1024.0;

GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
var beforeLoad = GC.GetTotalMemory(true);
var swLoad = System.Diagnostics.Stopwatch.StartNew();
var loaded = RomajiTrie.Load(blobPath);
swLoad.Stop();
var afterLoad = GC.GetTotalMemory(true);
Console.WriteLine("== 直列化 / 高速ロード（Tier2） ==");
Console.WriteLine($"  blob: {blobMb:N0} MB,  保存 {swSave.ElapsedMilliseconds:N0} ms");
Console.WriteLine($"  ロード {swLoad.ElapsedMilliseconds:N0} ms（テキスト辞書の再パース・トライ再構築なし）,  常駐 ~{(afterLoad - beforeLoad) / 1024 / 1024:N0} MB");
GC.KeepAlive(loaded);
Console.WriteLine();

// --- 不変条件監査：訓令式・ヘボン式の各々で「子音⊆フル」かつ「一貫フルが展開集合に含まれる」 ---
var consK = new ConsonantEncoder(RomajiScheme.Kunrei);
var consH = new ConsonantEncoder(RomajiScheme.Hepburn);
var fullK = new RomajiEncoder(RomajiScheme.Kunrei);
var fullH = new RomajiEncoder(RomajiScheme.Hepburn);
long checks = 0, subseqViolations = 0, missingInExpand = 0;
long degradeViolations = 0;
var examples = new List<string>();
foreach (var entry in entries)
{
    var expand = RomajiVariants.ExpandReading(entry.Reading);
    var expandHabits = RomajiVariants.ExpandReadingWithHabits(entry.Reading);
    var habitSet = expandHabits as IReadOnlyCollection<string> ?? expandHabits.ToList();

    foreach (var (cons, full) in new[]
             {
                 (consK.Encode(entry.Reading), fullK.Encode(entry.Reading)),
                 (consH.Encode(entry.Reading), fullH.Encode(entry.Reading)),
             })
    {
        checks++;
        if (!IsSubsequence(cons, full) || RemoveVowels(cons) != RemoveVowels(full))
        {
            subseqViolations++;
            if (examples.Count < 15)
            {
                examples.Add($"[subseq] 読み={entry.Reading} cons={cons} full={full}");
            }
        }

        if (full.Length > 0 && !habitSet.Contains(full))
        {
            missingInExpand++;
            if (examples.Count < 15)
            {
                examples.Add($"[expand欠落] 読み={entry.Reading} full={full} 異形数={habitSet.Count}");
            }
        }
    }

    // 非劣化：癖込み展開は scheme 異形を必ず全て含む（アンカー保持）。
    foreach (var v in expand)
    {
        if (!habitSet.Contains(v))
        {
            degradeViolations++;
            if (examples.Count < 15)
            {
                examples.Add($"[非劣化違反] 読み={entry.Reading} 欠落異形={v}");
            }

            break;
        }
    }
}

Console.WriteLine("== 不変条件監査（生成入力が必ず照合可能か） ==");
Console.WriteLine($"  検査数: {checks:N0}（エントリ×2方式）");
Console.WriteLine($"  子音⊆フル 違反: {subseqViolations:N0}");
Console.WriteLine($"  一貫フルが展開集合に無い: {missingInExpand:N0}");
Console.WriteLine($"  非劣化違反（癖込み⊉scheme異形）: {degradeViolations:N0}");
foreach (var example in examples)
{
    Console.WriteLine($"    {example}");
}

Console.WriteLine();

// --- operational 到達性監査（本番トライで癖入力が gold へ到達するか・サンプル） ---
{
    var habitStride = Math.Max(1, entries.Count / 5000);
    long sampled = 0, unreachable = 0;
    var unreachExamples = new List<string>();
    for (var i = 0; i < entries.Count; i += habitStride)
    {
        var entry = entries[i];
        if (!entry.Reading.Contains('を') && !entry.Reading.Contains('ん'))
        {
            continue;
        }

        sampled++;
        // 癖込み異形の子音骨格（最難入力）が gold へ到達できるか。
        var reachable = false;
        foreach (var v in RomajiVariants.ExpandReadingWithHabits(entry.Reading))
        {
            var key = RemoveVowels(v);
            if (key.Length > 0 && multiTrie.Search(key).Any(c => c.Surface == entry.Surface))
            {
                reachable = true;
                break;
            }
        }

        if (!reachable)
        {
            unreachable++;
            if (unreachExamples.Count < 10)
            {
                unreachExamples.Add($"読み={entry.Reading} 表層={entry.Surface}");
            }
        }
    }

    Console.WriteLine("== operational 到達性監査（癖含むエントリ・サンプル） ==");
    Console.WriteLine($"  サンプル数: {sampled:N0}（stride={habitStride}、を/ん 含む）");
    Console.WriteLine($"  gold 到達不能: {unreachable:N0}");
    foreach (var e in unreachExamples)
    {
        Console.WriteLine($"    {e}");
    }

    Console.WriteLine();
}

// --- テスト大幅拡充: テンプレ＋スロットで読みが正確な文を機械生成 ---
var generated = SentenceTemplateGenerator.Generate(400);
var evalDir = Path.Combine("data", "eval");
Directory.CreateDirectory(evalDir);
var generatedPath = Path.Combine(evalDir, "generated.tsv");
using (var writer = new StreamWriter(generatedPath))
{
    writer.WriteLine("# 機械生成（テンプレ＋スロット）。読みは構成上正確＝出題ミスなし。再生成: dotnet run --project tools/ShortcutIme.DataGen");
    foreach (var item in generated)
    {
        writer.WriteLine($"{item.Sentence}\t{item.Reading}");
    }
}

Console.WriteLine("== テスト大幅拡充（機械生成） ==");
Console.WriteLine($"  {generated.Count} 文を生成 → {Path.GetFullPath(generatedPath)}");
Console.WriteLine($"  例: {string.Join(" / ", generated.Take(3).Select(c => $"{c.Sentence}〔{c.Reading}〕"))}");

static void BuildAndMeasure(string label, Func<RomajiTrie> build)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var before = GC.GetTotalMemory(true);
    var sw = Stopwatch.StartNew();
    var trie = build();
    sw.Stop();
    var after = GC.GetTotalMemory(true);
    Console.WriteLine($"  {label}: build {sw.ElapsedMilliseconds:N0} ms,  常駐 ~{(after - before) / 1024 / 1024:N0} MB");
    GC.KeepAlive(trie); // 計測中はトライを生かす（after 計測後に解放されてよい）
}

static bool IsSubsequence(string sub, string full)
{
    var i = 0;
    foreach (var c in full)
    {
        if (i < sub.Length && sub[i] == c)
        {
            i++;
        }
    }

    return i == sub.Length;
}

static string RemoveVowels(string s) => string.Concat(s.Where(c => !"aiueo".Contains(c)));
