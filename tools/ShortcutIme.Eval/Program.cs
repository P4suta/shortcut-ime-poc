using ShortcutIme.Core;
using ShortcutIme.Evaluation;

// Stage 0/1 測定ハーネス：方式（訓令式/ヘボン式）×母音レベル（子音のみ/フル）での文変換精度を測る。
// トライは多方式（モーラ異形の直積）で索引するため、混在打鍵でも引ける。
// n-best を IReranker で並べ替えてから評価する（既定 identity＝WFST コスト順そのまま）。
// 使い方: dotnet run -c Release --project tools/ShortcutIme.Eval -- [辞書dir] [テストセット.tsv] [n-best] [サブコマンド] [arg5] [arg6...]
//   サブコマンド: identity | oracle | lm(blob λ) | vocab-dump | tune(corpus mode) | eval-lm(blob λ)
//                 | segment-corpus(出力パス) | seg-check | tune-interp(char.bin word.bin) | eval-interp(char.bin word.bin λ_char λ_word)
//                 | spectrum(seed)

var dataDir = args.Length > 0 ? args[0] : Path.Combine("data", "dictionary_oss");
var testSetPath = args.Length > 1 ? args[1] : Path.Combine("data", "eval", "seed.tsv");
var NBest = args.Length > 2 && int.TryParse(args[2], out var nb) ? nb : 20;
var rerankerName = args.Length > 3 ? args[3].ToLowerInvariant() : "identity";
var lmArg = args.Length > 4 ? args[4] : null;
var lmLambda = args.Length > 5 && double.TryParse(args[5], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedLambda) ? parsedLambda : 0.0;

// lgbm-check: C# GBDT 評価器が Python 予測と一致するか（パーサ正当性）。trie 不要なので辞書ロード前に分岐。
// args[1]=ranker.txt, args[4]=ranker_check.tsv（行: f0..f11 <TAB> python_pred）。
if (rerankerName == "lgbm-check")
{
    var model = GradientBoostedTrees.Load(testSetPath);
    var checkPath = lmArg ?? throw new ArgumentException("check.tsv が必要（args[4]）");
    var ci = System.Globalization.CultureInfo.InvariantCulture;
    var maxDiff = 0.0;
    var rows = 0;
    foreach (var raw in File.ReadLines(checkPath))
    {
        var parts = raw.Split('\t');
        if (parts.Length < model.FeatureCount + 1)
        {
            continue;
        }

        var feats = new double[model.FeatureCount];
        for (var i = 0; i < model.FeatureCount; i++)
        {
            feats[i] = double.Parse(parts[i], ci);
        }

        var pyPred = double.Parse(parts[model.FeatureCount], ci);
        var csPred = model.Evaluate(feats);
        maxDiff = Math.Max(maxDiff, Math.Abs(csPred - pyPred));
        rows++;
    }

    Console.WriteLine($"lgbm-check: model={Path.GetFullPath(testSetPath)}, {rows} 行, 特徴数={model.FeatureCount}");
    Console.WriteLine($"  C#評価器 vs Python 予測 最大絶対差 = {maxDiff:E3}");
    Console.WriteLine($"  {(maxDiff < 1e-6 ? "PASS（パーサ/評価器は正当）" : "FAIL（パーサ要修正）")}");
    return;
}

Console.WriteLine($"辞書ディレクトリ: {Path.GetFullPath(dataDir)}");
var entries = Directory.EnumerateFiles(dataDir, "dictionary*.txt").Order()
    .SelectMany(MozcDictionaryReader.ReadFile).ToList();
Console.WriteLine($"エントリ数: {entries.Count:N0}");

Console.WriteLine("多方式トライを構築中（モーラ異形の直積を索引）...");
var trie = RomajiTrie.Build(entries, reading => RomajiVariants.ExpandReadingWithHabits(reading));

var connectionPath = Path.Combine(dataDir, "connection_single_column.txt");
var connection = File.Exists(connectionPath) ? ConnectionMatrix.Load(connectionPath) : null;
Console.WriteLine($"連接コスト: {(connection is null ? "なし" : "ロード済み")}");

const int SegmentPenalty = 0;
const int VowelSkipPenalty = 500;
var converter = new PhraseConverter(trie, connection, SegmentPenalty, VowelSkipPenalty);

// segment-corpus: 生文コーパス（1行1文）を Mozc 辞書 surface ビタビで分割し、空白区切り surface 列を書き出す。
// cases（テストセット TSV）ロードより前に分岐する——生文は「表層<TAB>読み」形式でないため LoadTsv が落ちる。
if (rerankerName == "segment-corpus")
{
    SegmentCorpus();
    return;
}

var cases = EvalDataset.LoadTsv(testSetPath);
Console.WriteLine($"テストセット: {Path.GetFullPath(testSetPath)}（{cases.Count} 文）");
Console.WriteLine();

var harness = new EvaluationHarness();

// vocab-dump: LM を作らずに gold の Mozc 文節境界をダンプ（fugashi 分かち書きと目視比較し word/char の plan A を決める）。
if (rerankerName == "vocab-dump")
{
    DumpSegmentation();
    return;
}

// seg-check: SurfaceSegmenter（surface ビタビ）の分割と gold hypothesis の Mozc 文節境界の token 一致率を測る。
// word LM が成立する前提（コーパス分割が rerank の分割に転移する）を全コーパス学習の前に検証する最重要ゲート。
if (rerankerName == "seg-check")
{
    SegCheck();
    return;
}

// tune: dev のみで (λ_bi, floor, λ) を grid search（n-best キャッシュで高速化）し G1 を判定する。mode は args[5]（word|char）。
if (rerankerName == "tune")
{
    TuneLm();
    return;
}

// tune-interp: char+word の補間重み (λ_char, λ_word) を dev で 2D grid search する。args[4]=char blob, args[5]=word blob。
if (rerankerName == "tune-interp")
{
    TuneInterp();
    return;
}

// eval-lm: test セットで identity と lm を1回の trie 構築で並べて比較する（top-1 / MRR）。
if (rerankerName == "eval-lm")
{
    EvalLm();
    return;
}

// eval-interp: identity / char / word / char+word を横並び比較。args[4]=char, args[5]=word, args[6]=λ_char, args[7]=λ_word。
if (rerankerName == "eval-interp")
{
    EvalInterp();
    return;
}

// spectrum: 母音保持率 keepRate∈{0,.25,.5,.75,1}×方式 で top-1/top-k/gold∈n-best/MRR を測る（identity）。
// 2端点（子音のみ/フル）だけでなく実打鍵の中間混在を測る。seed は args[4]（既定 0）。
if (rerankerName == "spectrum")
{
    Spectrum();
    return;
}

// misses: 訓令式・フル入力で gold が n-best 圏外＝ラティスで作れない（活用ユニット欠落等）事例を列挙する。
// フル入力なので曖昧性でなく「作れるか」を切り分けられる。活用の穴の確認＝受け入れテスト。
if (rerankerName == "misses")
{
    DumpUnreachable();
    return;
}

// reading-acc: 「子音→ひらがな（読み）」の精度を keepRate スペクトラムで測る。直接漢字変換の前段として
// 読みだけを正しく当てられるかの上限を見る（top-1 読み一致・読み∈n-best）。args[4]=seed(既定0)。
if (rerankerName == "reading-acc")
{
    ReadingAccuracy();
    return;
}

// two-stage: 二段化（子音→かな→漢字）の天井を測る。「読みを oracle で当てたら最終漢字 top-1 はどこまで伸びるか」を
// 直接変換 top-1 と並べる。oracle読み→漢字＝gold 読みを持つ仮説に絞った最尤の漢字一致率。args[4]=seed(既定0)。
if (rerankerName == "two-stage")
{
    TwoStageCeiling();
    return;
}

// reading-rerank: 読み(モーラ/語)LM で n-best の「読み」を選び直し、読み top-1 が oracle 天井に迫るか測る。
// 実際の Stage A（子音→かな）。args[4]=reading.bin, args[5]=seed(既定0)。
if (rerankerName == "reading-rerank")
{
    ReadingRerank();
    return;
}

// tune-reading: 読みを FEATURE として第3成分に足す λ_reading を dev で掃引（λ_char/λ_word 固定）。
// args[4]=char.bin, args[5]=word.bin, args[6]=reading.bin, args[7]=λ_char(既定50), args[8]=λ_word(既定500), args[9]=keepRate(既定0.5)。
if (rerankerName == "tune-reading")
{
    TuneReading();
    return;
}

