using ShortcutIme.Core;

namespace ShortcutIme.Evaluation;

/// <summary>
/// 実打鍵を模す合成入力生成器。各 OPTIONAL 母音（子音モーラの末尾母音・確信長音）を保持率
/// <see cref="_keepRate"/> で残すか落とすか<b>モーラ単位で混在</b>させる。MANDATORY 母音は常に残す
/// （<see cref="MoraKeystrokeWalker"/> の規約）。
/// <para>
/// 決定論：共有乱数を反復で進めると順序依存になり再現できないため、各 OPTIONAL 母音の判定は
/// <c>hash(seed, reading, optionalVowelIndex)</c> を [0,1) に写した安定値 &lt; keepRate で行う。
/// 同じ (seed, 読み, 連番) は常に同じ判定＝反復順非依存・完全再現。
/// </para>
/// <para>
/// 両端の構成的一致：<paramref name="keepRate"/> ≤ 0 はハッシュを消費せず常に false＝
/// <see cref="ConsonantEncoder"/>、≥ 1 は常に true＝<see cref="RomajiEncoder"/> に厳密一致する。
/// よって混在は第三の挙動でなく両端の真の補間。なお一様 Bernoulli は<b>合成的仮定</b>であり実打鍵分布そのものではない。
/// </para>
/// </summary>
public sealed class MixedVowelEncoder : IReadingEncoder
{
    private readonly RomajiScheme _scheme;
    private readonly double _keepRate;
    private readonly int _seed;

    /// <param name="scheme">ローマ字方式。</param>
    /// <param name="keepRate">OPTIONAL 母音を残す確率 [0,1]。0=子音のみ、1=フル。</param>
    /// <param name="seed">決定的判定の種。同 seed なら完全再現。</param>
    public MixedVowelEncoder(RomajiScheme scheme, double keepRate, int seed)
    {
        _scheme = scheme;
        _keepRate = keepRate;
        _seed = seed;
    }

    /// <inheritdoc />
    public string Encode(string reading) => MoraKeystrokeWalker.Encode(reading, _scheme, KeepVowel);

    private bool KeepVowel(MoraKeystrokeWalker.VowelDecision decision)
    {
        if (_keepRate <= 0.0)
        {
            return false; // 短絡＝ConsonantEncoder と厳密一致。
        }

        if (_keepRate >= 1.0)
        {
            return true; // 短絡＝RomajiEncoder と厳密一致。
        }

        return Uniform(_seed, decision.Reading, decision.OptionalVowelIndex) < _keepRate;
    }

    // FNV-1a で (seed, index, 読み) を uint へ畳み込み [0,1) に写す。string.GetHashCode はプロセス毎に
    // ランダム化され再現性が無いため使わない。
    private static double Uniform(int seed, string reading, int index)
    {
        unchecked
        {
            var h = 2166136261u;
            h = (h ^ (uint)seed) * 16777619u;
            h = (h ^ (uint)index) * 16777619u;
            foreach (var c in reading)
            {
                h = (h ^ c) * 16777619u;
            }

            return h / 4294967296.0; // 2^32
        }
    }
}
