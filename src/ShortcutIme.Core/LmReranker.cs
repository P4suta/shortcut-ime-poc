namespace ShortcutIme.Core;

/// <summary>
/// n-gram LM で n-best を加算リスコアするリランカー。経路コスト <see cref="Hypothesis.Cost"/>（WFST＝POS-bigram を
/// 内包）に Σ λ_i·LM_i を足して並べ替える。LM は純粋な加算項で、全 λ=0 は identity 等価。複数 LM を渡すと
/// 線形補間（例：char+word+reading）になる。各成分は表層採点（<see cref="WordNGramLm.NegLogProb"/>）か
/// 読み採点（<see cref="WordNGramLm.NegLogProbReading"/>）を選べる。安定ソートのため同スコアは元の n-best 順を保つ。
/// </summary>
public sealed class LmReranker : IReranker
{
    /// <summary>リランカーの一成分。<paramref name="OverReading"/>=true なら文節の読みで採点する。</summary>
    public readonly record struct Component(WordNGramLm Lm, double Lambda, bool OverReading = false);

    private readonly Component[] _components;

    /// <summary>単一 LM で加算リスコアする（表層採点）。</summary>
    public LmReranker(WordNGramLm lm, double lambda)
        : this([new Component(lm ?? throw new ArgumentNullException(nameof(lm)), lambda)])
    {
    }

    /// <summary>複数の重み付き LM を線形補間して加算リスコアする（表層採点・後方互換）。</summary>
    public LmReranker(IReadOnlyList<(WordNGramLm Lm, double Lambda)> components)
        : this(MapTuples(components))
    {
    }

    /// <summary>成分（表層/読み採点を個別指定）を線形補間して加算リスコアする。</summary>
    public LmReranker(IReadOnlyList<Component> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        if (components.Count == 0)
        {
            throw new ArgumentException("少なくとも1つの LM が必要。", nameof(components));
        }

        _components = new Component[components.Count];
        for (var i = 0; i < components.Count; i++)
        {
            var c = components[i];
            _components[i] = c.Lm is null ? throw new ArgumentNullException(nameof(components)) : c;
        }
    }

    private static IReadOnlyList<Component> MapTuples(IReadOnlyList<(WordNGramLm Lm, double Lambda)> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        var mapped = new Component[components.Count];
        for (var i = 0; i < components.Count; i++)
        {
            mapped[i] = new Component(components[i].Lm, components[i].Lambda);
        }

        return mapped;
    }

    /// <inheritdoc />
    public IReadOnlyList<Hypothesis> Rerank(string input, string leftContext, IReadOnlyList<Hypothesis> hypotheses)
    {
        ArgumentNullException.ThrowIfNull(hypotheses);
        _ = input;
        return hypotheses
            .OrderBy(hypothesis => hypothesis.Cost + Score(hypothesis, leftContext))
            .ToList();
    }

    private double Score(Hypothesis hypothesis, string leftContext)
    {
        var sum = 0.0;
        foreach (var c in _components)
        {
            sum += c.Lambda * (c.OverReading
                ? c.Lm.NegLogProbReading(hypothesis.Segments, leftContext)
                : c.Lm.NegLogProb(hypothesis.Segments, leftContext));
        }

        return sum;
    }
}