// eval-reading: cw（char+word）と cwr（+reading feature）を全スペクトラムで比較する決定実験。
// args[4]=char, args[5]=word, args[6]=reading, args[7]=λ_char, args[8]=λ_word, args[9]=λ_reading, args[10]=seed(既定0)。
if (rerankerName == "eval-reading")
{
    EvalReading();
    return;
}

// tune-skip: vowelSkipPenalty を掃引し cw リランカー top-1 を keepRate 別に出す。打った母音を尊重する度合いの調整。
// 純子音ではスキップ必須なので mid-spectrum（特に seed p=0.5）で純利得を見る。args[4]=char, args[5]=word, args[6]=λ_char(50), args[7]=λ_word(500), args[8]=seed(0)。
if (rerankerName == "tune-skip")
{
    TuneSkip();
    return;
}

// gen-train: LightGBM ランカー学習データを出力。各 (文×keepRate) を1群とし、RankingFeatures＋gold ラベルを書く。
// gold∈n-best の群のみ（正例なしは学習信号なし）。args[4]=char, args[5]=word, args[6]=reading, args[7]=出力base, args[8]=seed(0)。
if (rerankerName == "gen-train")
{
    GenTrain();
    return;
}

// eval-lgbm: LightGBM ランカー(LgbmReranker) と cw（char+word）を全スペクトラムで比較する最終ゲート。
// args[4]=char, args[5]=word, args[6]=reading, args[7]=ranker.txt, args[8]=λ_char(50), args[9]=λ_word(500), args[10]=seed(0)。
if (rerankerName == "eval-lgbm")
{
    EvalLgbm();
    return;
}

// diagnose: cw の誤りを「到達性欠落(beam/構造)」「順位ミス(同音異字/読み違い/記号)」に分類し伸びしろの所在を特定。
// args[4]=char, args[5]=word, args[6]=λ_char(50), args[7]=λ_word(500), args[8]=keepRate(0.5), args[9]=seed(0)。
if (rerankerName == "diagnose")
{
    Diagnose();
    return;
}

// incremental: 逐次文節確定（確定左文脈つき）を一発全文 cw と比較。無人 greedy（apples-to-apples）＋候補UI oracle@k。
// args[4]=char, args[5]=word, args[6]=λ_char(50), args[7]=λ_word(500), args[8]=topK(5), args[9]=keepRate(0.5), args[10]=seed(0)。
if (rerankerName == "incremental")
{
    Incremental();
    return;
}

(RomajiScheme Scheme, EvalInputMode Mode)[] profiles =
[
    (RomajiScheme.Kunrei, EvalInputMode.Consonant),
    (RomajiScheme.Kunrei, EvalInputMode.Full),
    (RomajiScheme.Hepburn, EvalInputMode.Consonant),
    (RomajiScheme.Hepburn, EvalInputMode.Full),
];

// リランカー seam：n-best（コスト昇順の仮説）を並べ替え、その先頭順で評価する。
// gold が何位に居たか（＝リランカーの到達上限）は identity 実行の gold∈n-best 列で読める。
var reports = profiles.ToDictionary(p => p, p => harness.Run(cases, p.Scheme, p.Mode, MakeConvert(p.Scheme, p.Mode)));

Console.WriteLine($"== リランカー={rerankerName}（{NBest}-best・多方式索引・製品設定 seg={SegmentPenalty}/skip={VowelSkipPenalty}） ==");
Console.WriteLine($"  {"プロファイル",-20} {"top-1",7} {"top-5",7} {"top-10",7} {"gold∈n-best",12} {"MRR",6}");
foreach (var profile in profiles)
{
    var r = reports[profile];
    var goldRecall = r.Total == 0 ? 0.0 : (double)r.Cases.Count(c => c.Rank >= 1) / r.Total;
    Console.WriteLine(
        $"  {profile.Scheme + "/" + profile.Mode,-20} {r.Top1Accuracy,7:P0} {r.TopKAccuracy(5),7:P0} " +
        $"{r.TopKAccuracy(10),7:P0} {goldRecall,12:P0} {r.Mrr,6:F3}");
}

Console.WriteLine();
Console.WriteLine($"※ gold∈n-best＝正解が {NBest}-best に含まれる率＝リランカーが到達できる上限。top-1 との差がリランカーの伸びしろ。");
Console.WriteLine();

// 誤変換ダンプ（訓令式・子音のみ＝最難条件）。gold の順位を併記＝リランカーが拾えるか。
var hardest = reports[(RomajiScheme.Kunrei, EvalInputMode.Consonant)];
var misses = hardest.Cases.Where(c => !c.IsTop1).ToList();
Console.WriteLine($"== 訓令式・子音のみ での top-1 誤り {misses.Count}/{hardest.Total} 件（gold 順位つき） ==");
foreach (var r in misses)
{
    var rank = r.Rank >= 1 ? $"gold {r.Rank}位（リランカーで拾える）" : $"gold 圏外（>{NBest}）";
    Console.WriteLine($"  ✗ \"{r.Input}\"  →  {(r.Converted ? r.TopHypothesis : "(変換失敗)")}");
    Console.WriteLine($"        目標: {r.Case.Sentence}  [{rank}]");
}

// --- リランカー配線 ---
// 入力プロファイル（方式×母音レベル）ごとにリランカーを構築し、n-best を並べ替える変換クロージャを返す。
// oracle はプロファイルごとに gold 対応表が変わるため、プロファイル単位で組み直す。
Func<string, IReadOnlyList<string>> MakeConvert(RomajiScheme scheme, EvalInputMode mode)
{
    var reranker = MakeReranker(scheme, mode);
    return input => reranker
        .Rerank(input, "", converter.ConvertNBest(input, NBest))
        .Select(h => h.Surface).ToList();
}

IReranker MakeReranker(RomajiScheme scheme, EvalInputMode mode) => rerankerName switch
{
    "identity" => IdentityReranker.Instance,
    "oracle" => new OracleReranker(BuildGoldMap(scheme, mode)), // 評価専用（Evaluation 側）
    "lm" => new LmReranker(WordNGramLm.Load(lmArg ?? throw new ArgumentException("lm には blob パスが必要（args[4]）")), lmLambda),
    _ => throw new ArgumentException($"未知のリランカー: '{rerankerName}'（identity / oracle / lm / vocab-dump / tune）"),
};

// 合成入力（打鍵列）→ gold 表層。入力衝突は後勝ち（同一打鍵は本質的に曖昧で、片方しか先頭化できない）。
Dictionary<string, string> BuildGoldMap(RomajiScheme scheme, EvalInputMode mode)
{
    var map = new Dictionary<string, string>();
    foreach (var c in cases)
    {
        map[harness.EncodeInput(c.Reading, scheme, mode)] = c.Sentence;
    }

    return map;
}

// vocab-dump 本体：訓令式・フルで gold を ConvertNBest し、Surface==gold の Hypothesis の Segments を空白連結で出力。
// Mozc 文節境界（＝語 LM のトークン単位候補）を可視化し、fugashi 分かち書きと突き合わせて word/char を決める。
void DumpSegmentation()
{
    const RomajiScheme scheme = RomajiScheme.Kunrei;
    const EvalInputMode mode = EvalInputMode.Full;
    Console.WriteLine($"== vocab-dump（{scheme}/{mode}・{NBest}-best）: gold の Mozc 文節境界 ==");
    long segTotal = 0;
    var hit = 0;
    foreach (var c in cases)
    {
        var input = harness.EncodeInput(c.Reading, scheme, mode);
        var nbest = converter.ConvertNBest(input, NBest);
        Hypothesis? goldHyp = null;
        var rank = 0;
        for (var i = 0; i < nbest.Count; i++)
        {
            if (nbest[i].Surface == c.Sentence)
            {
                goldHyp = nbest[i];
                rank = i + 1;
                break;
            }
        }

        Console.WriteLine($"gold: {c.Sentence}");
        if (goldHyp is null)
        {
            Console.WriteLine($"  mozc: 圏外(>{NBest})");
        }
        else
        {
            var seg = string.Join(" / ", goldHyp.Segments.Select(s => s.Surface));
            Console.WriteLine($"  mozc: {seg}   (rank={rank}, {goldHyp.Segments.Count}文節)");
            segTotal += goldHyp.Segments.Count;
            hit++;
        }
    }

    if (hit > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"gold∈n-best: {hit}/{cases.Count}、平均文節数: {(double)segTotal / hit:F2}");
    }
}

