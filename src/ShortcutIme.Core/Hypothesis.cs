namespace ShortcutIme.Core;

/// <summary>連文節変換の1つの文候補（文節列＋経路コスト）。n-best／リランキングの単位。</summary>
/// <param name="Segments">文節（単語）列。</param>
/// <param name="Cost">経路の総コスト（小さいほど上位）。</param>
public sealed record Hypothesis(IReadOnlyList<Candidate> Segments, long Cost)
{
    /// <summary>文節を連結した表層文。</summary>
    public string Surface => string.Concat(Segments.Select(segment => segment.Surface));
}
