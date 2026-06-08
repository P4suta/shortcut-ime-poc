namespace ShortcutIme.Core;

/// <summary>
/// 確定済み左文脈の下で次文節候補を LM の<b>継続採点</b>（<see cref="WordNGramLm.NegLogProbContinuation"/>）で評価する
/// <see cref="IStepScorer"/>。<see cref="LmReranker.Component"/>（char/word/reading × λ）を再利用し N 成分線形補間する。
/// 全文 cw リランカーと同じ LM・λ を渡せば、一発レジームと逐次レジームを同一 LM で公平比較できる。
/// </summary>
public sealed class LmStepScorer : IStepScorer
{
    private readonly LmReranker.Component[] _components;

    public LmStepScorer(IReadOnlyList<LmReranker.Component> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        if (components.Count == 0)
        {
            throw new ArgumentException("少なくとも1つの LM が必要。", nameof(components));
        }

        _components = new LmReranker.Component[components.Count];
        for (var i = 0; i < components.Count; i++)
        {
            var c = components[i];
            _components[i] = c.Lm is null ? throw new ArgumentNullException(nameof(components)) : c;
        }
    }

    /// <inheritdoc />
    public double Score(IReadOnlyList<Candidate> committed, Candidate candidate)
    {
        ArgumentNullException.ThrowIfNull(committed);
        ArgumentNullException.ThrowIfNull(candidate);
        Candidate[] next = [candidate];
        var sum = 0.0;
        foreach (var c in _components)
        {
            sum += c.Lambda * c.Lm.NegLogProbContinuation(next, committed, c.OverReading);
        }

        return sum;
    }
}
