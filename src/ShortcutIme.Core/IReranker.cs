namespace ShortcutIme.Core;

/// <summary>
/// n-best の文候補を「入力キー＋左文脈」を見て並べ替えるリランカーの seam。
/// Stage 1.5（LightGBM）・Stage 2（ニューラル cross-encoder）がこれを実装する。
/// 既定は <see cref="IdentityReranker"/>（並べ替えなし）。
/// </summary>
public interface IReranker
{
    /// <param name="input">変換に使った打鍵列（子音/混在）。</param>
    /// <param name="leftContext">確定済みの左文脈（無ければ空）。</param>
    /// <param name="hypotheses">コスト昇順の n-best。</param>
    /// <returns>並べ替え後の候補列（最尤が先頭）。</returns>
    IReadOnlyList<Hypothesis> Rerank(string input, string leftContext, IReadOnlyList<Hypothesis> hypotheses);
}

/// <summary>並べ替えをしない既定リランカー（WFST のコスト順をそのまま返す）。</summary>
public sealed class IdentityReranker : IReranker
{
    /// <summary>共有インスタンス。</summary>
    public static readonly IdentityReranker Instance = new();

    /// <inheritdoc />
    public IReadOnlyList<Hypothesis> Rerank(string input, string leftContext, IReadOnlyList<Hypothesis> hypotheses)
    {
        ArgumentNullException.ThrowIfNull(hypotheses);
        return hypotheses;
    }
}
