namespace ShortcutIme.Core;

/// <summary>
/// LightGBM ランカー（Stage 1.5）の特徴抽出。<b>学習データ生成と実行時推論で同一のロジック</b>を使い、
/// train/serve skew を防ぐ。1つの n-best グループ（同一入力の候補群）に対し、各仮説の特徴ベクトルを返す。
/// 群相対特徴（最良候補との差）は LambdaRank が群内順位を学ぶのに効く。特徴の<b>順序は固定</b>（model.txt と一致）。
/// </summary>
public static class RankingFeatures
{
    /// <summary>特徴名（順序＝ベクトルのインデックス。学習・推論・診断で共有）。</summary>
    public static readonly string[] Names =
    [
        "cost", "charLP", "wordLP", "readingLP", "segCount", "surfaceLen", "readingLen", "avgWordCost",
        "costMinusBest", "charLPMinusBest", "wordLPMinusBest", "readingLPMinusBest",
        "cwScore", "cwScoreMinusBest",
    ];

    /// <summary>cwScore を構成する既定の補間重み（製品 cw リランカーと同一）。</summary>
    public const double DefaultCwLambdaChar = 50.0;

    /// <summary>cwScore を構成する既定の補間重み（製品 cw リランカーと同一）。</summary>
    public const double DefaultCwLambdaWord = 500.0;

    /// <summary>特徴数。</summary>
    public static int Count => Names.Length;

    /// <summary>n-best 群の各仮説の特徴ベクトルを返す（群相対特徴つき）。LM が null の項は 0。</summary>
    /// <remarks><c>cwScore</c>＝<c>cost + λ_char·charLP + λ_word·wordLP</c>（製品 cw リランカーのスコア）を特徴に含める。
    /// GBDT が cw を入力として持っても cw を超えられるかを検証するため（超えられなければ「新信号なし」が確定）。</remarks>
    public static double[][] ExtractGroup(
        IReadOnlyList<Hypothesis> group,
        WordNGramLm? charLm,
        WordNGramLm? wordLm,
        WordNGramLm? readingLm,
        string leftContext,
        double cwLambdaChar = DefaultCwLambdaChar,
        double cwLambdaWord = DefaultCwLambdaWord)
    {
        ArgumentNullException.ThrowIfNull(group);

        var n = group.Count;
        var features = new double[n][];
        var minCost = double.MaxValue;
        var minChar = double.MaxValue;
        var minWord = double.MaxValue;
        var minReading = double.MaxValue;
        var minCw = double.MaxValue;

        for (var i = 0; i < n; i++)
        {
            var h = group[i];
            var cost = (double)h.Cost;
            var charLp = charLm?.NegLogProb(h.Segments, leftContext) ?? 0.0;
            var wordLp = wordLm?.NegLogProb(h.Segments, leftContext) ?? 0.0;
            var readingLp = readingLm?.NegLogProbReading(h.Segments, leftContext) ?? 0.0;
            var segCount = h.Segments.Count;
            var surfaceLen = 0;
            var readingLen = 0;
            long wordCostSum = 0;
            foreach (var s in h.Segments)
            {
                surfaceLen += s.Surface.Length;
                readingLen += s.Reading.Length;
                wordCostSum += s.Cost;
            }

            var avgWordCost = segCount > 0 ? (double)wordCostSum / segCount : 0.0;
            var cwScore = cost + (cwLambdaChar * charLp) + (cwLambdaWord * wordLp); // 製品 cw のスコア。

            // 群相対の差(8..11,13)は後で埋めるためプレースホルダ。
            features[i] = [cost, charLp, wordLp, readingLp, segCount, surfaceLen, readingLen, avgWordCost, 0, 0, 0, 0, cwScore, 0];

            if (cost < minCost) { minCost = cost; }
            if (charLp < minChar) { minChar = charLp; }
            if (wordLp < minWord) { minWord = wordLp; }
            if (readingLp < minReading) { minReading = readingLp; }
            if (cwScore < minCw) { minCw = cwScore; }
        }

        for (var i = 0; i < n; i++)
        {
            var f = features[i];
            f[8] = f[0] - minCost;
            f[9] = f[1] - minChar;
            f[10] = f[2] - minWord;
            f[11] = f[3] - minReading;
            f[13] = f[12] - minCw;
        }

        return features;
    }
}