// tune 本体：dev で char LM の (λ_bi, floor, λ) を grid search。n-best は一度だけ計算してキャッシュする。
void TuneLm()
{
    const RomajiScheme scheme = RomajiScheme.Kunrei;
    const EvalInputMode mode = EvalInputMode.Consonant;
    var corpusPath = lmArg ?? Path.Combine("data", "lm", "corpus.tsv");
    var tokenMode = (args.Length > 5 ? args[5].ToLowerInvariant() : "char") switch
    {
        "word" => TokenMode.Word,
        "char" => TokenMode.Char,
        var other => throw new ArgumentException($"未知のトークンモード: '{other}'（word / char）"),
    };
    Console.WriteLine($"tune: dev={Path.GetFullPath(testSetPath)}（{cases.Count}文）, corpus={Path.GetFullPath(corpusPath)}, n={NBest}, {scheme}/{mode}, tokens={tokenMode}");

    // ConvertNBest は LM/λ 非依存なので一度だけ計算してキャッシュ（grid search を高速化）。
    var cache = cases
        .Select(c => (Gold: c.Sentence, Nbest: converter.ConvertNBest(harness.EncodeInput(c.Reading, scheme, mode), NBest)))
        .ToList();

    double[] lambdaBis = [0.5, 0.7, 0.9, 0.95];
    double[] floors = [10.0, 15.0, 20.0];
    double[] lambdas = [0.0, 100.0, 300.0, 500.0, 1000.0, 2000.0];

    var bestMrr = -1.0;
    (double LambdaBi, double Floor, double Lambda) bestParams = default;
    var baselineMrr = 0.0;
    foreach (var lambdaBi in lambdaBis)
    {
        foreach (var floor in floors)
        {
            WordNGramLm lm;
            using (var reader = new StreamReader(corpusPath))
            {
                lm = WordNGramLm.Build(reader, tokenMode, lambdaBi, floor);
            }

            foreach (var lambda in lambdas)
            {
                var mrr = Mrr(cache, new LmReranker(lm, lambda));
                if (lambda == 0.0)
                {
                    baselineMrr = mrr;
                }

                if (mrr > bestMrr)
                {
                    bestMrr = mrr;
                    bestParams = (lambdaBi, floor, lambda);
                }
            }
        }
    }

    Console.WriteLine($"baseline(λ=0) MRR = {baselineMrr:F4}");
    Console.WriteLine($"best: λ_bi={bestParams.LambdaBi}, floor={bestParams.Floor}, λ={bestParams.Lambda}, MRR={bestMrr:F4}");
    var pass = bestParams.Lambda > 0.0 && bestMrr > baselineMrr + 1e-9;
    Console.WriteLine($"G1: {(pass ? "PASS（LM が POS-bigram ベースを改善）" : "FAIL（dev 最適 λ≈0＝改善なし、length prior と同型の負の結果）")}");
}

// キャッシュ済み n-best をリランクし MRR（平均逆順位）を返す。
double Mrr(IReadOnlyList<(string Gold, IReadOnlyList<Hypothesis> Nbest)> items, IReranker reranker)
{
    var sum = 0.0;
    foreach (var (gold, nbest) in items)
    {
        var ranked = reranker.Rerank("", "", nbest);
        for (var i = 0; i < ranked.Count; i++)
        {
            if (ranked[i].Surface == gold)
            {
                sum += 1.0 / (i + 1);
                break;
            }
        }
    }

    return items.Count == 0 ? 0.0 : sum / items.Count;
}

// eval-lm 本体：test を identity と lm で並べて比較する（top-1 と MRR、gold∈n-best）。
void EvalLm()
{
    var lm = WordNGramLm.Load(lmArg ?? throw new ArgumentException("eval-lm には blob パスが必要（args[4]）"));
    (RomajiScheme Scheme, EvalInputMode Mode)[] evalProfiles =
    [
        (RomajiScheme.Kunrei, EvalInputMode.Consonant),
        (RomajiScheme.Kunrei, EvalInputMode.Full),
        (RomajiScheme.Hepburn, EvalInputMode.Consonant),
        (RomajiScheme.Hepburn, EvalInputMode.Full),
    ];
    Console.WriteLine($"eval-lm: test={Path.GetFullPath(testSetPath)}（{cases.Count}文）, blob={lmArg}, λ={lmLambda}, n={NBest}");
    Console.WriteLine($"  {"プロファイル",-20} {"id top1",8} {"lm top1",8} {"id MRR",7} {"lm MRR",7} {"gold∈n",7}");
    foreach (var (scheme, mode) in evalProfiles)
    {
        var cache = cases
            .Select(c => (Gold: c.Sentence, Nbest: converter.ConvertNBest(harness.EncodeInput(c.Reading, scheme, mode), NBest)))
            .ToList();
        var lmReranker = new LmReranker(lm, lmLambda);
        var goldRecall = cache.Count == 0 ? 0.0 : (double)cache.Count(x => x.Nbest.Any(h => h.Surface == x.Gold)) / cache.Count;
        Console.WriteLine(
            $"  {scheme + "/" + mode,-20} {Top1(cache, IdentityReranker.Instance),8:P0} {Top1(cache, lmReranker),8:P0} " +
            $"{Mrr(cache, IdentityReranker.Instance),7:F3} {Mrr(cache, lmReranker),7:F3} {goldRecall,7:P0}");
    }
}

// キャッシュ済み n-best をリランクし top-1 正解率を返す。
double Top1(IReadOnlyList<(string Gold, IReadOnlyList<Hypothesis> Nbest)> items, IReranker reranker)
{
    var hit = 0;
    foreach (var (gold, nbest) in items)
    {
        var ranked = reranker.Rerank("", "", nbest);
        if (ranked.Count > 0 && ranked[0].Surface == gold)
        {
            hit++;
        }
    }

    return items.Count == 0 ? 0.0 : (double)hit / items.Count;
}

