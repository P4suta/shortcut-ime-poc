namespace ShortcutIme.Evaluation;

/// <summary>
/// 変換精度の指標（純粋関数）。Stage 0 のベースラインから Stage 1.5（LightGBM）・
/// Stage 2（ニューラル）の比較まで、全段で同じ尺度を使い回すための共通モジュール。
/// </summary>
public static class ConversionMetrics
{
    /// <summary>文の完全一致。</summary>
    public static bool SentenceMatch(string expected, string actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        return expected == actual;
    }

    /// <summary>
    /// 文字精度＝ 1 − 正規化レーベンシュタイン距離（0..1、1 が完全一致）。
    /// 完全一致でなくても「どれだけ近いか」を見るための部分点。
    /// </summary>
    public static double CharacterAccuracy(string expected, string actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        if (expected.Length == 0 && actual.Length == 0)
        {
            return 1.0;
        }

        var distance = Levenshtein(expected, actual);
        var max = Math.Max(expected.Length, actual.Length);
        return 1.0 - ((double)distance / max);
    }

    /// <summary>順位付き仮説列のうち、正解が最初に現れる順位（1 始まり）。無ければ 0。</summary>
    public static int RankOf(IReadOnlyList<string> ranked, string expected)
    {
        ArgumentNullException.ThrowIfNull(ranked);
        ArgumentNullException.ThrowIfNull(expected);
        for (var i = 0; i < ranked.Count; i++)
        {
            if (ranked[i] == expected)
            {
                return i + 1;
            }
        }

        return 0;
    }

    /// <summary>逆順位（Reciprocal Rank）。正解が順位 r なら 1/r、圏外なら 0。MRR の素。</summary>
    public static double ReciprocalRank(IReadOnlyList<string> ranked, string expected)
    {
        var rank = RankOf(ranked, expected);
        return rank == 0 ? 0.0 : 1.0 / rank;
    }

    /// <summary>レーベンシュタイン編集距離（2行 DP、O(n) メモリ）。</summary>
    public static int Levenshtein(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + substitution);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }
}
