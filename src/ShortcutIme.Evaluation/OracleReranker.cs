using ShortcutIme.Core;

namespace ShortcutIme.Evaluation;

/// <summary>
/// 評価専用リランカー。合成入力（打鍵列）→ gold の対応表を与え、gold が n-best に含まれていれば先頭へ繰り上げる。
/// リランカーが到達しうる上限（gold∈n-best がそのまま top-1 になる）を実測し、seam の配線が正しいかを検証する土台。
/// 本番 <see cref="IReranker"/> 契約に gold を持ち込まないよう、Core ではなく Evaluation に置く。
/// </summary>
/// <param name="goldByInput">合成入力（打鍵列）から gold 表層文への対応表。</param>
public sealed class OracleReranker(IReadOnlyDictionary<string, string> goldByInput) : IReranker
{
    /// <inheritdoc />
    public IReadOnlyList<Hypothesis> Rerank(string input, string leftContext, IReadOnlyList<Hypothesis> hypotheses)
    {
        ArgumentNullException.ThrowIfNull(hypotheses);

        if (!goldByInput.TryGetValue(input, out var gold))
        {
            return hypotheses;
        }

        var index = -1;
        for (var i = 0; i < hypotheses.Count; i++)
        {
            if (hypotheses[i].Surface == gold)
            {
                index = i;
                break;
            }
        }

        if (index <= 0)
        {
            return hypotheses; // 既に先頭、または圏外（>n-best）なら触らない
        }

        var reordered = hypotheses.ToList();
        reordered.RemoveAt(index);
        reordered.Insert(0, hypotheses[index]);
        return reordered;
    }
}
