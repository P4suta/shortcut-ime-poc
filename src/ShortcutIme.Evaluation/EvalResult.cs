using ShortcutIme.Core;

namespace ShortcutIme.Evaluation;

/// <summary>1事例の評価結果。</summary>
/// <param name="Case">元の事例。</param>
/// <param name="Input">実際に変換器へ与えた合成入力。</param>
/// <param name="TopHypothesis">変換器が返した最上位仮説（n-best の先頭、ユーザが最初に見る文）。空なら変換失敗。</param>
/// <param name="Rank">順位付き仮説列のうち正解が現れた順位（1 始まり、圏外は 0）。MRR・top-k の素。</param>
/// <param name="CharacterAccuracy">最上位仮説と正解の文字精度。</param>
public sealed record CaseResult(EvalCase Case, string Input, string TopHypothesis, int Rank, double CharacterAccuracy)
{
    /// <summary>最上位が完全一致したか（top-1 正解）。</summary>
    public bool IsTop1 => Rank == 1;

    /// <summary>変換器が何らかの仮説を返したか。</summary>
    public bool Converted => TopHypothesis.Length > 0;
}

/// <summary>1入力プロファイル（方式×母音保持率）ぶんの集計レポート。</summary>
/// <param name="Scheme">ローマ字方式。</param>
/// <param name="InputLabel">母音保持率の表示ラベル（Consonant／Full／p=0.50 等）。</param>
/// <param name="Cases">事例ごとの結果。</param>
public sealed record EvalReport(RomajiScheme Scheme, string InputLabel, IReadOnlyList<CaseResult> Cases)
{
    /// <summary>事例数。</summary>
    public int Total => Cases.Count;

    /// <summary>top-1 正解数。</summary>
    public int Top1Correct => Cases.Count(c => c.IsTop1);

    /// <summary>top-1 正解率。</summary>
    public double Top1Accuracy => Total == 0 ? 0.0 : (double)Top1Correct / Total;

    /// <summary>平均文字精度。</summary>
    public double MeanCharacterAccuracy => Total == 0 ? 0.0 : Cases.Average(c => c.CharacterAccuracy);

    /// <summary>平均逆順位（MRR）。1-best 変換器では top-1 正解率と一致する。</summary>
    public double Mrr => Total == 0 ? 0.0 : Cases.Average(c => c.Rank == 0 ? 0.0 : 1.0 / c.Rank);

    /// <summary>変換に成功（非空の仮説を返した）した事例数。</summary>
    public int ConvertedCount => Cases.Count(c => c.Converted);

    /// <summary>正解が上位 <paramref name="k"/> 位以内に入った事例数。</summary>
    public int TopKCorrect(int k) => Cases.Count(c => c.Rank >= 1 && c.Rank <= k);

    /// <summary>top-k 正解率。</summary>
    public double TopKAccuracy(int k) => Total == 0 ? 0.0 : (double)TopKCorrect(k) / Total;
}
