using ShortcutIme.Core;

namespace ShortcutIme.Evaluation;

/// <summary>
/// 逐次文節確定レジームをシミュレートする（オフライン評価）。確定左文脈を前送りしながら左から文節を確定する。
/// 2モード：<see cref="RunGreedy"/>＝無人 top-1 commit（誤れば cascade。一発 cw top-1 と apples-to-apples）、
/// <see cref="RunOracleTopK"/>＝候補UI（各 step で gold が top-k に居るか＝ユーザーが選べる。構造的に逐次有利なので bias 注記つきで解釈）。
/// </summary>
public sealed class IncrementalSimulator
{
    private const int FullList = 4096; // oracle で gold の順位を得るための実質「全候補」。
    private readonly INextSegmentSource _inc;

    public IncrementalSimulator(INextSegmentSource inc)
        => _inc = inc ?? throw new ArgumentNullException(nameof(inc));

    /// <param name="ExactMatch">確定列の表層が gold 文に一致したか。</param>
    /// <param name="DeadEnd">途中で次候補が尽き全被覆できなかったか（greedy の失敗）。</param>
    public readonly record struct GreedyResult(bool ExactMatch, bool DeadEnd);

    /// <param name="Steps">gold 文節数。</param>
    /// <param name="Hits">各 step で gold が top-k に居た回数。</param>
    /// <param name="AllHit">全 step で gold∈top-k かつ入力を丁度被覆（＝候補UIで全文を組める）。</param>
    /// <param name="Aligned">gold 文節が各位置で整合し入力を丁度被覆したか。</param>
    public readonly record struct OracleResult(int Steps, int Hits, bool AllHit, bool Aligned);

    /// <summary>無人 greedy：各 step top-1 を commit（誤確定でも commit＝cascade）。全被覆できねば dead-end。</summary>
    public GreedyResult RunGreedy(string input, string goldSurface)
    {
        var committed = new List<Candidate>();
        var start = 0;
        while (start < input.Length)
        {
            var cands = _inc.NextCandidates(input, start, committed, 1);
            if (cands.Count == 0 || cands[0].Length <= 0)
            {
                return new GreedyResult(false, DeadEnd: true);
            }

            committed.Add(cands[0].Candidate);
            start += cands[0].Length;
        }

        var surface = string.Concat(committed.Select(c => c.Surface));
        return new GreedyResult(surface == goldSurface, DeadEnd: false);
    }

    /// <summary>
    /// 候補UI：各 step で gold 文節が top-k に居るか。gold を commit し<b>gold の正確な入力長</b>で前進（cascade 無し・desync 無し）。
    /// gold が候補に無い step はミスとして数え継続（早期 return しない＝各 step を独立評価）。
    /// </summary>
    public OracleResult RunOracleTopK(string input, IReadOnlyList<Candidate> goldSegs, IReadOnlyList<int> goldLengths, int k)
    {
        var committed = new List<Candidate>();
        var start = 0;
        var hits = 0;
        for (var t = 0; t < goldSegs.Count; t++)
        {
            var gold = goldSegs[t];
            if (start >= input.Length)
            {
                return new OracleResult(goldSegs.Count, hits, AllHit: false, Aligned: false);
            }

            var cands = _inc.NextCandidates(input, start, committed, FullList);
            var rank = -1;
            for (var i = 0; i < cands.Count; i++)
            {
                if (cands[i].Candidate.Surface == gold.Surface && cands[i].Candidate.Reading == gold.Reading)
                {
                    rank = i;
                    break;
                }
            }

            if (rank >= 0 && rank < k)
            {
                hits++;
            }

            committed.Add(gold);
            start += goldLengths[t]; // gold パスの正確な入力長で前進（母音スキップ変異の長さ曖昧を排除）。
        }

        var aligned = start == input.Length;
        return new OracleResult(goldSegs.Count, hits, AllHit: hits == goldSegs.Count && aligned, aligned);
    }
}
