namespace ShortcutIme.Core;

/// <summary>
/// look-ahead 版の次文節候補生成。各 step で<b>残り入力を丸ごと再パース</b>（<see cref="PhraseConverter.ConvertNBest"/>）し、
/// 全経路を「経路コスト＋確定左文脈で条件付けた LM」で採点して、その<b>先頭文節</b>を候補として返す。
/// 単一セグメント版（<see cref="IncrementalConverter"/>）の myopia（長い文節が短断片に負ける）を、先読み（残り全体の
/// 最良完成）で解消する。確定左文脈は LM の prev に積む＝<c>leftContext</c> を実信号にする。
/// </summary>
public sealed class LookaheadConverter : INextSegmentSource
{
    private readonly PhraseConverter _converter;
    private readonly LmReranker.Component[] _components;
    private readonly int _nbest;

    /// <param name="converter">残り入力を再パースする連文節変換器（IncrementalConverter と同じ trie/penalty 設定にする）。</param>
    /// <param name="components">確定左文脈で条件付けて経路を採点する LM 成分（cw と同じ char/word を渡す）。</param>
    /// <param name="nbest">各 step で再パースする n-best 幅（先頭文節の多様性を担保）。</param>
    public LookaheadConverter(PhraseConverter converter, IReadOnlyList<LmReranker.Component> components, int nbest = 100)
    {
        ArgumentNullException.ThrowIfNull(converter);
        ArgumentNullException.ThrowIfNull(components);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nbest);
        _converter = converter;
        _components = [.. components];
        _nbest = nbest;
    }

    /// <inheritdoc />
    public IReadOnlyList<NextSegment> NextCandidates(string input, int start, IReadOnlyList<Candidate> committed, int topK)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(committed);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);
        if (start < 0 || start >= input.Length)
        {
            return [];
        }

        var remaining = start == 0 ? input : input[start..];
        var hypotheses = _converter.ConvertNBest(remaining, _nbest);

        // 先頭文節 (Surface,Reading,Length) ごとに、それを先頭に持つ最良経路スコアを保持する。
        var best = new Dictionary<(string Surface, string Reading, int Length), NextSegment>();
        foreach (var h in hypotheses)
        {
            if (h.Segments.Count == 0 || h.SegmentLengths is null || h.SegmentLengths.Count == 0)
            {
                continue;
            }

            var first = h.Segments[0];
            var firstLen = h.SegmentLengths[0];
            var lm = ScoreConditioned(committed, h.Segments);
            var total = h.Cost + lm; // 残り全体の最良完成スコア（先読み）。
            var key = (first.Surface, first.Reading, firstLen);
            if (!best.TryGetValue(key, out var existing) || total < existing.Total)
            {
                best[key] = new NextSegment(first, firstLen, VowelSkips: 0, BaseCost: h.Cost, LmScore: lm);
            }
        }

        var results = new List<NextSegment>(best.Values);
        results.Sort(static (a, b) =>
        {
            var c = a.Total.CompareTo(b.Total);
            return c != 0 ? c : a.BaseCost.CompareTo(b.BaseCost);
        });

        return results.Count > topK ? results.GetRange(0, topK) : results;
    }

    // 確定左文脈の下で残り経路を採点（committed の内部コストは全候補共通＝順位に無影響だが、committed[-1]→次 の継ぎ目が効く）。
    private double ScoreConditioned(IReadOnlyList<Candidate> committed, IReadOnlyList<Candidate> remaining)
    {
        var sum = 0.0;
        foreach (var c in _components)
        {
            sum += c.Lambda * (c.OverReading
                ? c.Lm.NegLogProbReading(Concat(committed, remaining), "")
                : c.Lm.NegLogProb(Concat(committed, remaining), ""));
        }

        return sum;
    }

    private static IReadOnlyList<Candidate> Concat(IReadOnlyList<Candidate> a, IReadOnlyList<Candidate> b)
    {
        if (a.Count == 0)
        {
            return b;
        }

        var list = new List<Candidate>(a.Count + b.Count);
        list.AddRange(a);
        list.AddRange(b);
        return list;
    }
}
