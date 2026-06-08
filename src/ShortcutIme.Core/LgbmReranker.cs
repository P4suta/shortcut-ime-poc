namespace ShortcutIme.Core;

/// <summary>
/// LightGBM ランカー（Stage 1.5）。n-best 群の各仮説から <see cref="RankingFeatures"/> を抽出し、
/// <see cref="GradientBoostedTrees"/>（lambdarank で学習）のスコア降順に並べ替える。LM は特徴計算に使う
/// （char/word/reading、null 可）。スコア同値は安定ソートで元の n-best 順（WFST コスト順）を保つ。
/// </summary>
public sealed class LgbmReranker : IReranker
{
    private readonly GradientBoostedTrees _model;
    private readonly WordNGramLm? _charLm;
    private readonly WordNGramLm? _wordLm;
    private readonly WordNGramLm? _readingLm;

    public LgbmReranker(GradientBoostedTrees model, WordNGramLm? charLm, WordNGramLm? wordLm, WordNGramLm? readingLm)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _charLm = charLm;
        _wordLm = wordLm;
        _readingLm = readingLm;
    }

    /// <inheritdoc />
    public IReadOnlyList<Hypothesis> Rerank(string input, string leftContext, IReadOnlyList<Hypothesis> hypotheses)
    {
        ArgumentNullException.ThrowIfNull(hypotheses);
        _ = input;
        if (hypotheses.Count <= 1)
        {
            return hypotheses;
        }

        var features = RankingFeatures.ExtractGroup(hypotheses, _charLm, _wordLm, _readingLm, leftContext);
        var scored = new (Hypothesis Hyp, double Score)[hypotheses.Count];
        for (var i = 0; i < hypotheses.Count; i++)
        {
            scored[i] = (hypotheses[i], _model.Evaluate(features[i]));
        }

        // lambdarank は大きいほど上位。安定ソートで同値は元順（コスト順）維持。
        return scored.OrderByDescending(s => s.Score).Select(s => s.Hyp).ToList();
    }
}
