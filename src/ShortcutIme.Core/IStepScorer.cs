namespace ShortcutIme.Core;

/// <summary>
/// 逐次文節確定で「確定済み左文脈の下での次文節候補」を採点する seam。スコアは小さいほど上位。
/// <see cref="IncrementalConverter"/> が各候補に加算する LM 項などを供給する。既定は <see cref="ZeroStepScorer"/>。
/// </summary>
public interface IStepScorer
{
    /// <param name="committed">確定済みの左文脈（文節列）。空なら文頭。</param>
    /// <param name="candidate">採点する次文節候補。</param>
    /// <returns>加算スコア（小さいほど上位）。</returns>
    double Score(IReadOnlyList<Candidate> committed, Candidate candidate);
}

/// <summary>LM を使わない既定スコアラ（連接＋生起コストのみで並べる）。</summary>
public sealed class ZeroStepScorer : IStepScorer
{
    /// <summary>共有インスタンス。</summary>
    public static readonly ZeroStepScorer Instance = new();

    /// <inheritdoc />
    public double Score(IReadOnlyList<Candidate> committed, Candidate candidate) => 0.0;
}
