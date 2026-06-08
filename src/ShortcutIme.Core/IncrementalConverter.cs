namespace ShortcutIme.Core;

/// <summary>逐次デコードの「次文節候補」。<see cref="Total"/>（小さいほど上位）で並べる。</summary>
/// <param name="Candidate">次文節の候補語。</param>
/// <param name="Length">この候補が消費する入力長（次の開始位置 = start + Length）。</param>
/// <param name="VowelSkips">この候補で読み飛ばした母音数。</param>
/// <param name="BaseCost">生起＋連接＋文節/母音スキップペナルティ（WFST 部分コスト）。</param>
/// <param name="LmScore">確定左文脈の下での LM 継続スコア（<see cref="IStepScorer"/>）。</param>
public readonly record struct NextSegment(Candidate Candidate, int Length, int VowelSkips, long BaseCost, double LmScore)
{
    /// <summary>総合スコア（小さいほど上位）。</summary>
    public double Total => BaseCost + LmScore;
}

/// <summary>確定左文脈＋残り入力から「次文節候補」を返す seam。単一セグメント版／look-ahead 版が実装する。</summary>
public interface INextSegmentSource
{
    /// <summary>位置 <paramref name="start"/> 以降の残り入力に対する次文節候補を上位 <paramref name="topK"/> で返す。</summary>
    IReadOnlyList<NextSegment> NextCandidates(string input, int start, IReadOnlyList<Candidate> committed, int topK);
}

/// <summary>
/// 逐次文節確定の素朴版。残り入力（位置 start 以降）の「次の1セグメント候補」を <see cref="NextSegment.Total"/> 昇順で返す。
/// <b>注意</b>：単一セグメントのみ見るため長い文節が短断片に負ける myopia がある（look-ahead は <see cref="LookaheadConverter"/>）。
/// </summary>
public sealed class IncrementalConverter : INextSegmentSource
{
    private readonly RomajiTrie _trie;
    private readonly ConnectionMatrix? _connection;
    private readonly IStepScorer _scorer;
    private readonly int _segmentPenalty;
    private readonly int _vowelSkipPenalty;

    /// <param name="trie">フルローマ字トライ。</param>
    /// <param name="connection">連接コスト行列（null なら 0）。</param>
    /// <param name="scorer">確定左文脈の下での次文節 LM スコアラ。</param>
    /// <param name="segmentPenalty">1文節ごとの加算（一発レジームと揃える）。</param>
    /// <param name="vowelSkipPenalty">母音スキップ1つごとの加算（一発レジームと揃える）。</param>
    public IncrementalConverter(RomajiTrie trie, ConnectionMatrix? connection, IStepScorer scorer,
        int segmentPenalty = 0, int vowelSkipPenalty = 500)
    {
        ArgumentNullException.ThrowIfNull(trie);
        ArgumentNullException.ThrowIfNull(scorer);
        _trie = trie;
        _connection = connection;
        _scorer = scorer;
        _segmentPenalty = segmentPenalty;
        _vowelSkipPenalty = vowelSkipPenalty;
    }

    /// <summary>位置 <paramref name="start"/> 以降の残り入力に対する次文節候補を Total 昇順 top-K で返す。</summary>
    public IReadOnlyList<NextSegment> NextCandidates(string input, int start, IReadOnlyList<Candidate> committed, int topK)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(committed);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);
        if (start < 0 || start >= input.Length)
        {
            return [];
        }

        var prevRightId = committed.Count == 0 ? 0 : committed[^1].RightId;
        var results = new List<NextSegment>();
        foreach (var seg in _trie.SegmentsFrom(input, start))
        {
            var word = seg.Candidate;
            var connectionCost = _connection?.Cost(prevRightId, word.LeftId) ?? 0;
            var baseCost = word.Cost + connectionCost + _segmentPenalty + ((long)_vowelSkipPenalty * seg.VowelSkips);
            var lmScore = _scorer.Score(committed, word);
            results.Add(new NextSegment(word, seg.Length, seg.VowelSkips, baseCost, lmScore));
        }

        // Total 昇順、同点は BaseCost 昇順（既存 n-best と整合する安定タイブレーク）。
        results.Sort(static (a, b) =>
        {
            var c = a.Total.CompareTo(b.Total);
            return c != 0 ? c : a.BaseCost.CompareTo(b.BaseCost);
        });

        return results.Count > topK ? results.GetRange(0, topK) : results;
    }
}