// segment-corpus 本体：生文コーパス（args[1]）を SurfaceSegmenter で分割し、空白区切り surface 列を出力（args[4]）。
// フォールバック率を stderr に出す（高いと正規化ミスマッチ or 真の OOV の兆候）。
void SegmentCorpus()
{
    var outputPath = lmArg ?? throw new ArgumentException("segment-corpus には出力パスが必要（args[4]）");
    var segPenalty = args.Length > 5 && int.TryParse(args[5], out var sp) ? sp : 0;
    var segmenter = new SurfaceSegmenter(entries, connection, segmentPenalty: segPenalty);
    Console.Error.WriteLine($"segment-corpus: in={Path.GetFullPath(testSetPath)} → out={Path.GetFullPath(outputPath)}（maxLen={segmenter.MaxSurfaceLength}, segPenalty={segPenalty}）");

    long lines = 0;
    long totalTokens = 0;
    long fallbackTokens = 0;
    using (var writer = new StreamWriter(outputPath, append: false, System.Text.Encoding.UTF8))
    {
        foreach (var raw in File.ReadLines(testSetPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var result = segmenter.Segment(line);
            if (result.Tokens.Count == 0)
            {
                continue;
            }

            writer.WriteLine(string.Join(' ', result.Tokens));
            lines++;
            totalTokens += result.Tokens.Count;
            fallbackTokens += result.FallbackCount;
        }
    }

    var rate = totalTokens == 0 ? 0.0 : (double)fallbackTokens / totalTokens;
    Console.Error.WriteLine($"  {lines:N0} 文, {totalTokens:N0} トークン, フォールバック {fallbackTokens:N0}（{rate:P2}）");
}

// seg-check 本体：SurfaceSegmenter の分割と gold hypothesis（reading-lattice の Mozc 文節境界）の token 列を比較する。
// 比較集合は gold∈n-best のみ。token agreement（LCS ベースの recall）が word LM 成立の前提を決める最重要ゲート。
void SegCheck()
{
    const RomajiScheme scheme = RomajiScheme.Kunrei;
    const EvalInputMode mode = EvalInputMode.Full;
    var segPenalty = args.Length > 4 && int.TryParse(args[4], out var sp) ? sp : 0;
    var segmenter = new SurfaceSegmenter(entries, connection, segmentPenalty: segPenalty);
    Console.WriteLine($"seg-check: test={Path.GetFullPath(testSetPath)}（{cases.Count}文）, {scheme}/{mode}, n={NBest}, maxLen={segmenter.MaxSurfaceLength}, segPenalty={segPenalty}");

    var inNbest = 0;
    var exact = 0;
    long lcsSum = 0;
    long goldTokSum = 0;
    long segTokSum = 0;
    var shown = 0;
    foreach (var c in cases)
    {
        var input = harness.EncodeInput(c.Reading, scheme, mode);
        var nbest = converter.ConvertNBest(input, NBest);
        IReadOnlyList<Candidate>? goldSegs = null;
        foreach (var h in nbest)
        {
            if (h.Surface == c.Sentence)
            {
                goldSegs = h.Segments;
                break;
            }
        }

        if (goldSegs is null)
        {
            continue; // gold 圏外＝リランカー範囲外なので比較対象にしない。
        }

        inNbest++;
        var goldTokens = goldSegs.Select(s => s.Surface).ToList();
        var segTokens = segmenter.Segment(c.Sentence).Tokens;
        var lcs = LcsLength(goldTokens, segTokens);
        lcsSum += lcs;
        goldTokSum += goldTokens.Count;
        segTokSum += segTokens.Count;
        if (goldTokens.SequenceEqual(segTokens, StringComparer.Ordinal))
        {
            exact++;
        }
        else if (shown < 20)
        {
            Console.WriteLine($"  ≠ gold: {c.Sentence}");
            Console.WriteLine($"      mozc: {string.Join(" / ", goldTokens)}");
            Console.WriteLine($"      seg : {string.Join(" / ", segTokens)}");
            shown++;
        }
    }

    var exactRate = inNbest == 0 ? 0.0 : (double)exact / inNbest;
    var recall = goldTokSum == 0 ? 0.0 : (double)lcsSum / goldTokSum;     // gold トークンのうち順序通り再現された割合＝bigram 転移の鍵。
    var precision = segTokSum == 0 ? 0.0 : (double)lcsSum / segTokSum;
    var f1 = recall + precision == 0 ? 0.0 : 2 * recall * precision / (recall + precision);
    Console.WriteLine();
    Console.WriteLine($"gold∈n-best: {inNbest}/{cases.Count}");
    Console.WriteLine($"完全一致率: {exactRate:P1}（{exact}/{inNbest}）");
    Console.WriteLine($"token agreement: recall={recall:P1}, precision={precision:P1}, F1={f1:P1}");
    Console.WriteLine($"ゲート（recall ≥ 80%）: {(recall >= 0.80 ? "PASS（word LM へ進む）" : "FAIL（フォールバック粒度・正規化を見直して再検証）")}");
}

// 2つのトークン列の最長共通部分列長（順序を保った一致トークン数）。
int LcsLength(IReadOnlyList<string> a, IReadOnlyList<string> b)
{
    var dp = new int[b.Count + 1];
    for (var i = 1; i <= a.Count; i++)
    {
        var diag = 0;
        for (var j = 1; j <= b.Count; j++)
        {
            var tmp = dp[j];
            dp[j] = string.Equals(a[i - 1], b[j - 1], StringComparison.Ordinal) ? diag + 1 : Math.Max(dp[j], dp[j - 1]);
            diag = tmp;
        }
    }

    return dp[b.Count];
}

// tune-interp 本体：char+word の補間重み (λ_char, λ_word) を dev で 2D grid search する。
void TuneInterp()
{
    const RomajiScheme scheme = RomajiScheme.Kunrei;
    const EvalInputMode mode = EvalInputMode.Consonant;
    var charLm = WordNGramLm.Load(lmArg ?? throw new ArgumentException("tune-interp には char blob が必要（args[4]）"));
    var wordLm = WordNGramLm.Load(args.Length > 5 ? args[5] : throw new ArgumentException("tune-interp には word blob が必要（args[5]）"));
    Console.WriteLine($"tune-interp: dev={Path.GetFullPath(testSetPath)}（{cases.Count}文）, char={lmArg}, word={args[5]}, n={NBest}, {scheme}/{mode}");

    var cache = cases
        .Select(c => (Gold: c.Sentence, Nbest: converter.ConvertNBest(harness.EncodeInput(c.Reading, scheme, mode), NBest)))
        .ToList();

    double[] lambdas = [0.0, 50.0, 100.0, 300.0, 500.0, 1000.0];
    var baselineMrr = Mrr(cache, IdentityReranker.Instance);
    var bestMrr = -1.0;
    (double Char, double Word) best = default;
    foreach (var lc in lambdas)
    {
        foreach (var lw in lambdas)
        {
            var reranker = new LmReranker([(charLm, lc), (wordLm, lw)]);
            var mrr = Mrr(cache, reranker);
            if (mrr > bestMrr)
            {
                bestMrr = mrr;
                best = (lc, lw);
            }
        }
    }

    Console.WriteLine($"baseline(λ=0) MRR = {baselineMrr:F4}");
    Console.WriteLine($"best: λ_char={best.Char}, λ_word={best.Word}, MRR={bestMrr:F4}");
    Console.WriteLine($"改善: {(bestMrr > baselineMrr + 1e-9 ? "PASS（補間が baseline 超え）" : "FAIL（補間で改善なし）")}");
}

// misses 本体：訓令式・フルで gold が n-best 圏外の事例を列挙（ラティスで作れない＝活用ユニット欠落の確認）。
void DumpUnreachable()
{
    const RomajiScheme scheme = RomajiScheme.Kunrei;
    var unreachable = 0;
    Console.WriteLine($"== gold 圏外（訓令式・フル・{NBest}-best）＝ラティスで作れない事例 ==");
    foreach (var c in cases)
    {
        var input = harness.EncodeInput(c.Reading, scheme, 1.0, 0);
        var nbest = converter.ConvertNBest(input, NBest);
        if (nbest.Any(h => h.Surface == c.Sentence))
        {
            continue;
        }

        unreachable++;
        var top = nbest.Count > 0 ? nbest[0].Surface : "(変換失敗)";
        Console.WriteLine($"  ✗ {c.Sentence}〔{c.Reading}〕");
        Console.WriteLine($"      入力={input}  top1={top}");
    }

    Console.WriteLine();
    Console.WriteLine($"圏外: {unreachable}/{cases.Count}（{(double)unreachable / cases.Count:P0}）＝活用ユニット欠落等の上限。");
}

// reading-acc 本体：「子音→ひらがな」精度を keepRate スペクトラムで測る。各仮説の読みを文節読みの連結で復元し
// gold 読みと比較。漢字 top-1（参考）と並べ、読みを当てる難度が漢字より低い＝二段化の上限を示す。
void ReadingAccuracy()
{
    var seed = args.Length > 4 && int.TryParse(args[4], out var sd) ? sd : 0;
    double[] rates = [0.0, 0.25, 0.5, 0.75, 1.0];
    const RomajiScheme scheme = RomajiScheme.Kunrei;
    Console.WriteLine($"== reading-acc（訓令式・{NBest}-best・seed={seed}）: 子音→ひらがな 精度 ==");
    Console.WriteLine($"  {"keepRate",-10} {"読み top-1",10} {"読み∈n",8} {"漢字 top-1",10} {"漢字∈n",8}");
    foreach (var rate in rates)
    {
        int readTop1 = 0, readIn = 0, kanjiTop1 = 0, kanjiIn = 0;
        foreach (var c in cases)
        {
            var input = harness.EncodeInput(c.Reading, scheme, rate, seed);
            var nbest = converter.ConvertNBest(input, NBest);
            if (nbest.Count == 0)
            {
                continue;
            }

            var topReading = string.Concat(nbest[0].Segments.Select(s => s.Reading));
            if (topReading == c.Reading) { readTop1++; }
            if (nbest.Any(h => string.Concat(h.Segments.Select(s => s.Reading)) == c.Reading)) { readIn++; }
            if (nbest[0].Surface == c.Sentence) { kanjiTop1++; }
            if (nbest.Any(h => h.Surface == c.Sentence)) { kanjiIn++; }
        }

        var n = cases.Count;
        Console.WriteLine(
            $"  {EvaluationHarness.LabelFor(rate),-10} {(double)readTop1 / n,10:P0} {(double)readIn / n,8:P0} " +
            $"{(double)kanjiTop1 / n,10:P0} {(double)kanjiIn / n,8:P0}");
    }

    Console.WriteLine();
    Console.WriteLine("※ 読み top-1＝最尤仮説の文節読み連結が gold 読みに一致。漢字 top-1 との差＝「読みは合うが漢字を外す」分。");
}

// two-stage 本体：二段化の天井。oracle読み→漢字＝gold 読みを持つ仮説に絞った中で最尤（最小コスト）の漢字一致率。
// 「読みステージが完璧なら最終 top-1 はどこまで伸びるか」＝直接変換 top-1 との差が二段化の伸びしろ。
void TwoStageCeiling()
{
    var seed = args.Length > 4 && int.TryParse(args[4], out var sd) ? sd : 0;
    double[] rates = [0.0, 0.25, 0.5, 0.75, 1.0];
    const RomajiScheme scheme = RomajiScheme.Kunrei;
    Console.WriteLine($"== two-stage（訓令式・{NBest}-best・seed={seed}）: 二段化（子音→かな→漢字）の天井 ==");
    Console.WriteLine($"  {"keepRate",-10} {"直接 漢字t1",10} {"oracle読み→漢字t1",18} {"伸びしろ",8} {"読み∈n",8}");
    foreach (var rate in rates)
    {
        int direct = 0, twoStage = 0, readingIn = 0;
        foreach (var c in cases)
        {
            var input = harness.EncodeInput(c.Reading, scheme, rate, seed);
            var nbest = converter.ConvertNBest(input, NBest);
            if (nbest.Count == 0)
            {
                continue;
            }

            if (nbest[0].Surface == c.Sentence) { direct++; }

            // oracle 読み選択：gold 読みを持つ仮説に絞り、その中の最尤（コスト昇順の先頭）の漢字が gold か。
            var goldReadingHyps = nbest.Where(h => string.Concat(h.Segments.Select(s => s.Reading)) == c.Reading).ToList();
            if (goldReadingHyps.Count > 0)
            {
                readingIn++;
                if (goldReadingHyps[0].Surface == c.Sentence) { twoStage++; }
            }
        }

        var n = cases.Count;
        var gain = (double)(twoStage - direct) / n;
        Console.WriteLine(
            $"  {EvaluationHarness.LabelFor(rate),-10} {(double)direct / n,10:P0} {(double)twoStage / n,18:P0} " +
            $"{gain,8:+0%;-0%;0%} {(double)readingIn / n,8:P0}");
    }

    Console.WriteLine();
    Console.WriteLine("※ oracle読み→漢字＝読みステージが完璧（gold 読みを選択）なら到達できる最終 top-1。直接との差＝二段化の上限利得。");
    Console.WriteLine("※ ただし読みステージ自身の精度（読み top-1→読み∈n）が別途要る。読み∈n が実質的な読みステージの天井。");
}

// reading-rerank 本体：読み LM で n-best の読みを選び直す Stage A の実力測定。
// 各読み候補 r を score(r)=minCost(r)+λ·readingLM(r) で評価し最小を選ぶ。読み top-1 を λ 掃引で出し、
// oracle（読み∈n）天井への到達度と、選んだ読みでの二段化最終漢字 top-1 を直接変換と並べる。
void ReadingRerank()
{
    var readingLm = WordNGramLm.Load(lmArg ?? throw new ArgumentException("reading-rerank には reading.bin が必要（args[4]）"));
    var seed = args.Length > 5 && int.TryParse(args[5], out var sd) ? sd : 0;
    double[] rates = [0.0, 0.25, 0.5, 0.75, 1.0];
    double[] lambdas = [0.0, 50.0, 100.0, 300.0, 1000.0];
    const RomajiScheme scheme = RomajiScheme.Kunrei;

    double Rlm(string reading) => readingLm.NegLogProb(reading.Select(c => c.ToString()), "");

    Console.WriteLine($"== reading-rerank（訓令式・{NBest}-best・seed={seed}）: 読み LM で Stage A（子音→かな）===");
    Console.WriteLine($"  {"keepRate",-10} {"直接漢字",8} {"読みt1 λ=0",10}" +
        string.Concat(lambdas.Skip(1).Select(l => $" {"λ=" + l,9}")) + $" {"読み∈n",8} {"2段最終",8}");
    foreach (var rate in rates)
    {
        var n = cases.Count;
        var direct = 0;
        var readingIn = 0;
        var readT1 = new int[lambdas.Length];
        var bestLambdaForFinal = 300.0;
        var twoStageFinal = 0;
        foreach (var c in cases)
        {
            var input = harness.EncodeInput(c.Reading, scheme, rate, seed);
            var nbest = converter.ConvertNBest(input, NBest);
            if (nbest.Count == 0)
            {
                continue;
            }

            if (nbest[0].Surface == c.Sentence) { direct++; }

            // 読みごとに最小コストと LM スコアを集約。
            var byReading = new Dictionary<string, (long MinCost, List<Hypothesis> Hyps)>();
            foreach (var h in nbest)
            {
                var r = string.Concat(h.Segments.Select(s => s.Reading));
                if (!byReading.TryGetValue(r, out var agg))
                {
                    agg = (long.MaxValue, []);
                }

                agg.Hyps.Add(h);
                if (h.Cost < agg.MinCost) { agg.MinCost = h.Cost; }
                byReading[r] = agg;
            }

            if (byReading.ContainsKey(c.Reading)) { readingIn++; }

            var rlmCache = byReading.Keys.ToDictionary(r => r, Rlm);
            for (var li = 0; li < lambdas.Length; li++)
            {
                var lambda = lambdas[li];
                var pick = byReading.Keys.OrderBy(r => byReading[r].MinCost + lambda * rlmCache[r]).First();
                if (pick == c.Reading) { readT1[li]++; }
                if (lambda == bestLambdaForFinal && li == Array.IndexOf(lambdas, bestLambdaForFinal))
                {
                    // 二段化最終：選んだ読みの中で最尤（最小コスト）の漢字が gold か。
                    var bestHyp = byReading[pick].Hyps.OrderBy(h => h.Cost).First();
                    if (bestHyp.Surface == c.Sentence) { twoStageFinal++; }
                }
            }
        }

        Console.Write($"  {EvaluationHarness.LabelFor(rate),-10} {(double)direct / n,8:P0} {(double)readT1[0] / n,10:P0}");
        for (var li = 1; li < lambdas.Length; li++)
        {
            Console.Write($" {(double)readT1[li] / n,9:P0}");
        }

        Console.WriteLine($" {(double)readingIn / n,8:P0} {(double)twoStageFinal / n,8:P0}");
    }

    Console.WriteLine();
    Console.WriteLine("※ 読みt1 λ=0＝最小コスト読み（現状）。λ>0＝読み LM 加味。読み∈n＝oracle 天井。2段最終＝λ=300 で選んだ読みでの漢字 top-1。");
}

// eval-lgbm 本体：LightGBM ランカー vs cw を全スペクトラムで比較（最終ゲート）。seed を重視。
void EvalLgbm()
{
    var charLm = WordNGramLm.Load(lmArg ?? throw new ArgumentException("char.bin が必要（args[4]）"));
    var wordLm = WordNGramLm.Load(args.Length > 5 ? args[5] : throw new ArgumentException("word.bin が必要（args[5]）"));
    var readingLm = WordNGramLm.Load(args.Length > 6 ? args[6] : throw new ArgumentException("reading.bin が必要（args[6]）"));
    var model = GradientBoostedTrees.Load(args.Length > 7 ? args[7] : throw new ArgumentException("ranker.txt が必要（args[7]）"));
    var lc = args.Length > 8 && double.TryParse(args[8], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pc) ? pc : 50.0;
    var lw = args.Length > 9 && double.TryParse(args[9], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pw) ? pw : 500.0;
    var seed = args.Length > 10 && int.TryParse(args[10], out var sd) ? sd : 0;
    double[] rates = [0.0, 0.25, 0.5, 0.75, 1.0];
    const RomajiScheme scheme = RomajiScheme.Kunrei;
    var cw = new LmReranker([new LmReranker.Component(charLm, lc), new LmReranker.Component(wordLm, lw)]);
    var lgbm = new LgbmReranker(model, charLm, wordLm, readingLm);

    Console.WriteLine($"== eval-lgbm（訓令式・{NBest}-best・seed={seed}）: LightGBM ランカー vs cw ==");
    Console.WriteLine($"  {"keepRate",-10} {"cw t1",7} {"lgbm t1",8} {"Δt1",6} {"cw MRR",7} {"lgbm MRR",9} {"gold∈n",7}");
    foreach (var rate in rates)
    {
        var cache = cases
            .Select(c => (Gold: c.Sentence, Nbest: converter.ConvertNBest(harness.EncodeInput(c.Reading, scheme, rate, seed), NBest)))
            .ToList();
        var cwT1 = Top1(cache, cw);
        var lgT1 = Top1(cache, lgbm);
        var goldRecall = cache.Count == 0 ? 0.0 : (double)cache.Count(x => x.Nbest.Any(h => h.Surface == x.Gold)) / cache.Count;
        Console.WriteLine(
            $"  {EvaluationHarness.LabelFor(rate),-10} {cwT1,7:P0} {lgT1,8:P0} {lgT1 - cwT1,6:+0%;-0%;0%} {Mrr(cache, cw),7:F3} {Mrr(cache, lgbm),9:F3} {goldRecall,7:P0}");
    }

    Console.WriteLine();
    Console.WriteLine("※ lgbm−cw が LightGBM の正味効果。seed を重視（generated は n-gram/テンプレの best case）。gold∈n が top-1 の天井。");
}

// gen-train 本体：LightGBM ランカーの学習データを出力。(文×keepRate)＝1群、各仮説に RankingFeatures と gold ラベル。
void GenTrain()
{
    var charLm = WordNGramLm.Load(lmArg ?? throw new ArgumentException("char.bin が必要（args[4]）"));
    var wordLm = WordNGramLm.Load(args.Length > 5 ? args[5] : throw new ArgumentException("word.bin が必要（args[5]）"));
    var readingLm = WordNGramLm.Load(args.Length > 6 ? args[6] : throw new ArgumentException("reading.bin が必要（args[6]）"));
    var basePath = args.Length > 7 ? args[7] : throw new ArgumentException("出力 base が必要（args[7]）");
    var seed = args.Length > 8 && int.TryParse(args[8], out var sd) ? sd : 0;
    double[] rates = [0.0, 0.5, 1.0];
    const RomajiScheme scheme = RomajiScheme.Kunrei;

    var rowsPath = basePath + ".tsv";
    var groupPath = basePath + ".group";
    long groups = 0, rows = 0, positives = 0, skippedNoGold = 0;
    var ci = System.Globalization.CultureInfo.InvariantCulture;

    using (var rowsWriter = new StreamWriter(rowsPath, append: false))
    using (var groupWriter = new StreamWriter(groupPath, append: false))
    {
        foreach (var c in cases)
        {
            foreach (var rate in rates)
            {
                var input = harness.EncodeInput(c.Reading, scheme, rate, seed);
                var nbest = converter.ConvertNBest(input, NBest);
                if (nbest.Count == 0 || !nbest.Any(h => h.Surface == c.Sentence))
                {
                    skippedNoGold++;
                    continue; // 正例なし＝学習信号なし。
                }

                var feats = RankingFeatures.ExtractGroup(nbest, charLm, wordLm, readingLm, "");
                for (var i = 0; i < nbest.Count; i++)
                {
                    var label = nbest[i].Surface == c.Sentence ? 1 : 0;
                    positives += label;
                    rowsWriter.Write(label);
                    foreach (var v in feats[i])
                    {
                        rowsWriter.Write('\t');
                        rowsWriter.Write(v.ToString("R", ci));
                    }

                    rowsWriter.Write('\n');
                    rows++;
                }

                groupWriter.Write(nbest.Count);
                groupWriter.Write('\n');
                groups++;
            }
        }
    }

    Console.WriteLine($"gen-train: train={Path.GetFullPath(testSetPath)}（{cases.Count}文）, keepRates=[{string.Join(",", rates)}], seed={seed}");
    Console.WriteLine($"  群={groups:N0}（gold無で除外 {skippedNoGold:N0}）, 行={rows:N0}, 正例={positives:N0}");
    Console.WriteLine($"  特徴={RankingFeatures.Count}（{string.Join(",", RankingFeatures.Names)}）");
    Console.WriteLine($"  → {rowsPath} / {groupPath}");
}

// tune-skip 本体：vowelSkipPenalty を掃引し cw リランカー top-1 を keepRate 別に測る。
// の(0 skip) vs なお(1 skip) のような「打った母音を尊重する度合い」の調整。trie/connection は共有。
void TuneSkip()
{
    var charLm = WordNGramLm.Load(lmArg ?? throw new ArgumentException("char.bin が必要（args[4]）"));
    var wordLm = WordNGramLm.Load(args.Length > 5 ? args[5] : throw new ArgumentException("word.bin が必要（args[5]）"));
    var lc = args.Length > 6 && double.TryParse(args[6], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pc) ? pc : 50.0;
    var lw = args.Length > 7 && double.TryParse(args[7], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pw) ? pw : 500.0;
    var seed = args.Length > 8 && int.TryParse(args[8], out var sd) ? sd : 0;
    int[] penalties = [0, 250, 500, 1000, 2000, 4000];
    double[] rates = [0.0, 0.5, 1.0];
    const RomajiScheme scheme = RomajiScheme.Kunrei;
    var cw = new LmReranker([new LmReranker.Component(charLm, lc), new LmReranker.Component(wordLm, lw)]);

    Console.WriteLine($"== tune-skip（訓令式・{NBest}-best・seed={seed}・cw λ={lc}/{lw}）: vowelSkipPenalty 掃引（cw top-1）==");
    Console.WriteLine($"  {"skip\\keep",-10}" + string.Concat(rates.Select(r => $" {EvaluationHarness.LabelFor(r),10}")));
    foreach (var pen in penalties)
    {
        var conv = new PhraseConverter(trie, connection, SegmentPenalty, pen);
        Console.Write($"  {pen,-10}");
        foreach (var rate in rates)
        {
            var cache = cases
                .Select(c => (Gold: c.Sentence, Nbest: conv.ConvertNBest(harness.EncodeInput(c.Reading, scheme, rate, seed), NBest)))
                .ToList();
            Console.Write($" {Top1(cache, cw),10:P0}");
        }

        Console.WriteLine();
    }

    Console.WriteLine();
    Console.WriteLine("※ 高 penalty＝打った母音を尊重（の>なお）。純子音はスキップ必須で劣化、フル寄りで利得。seed p=0.5 の純利得で判断。");
}

// tune-reading 本体：読みを第3成分に足す λ_reading を dev の指定 keepRate で掃引（λ_char/λ_word 固定）。
void TuneReading()
{
    var charLm = WordNGramLm.Load(lmArg ?? throw new ArgumentException("char.bin が必要（args[4]）"));
    var wordLm = WordNGramLm.Load(args.Length > 5 ? args[5] : throw new ArgumentException("word.bin が必要（args[5]）"));
    var readingLm = WordNGramLm.Load(args.Length > 6 ? args[6] : throw new ArgumentException("reading.bin が必要（args[6]）"));
    var lc = args.Length > 7 && double.TryParse(args[7], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pc) ? pc : 50.0;
    var lw = args.Length > 8 && double.TryParse(args[8], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pw) ? pw : 500.0;
    var rate = args.Length > 9 && double.TryParse(args[9], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pr) ? pr : 0.5;
    const RomajiScheme scheme = RomajiScheme.Kunrei;
    Console.WriteLine($"tune-reading: dev={Path.GetFullPath(testSetPath)}（{cases.Count}文）, keepRate={rate}, λ_char={lc}, λ_word={lw}, n={NBest}");

    var cache = cases
        .Select(c => (Gold: c.Sentence, Nbest: converter.ConvertNBest(harness.EncodeInput(c.Reading, scheme, rate, 0), NBest)))
        .ToList();

    double[] lambdas = [0.0, 50.0, 100.0, 300.0, 1000.0, 2000.0];
    var baseMrr = Mrr(cache, new LmReranker([new LmReranker.Component(charLm, lc), new LmReranker.Component(wordLm, lw)]));
    Console.WriteLine($"baseline(cw, λ_reading=0) MRR = {baseMrr:F4}");
    var bestMrr = baseMrr;
    var bestLr = 0.0;
    foreach (var lr in lambdas)
    {
        var r = new LmReranker([new LmReranker.Component(charLm, lc), new LmReranker.Component(wordLm, lw), new LmReranker.Component(readingLm, lr, true)]);
        var mrr = Mrr(cache, r);
        Console.WriteLine($"  λ_reading={lr,-6} MRR={mrr:F4} top1={Top1(cache, r):P0}");
        if (mrr > bestMrr) { bestMrr = mrr; bestLr = lr; }
    }

    Console.WriteLine($"best λ_reading={bestLr}, MRR={bestMrr:F4}（{(bestLr > 0 ? "読み feature が改善" : "改善なし")}）");
}

// eval-reading 本体：cw（char+word）vs cwr（+reading feature）を全スペクトラムで比較（決定実験）。
void EvalReading()
{
    var charLm = WordNGramLm.Load(lmArg ?? throw new ArgumentException("char.bin が必要（args[4]）"));
    var wordLm = WordNGramLm.Load(args.Length > 5 ? args[5] : throw new ArgumentException("word.bin が必要（args[5]）"));
    var readingLm = WordNGramLm.Load(args.Length > 6 ? args[6] : throw new ArgumentException("reading.bin が必要（args[6]）"));
    var lc = args.Length > 7 && double.TryParse(args[7], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pc) ? pc : 50.0;
    var lw = args.Length > 8 && double.TryParse(args[8], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pw) ? pw : 500.0;
    var lr = args.Length > 9 && double.TryParse(args[9], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pr) ? pr : 300.0;
    var seed = args.Length > 10 && int.TryParse(args[10], out var sd) ? sd : 0;
    double[] rates = [0.0, 0.25, 0.5, 0.75, 1.0];
    const RomajiScheme scheme = RomajiScheme.Kunrei;
    var cw = new LmReranker([new LmReranker.Component(charLm, lc), new LmReranker.Component(wordLm, lw)]);
    var cwr = new LmReranker([new LmReranker.Component(charLm, lc), new LmReranker.Component(wordLm, lw), new LmReranker.Component(readingLm, lr, true)]);

    Console.WriteLine($"== eval-reading（訓令式・{NBest}-best・seed={seed}・λ_char={lc}/λ_word={lw}/λ_reading={lr}）==");
    Console.WriteLine($"  {"keepRate",-10} {"cw t1",7} {"cwr t1",7} {"Δt1",6} {"cw MRR",7} {"cwr MRR",8}");
    foreach (var rate in rates)
    {
        var cache = cases
            .Select(c => (Gold: c.Sentence, Nbest: converter.ConvertNBest(harness.EncodeInput(c.Reading, scheme, rate, seed), NBest)))
            .ToList();
        var cwT1 = Top1(cache, cw);
        var cwrT1 = Top1(cache, cwr);
        Console.WriteLine(
            $"  {EvaluationHarness.LabelFor(rate),-10} {cwT1,7:P0} {cwrT1,7:P0} {cwrT1 - cwT1,6:+0%;-0%;0%} {Mrr(cache, cw),7:F3} {Mrr(cache, cwr),8:F3}");
    }

    Console.WriteLine();
    Console.WriteLine("※ cwr−cw が読み feature の正味効果。seed を重視（generated は n-gram の best case）。");
}

// incremental 本体：逐次文節確定（確定左文脈）を一発全文 cw と比較。無人 greedy（公平）＋候補UI oracle@k。
void Incremental()
{
    var charLm = WordNGramLm.Load(lmArg ?? throw new ArgumentException("char.bin が必要（args[4]）"));
    var wordLm = WordNGramLm.Load(args.Length > 5 ? args[5] : throw new ArgumentException("word.bin が必要（args[5]）"));
    var lc = args.Length > 6 && double.TryParse(args[6], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pc) ? pc : 50.0;
    var lw = args.Length > 7 && double.TryParse(args[7], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pw) ? pw : 500.0;
    var topK = args.Length > 8 && int.TryParse(args[8], out var tk) ? tk : 5;
    var rate = args.Length > 9 && double.TryParse(args[9], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pr) ? pr : 0.5;
    var seed = args.Length > 10 && int.TryParse(args[10], out var sd) ? sd : 0;
    // segmentPenalty は逐次レジームの要 knob（全文 DP の EOS+大域均衡を肩代わり＝短断片の過分割を抑える）。
    // gold 分割・一発・逐次を同一 segPenalty で揃え regime 効果を統制比較する。args[11]（既定 3000）。
    var segPen = args.Length > 11 && int.TryParse(args[11], out var sp) ? sp : 3000;
    var mode = args.Length > 12 ? args[12].ToLowerInvariant() : "lookahead"; // lookahead | single
    const RomajiScheme scheme = RomajiScheme.Kunrei;
    LmReranker.Component[] comps = [new LmReranker.Component(charLm, lc), new LmReranker.Component(wordLm, lw)];
    var cw = new LmReranker(comps);
    var expConv = new PhraseConverter(trie, connection, segPen, VowelSkipPenalty);
    INextSegmentSource source = mode == "single"
        ? new IncrementalConverter(trie, connection, new LmStepScorer(comps), segPen, VowelSkipPenalty)
        : new LookaheadConverter(expConv, comps, NBest);
    var sim = new IncrementalSimulator(source);

    int considered = 0, goldOut = 0, oneShot = 0, greedyExact = 0, greedyDead = 0, oracleAll = 0;
    long stepHits = 0, stepTotal = 0;
    foreach (var c in cases)
    {
        var input = harness.EncodeInput(c.Reading, scheme, rate, seed);
        if (input.Length == 0)
        {
            continue;
        }

        var nbest = expConv.ConvertNBest(input, NBest);
        var goldHyp = nbest.FirstOrDefault(h => h.Surface == c.Sentence);
        if (goldHyp is null)
        {
            goldOut++;
            continue; // gold∈n-best のみ対象（seg-check と同じ分母）。同一 segPenalty で gold 分割も得る。
        }

        considered++;
        if (cw.Rerank(input, "", nbest) is { Count: > 0 } r && r[0].Surface == c.Sentence)
        {
            oneShot++;
        }

        var g = sim.RunGreedy(input, c.Sentence);
        if (g.ExactMatch) { greedyExact++; }
        if (g.DeadEnd) { greedyDead++; }

        var o = sim.RunOracleTopK(input, goldHyp.Segments, goldHyp.SegmentLengths ?? [], topK);
        if (o.AllHit) { oracleAll++; }
        stepHits += o.Hits;
        stepTotal += o.Steps;
    }

    double Pct(int x) => considered == 0 ? 0.0 : (double)x / considered;
    Console.WriteLine($"== incremental[{mode}]（訓令式・cw λ={lc}/{lw}・{NBest}-best・topK={topK}・segPen={segPen}・keepRate={rate}・seed={seed}・{Path.GetFileName(testSetPath)}）==");
    Console.WriteLine($"  対象（gold∈n-best）: {considered}（gold圏外で除外 {goldOut}）");
    Console.WriteLine($"  一発 cw top-1            : {Pct(oneShot),6:P0}（{oneShot}/{considered}）");
    Console.WriteLine($"  逐次 greedy 全文一致(無人): {Pct(greedyExact),6:P0}（{greedyExact}/{considered}） dead-end {Pct(greedyDead),5:P0}");
    Console.WriteLine($"  逐次 oracle@{topK} 全step命中(候補UI): {Pct(oracleAll),6:P0}（{oracleAll}/{considered}） per-step {(stepTotal == 0 ? 0 : (double)stepHits / stepTotal),5:P0}");
    Console.WriteLine();
    Console.WriteLine("※ 無人比較は『逐次greedy全文一致 vs 一発cw top-1』（apples-to-apples）。oracle@k は step毎kショットで構造的に逐次有利＝候補UI成功率の上限の目安。");
}

// diagnose 本体：cw の誤りを「到達性欠落(beam/構造)」「順位ミス(同音異字/読み違い/記号数字)」に分類し伸びしろの所在を特定。
void Diagnose()
{
    var charLm = WordNGramLm.Load(lmArg ?? throw new ArgumentException("char.bin が必要（args[4]）"));
    var wordLm = WordNGramLm.Load(args.Length > 5 ? args[5] : throw new ArgumentException("word.bin が必要（args[5]）"));
    var lc = args.Length > 6 && double.TryParse(args[6], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pc) ? pc : 50.0;
    var lw = args.Length > 7 && double.TryParse(args[7], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pw) ? pw : 500.0;
    var rate = args.Length > 8 && double.TryParse(args[8], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pr) ? pr : 0.5;
    var seed = args.Length > 9 && int.TryParse(args[9], out var sd) ? sd : 0;
    const RomajiScheme scheme = RomajiScheme.Kunrei;
    const int Deep = 500;
    var cw = new LmReranker([new LmReranker.Component(charLm, lc), new LmReranker.Component(wordLm, lw)]);

    int total = 0, correct = 0, reachBeam = 0, reachStructural = 0, rankHomograph = 0, rankReadingDiff = 0, rankNumSym = 0;
    var structuralEx = new List<string>();
    var homographEx = new List<string>();
    foreach (var c in cases)
    {
        var input = harness.EncodeInput(c.Reading, scheme, rate, seed);
        var nbest = converter.ConvertNBest(input, NBest);
        var reranked = cw.Rerank(input, "", nbest);
        total++;
        if (reranked.Count > 0 && reranked[0].Surface == c.Sentence)
        {
            correct++;
            continue;
        }

        var goldHyp = nbest.FirstOrDefault(h => h.Surface == c.Sentence);
        if (goldHyp is null)
        {
            // 到達性欠落：n=NBest に gold 無し。n=Deep で入れば beam、入らねば構造（語彙/格子）。
            var deep = converter.ConvertNBest(input, Deep);
            if (deep.Any(h => h.Surface == c.Sentence))
            {
                reachBeam++;
            }
            else
            {
                reachStructural++;
                if (structuralEx.Count < 6) { structuralEx.Add($"{c.Sentence}〔{c.Reading}〕"); }
            }
        }
        else
        {
            // 順位ミス：gold∈n だが top1 でない。同音異字（読み一致）/読み違い/記号数字に分類。
            var goldReading = string.Concat(goldHyp.Segments.Select(s => s.Reading));
            var top = reranked[0];
            var topReading = string.Concat(top.Segments.Select(s => s.Reading));
            if (goldReading == topReading)
            {
                rankHomograph++;
                if (homographEx.Count < 6) { homographEx.Add($"{c.Sentence} ← {top.Surface}〔{goldReading}〕"); }
            }
            else if (top.Surface.Any(ch => ch < 128))
            {
                rankNumSym++;
            }
            else
            {
                rankReadingDiff++;
            }
        }
    }

    double Pct(int x) => total == 0 ? 0.0 : (double)x / total;
    Console.WriteLine($"== diagnose（訓令式・cw λ={lc}/{lw}・{NBest}-best・keepRate={rate}・seed={seed}・{Path.GetFileName(testSetPath)}）==");
    Console.WriteLine($"  総数 {total}, 正解 {correct}（{Pct(correct):P0}）, 誤り {total - correct}");
    Console.WriteLine($"  到達性欠落: beam(n={Deep}で入る) {reachBeam}（{Pct(reachBeam):P0}） / 構造(語彙·格子) {reachStructural}（{Pct(reachStructural):P0}）");
    Console.WriteLine($"  順位ミス(gold∈n): 同音異字 {rankHomograph}（{Pct(rankHomograph):P0}） / 読み違い {rankReadingDiff}（{Pct(rankReadingDiff):P0}） / 記号数字 {rankNumSym}（{Pct(rankNumSym):P0}）");
    if (structuralEx.Count > 0) { Console.WriteLine($"  [構造欠落例] {string.Join(" / ", structuralEx)}"); }
    if (homographEx.Count > 0) { Console.WriteLine($"  [同音異字例] {string.Join(" / ", homographEx)}"); }
    Console.WriteLine("※ 構造欠落＝語彙/活用カバレッジの余地。読み違い＝逐次確定（左文脈）が効きうる順位ギャップ。同音異字/記号＝コスト/LM では切りにくい。");
}

// spectrum 本体：母音保持率の連続軸で baseline（identity）を測る。実打鍵の中間混在の精度を可視化する。
void Spectrum()
{
    var seed = args.Length > 4 && int.TryParse(args[4], out var sd) ? sd : 0;
    double[] rates = [0.0, 0.25, 0.5, 0.75, 1.0];
    RomajiScheme[] schemes = [RomajiScheme.Kunrei, RomajiScheme.Hepburn];
    IReadOnlyList<string> Convert(string input) => converter.ConvertNBest(input, NBest).Select(h => h.Surface).ToList();

    Console.WriteLine($"== spectrum（identity・{NBest}-best・seed={seed}・seg={SegmentPenalty}/skip={VowelSkipPenalty}）: 母音保持率 keepRate ==");
    Console.WriteLine($"  {"プロファイル",-20} {"top-1",7} {"top-5",7} {"top-10",7} {"gold∈n-best",12} {"MRR",6}");
    foreach (var scheme in schemes)
    {
        foreach (var rate in rates)
        {
            var r = harness.Run(cases, scheme, rate, seed, Convert);
            var goldRecall = r.Total == 0 ? 0.0 : (double)r.Cases.Count(c => c.Rank >= 1) / r.Total;
            Console.WriteLine(
                $"  {scheme + "/" + r.InputLabel,-20} {r.Top1Accuracy,7:P0} {r.TopKAccuracy(5),7:P0} " +
                $"{r.TopKAccuracy(10),7:P0} {goldRecall,12:P0} {r.Mrr,6:F3}");
        }
    }

    Console.WriteLine();
    Console.WriteLine("※ keepRate=0 は子音のみ（=Consonant）、1 はフル（=Full）。中間は OPTIONAL 母音をモーラ単位で混在保持（合成的仮定）。");
}

// eval-interp 本体：identity / char / word / char+word を seed/generated 全プロファイルで横並び比較する。
void EvalInterp()
{
    var charLm = WordNGramLm.Load(lmArg ?? throw new ArgumentException("eval-interp には char blob が必要（args[4]）"));
    var wordLm = WordNGramLm.Load(args.Length > 5 ? args[5] : throw new ArgumentException("eval-interp には word blob が必要（args[5]）"));
    var lambdaChar = args.Length > 6 && double.TryParse(args[6], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lc) ? lc : 0.0;
    var lambdaWord = args.Length > 7 && double.TryParse(args[7], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lw) ? lw : 0.0;
    (RomajiScheme Scheme, EvalInputMode Mode)[] evalProfiles =
    [
        (RomajiScheme.Kunrei, EvalInputMode.Consonant),
        (RomajiScheme.Kunrei, EvalInputMode.Full),
        (RomajiScheme.Hepburn, EvalInputMode.Consonant),
        (RomajiScheme.Hepburn, EvalInputMode.Full),
    ];
    Console.WriteLine($"eval-interp: test={Path.GetFullPath(testSetPath)}（{cases.Count}文）, char={lmArg}(λ={lambdaChar}), word={args[5]}(λ={lambdaWord}), n={NBest}");
    Console.WriteLine($"  {"プロファイル",-20} {"id t1",6} {"ch t1",6} {"wd t1",6} {"cw t1",6}  {"id MRR",7} {"ch MRR",7} {"wd MRR",7} {"cw MRR",7} {"gold∈n",7}");
    foreach (var (scheme, mode) in evalProfiles)
    {
        var cache = cases
            .Select(c => (Gold: c.Sentence, Nbest: converter.ConvertNBest(harness.EncodeInput(c.Reading, scheme, mode), NBest)))
            .ToList();
        var charReranker = new LmReranker(charLm, lambdaChar);
        var wordReranker = new LmReranker(wordLm, lambdaWord);
        var interpReranker = new LmReranker([(charLm, lambdaChar), (wordLm, lambdaWord)]);
        var goldRecall = cache.Count == 0 ? 0.0 : (double)cache.Count(x => x.Nbest.Any(h => h.Surface == x.Gold)) / cache.Count;
        Console.WriteLine(
            $"  {scheme + "/" + mode,-20} {Top1(cache, IdentityReranker.Instance),6:P0} {Top1(cache, charReranker),6:P0} {Top1(cache, wordReranker),6:P0} {Top1(cache, interpReranker),6:P0}  " +
            $"{Mrr(cache, IdentityReranker.Instance),7:F3} {Mrr(cache, charReranker),7:F3} {Mrr(cache, wordReranker),7:F3} {Mrr(cache, interpReranker),7:F3} {goldRecall,7:P0}");
    }
}
