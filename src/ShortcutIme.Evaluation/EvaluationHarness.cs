using System.Globalization;
using ShortcutIme.Core;

namespace ShortcutIme.Evaluation;

/// <summary>
/// 読みから合成入力を作り、与えた変換器（順位付き仮説を返す関数）を走らせて指標を集計する。
/// 入力は (ローマ字方式 × 母音保持率 keepRate) で生成する。<see cref="EvalInputMode"/> の2端点
/// （Consonant=keepRate 0／Full=keepRate 1）は連続軸の特殊点として委譲で表す。変換器は
/// <c>Func&lt;string, IReadOnlyList&lt;string&gt;&gt;</c> で受けるため 1-best でも n-best でも同じハーネスで比較できる。
/// </summary>
public sealed class EvaluationHarness
{
    /// <summary>指定方式・母音レベル（2端点）で読みを合成入力へ変換する。</summary>
    public string EncodeInput(string reading, RomajiScheme scheme, EvalInputMode mode)
        => EncodeInput(reading, scheme, KeepRateOf(mode), seed: 0);

    /// <summary>指定方式・母音保持率 keepRate で読みを合成入力（混在打鍵列）へ変換する。</summary>
    public string EncodeInput(string reading, RomajiScheme scheme, double keepRate, int seed)
    {
        ArgumentNullException.ThrowIfNull(reading);
        return new MixedVowelEncoder(scheme, keepRate, seed).Encode(reading);
    }

    /// <summary>事例集合を指定方式・母音レベル（2端点）で評価する。</summary>
    public EvalReport Run(
        IReadOnlyCollection<EvalCase> cases,
        RomajiScheme scheme,
        EvalInputMode mode,
        Func<string, IReadOnlyList<string>> convert)
        => Run(cases, scheme, KeepRateOf(mode), seed: 0, convert);

    /// <summary>
    /// 事例集合を指定方式・母音保持率 keepRate で評価する。<paramref name="convert"/> は合成入力を受け取り、
    /// 順位付き仮説（最尤が先頭）の文字列列を返す。1-best 変換器は要素1の列を返せばよい。
    /// </summary>
    public EvalReport Run(
        IReadOnlyCollection<EvalCase> cases,
        RomajiScheme scheme,
        double keepRate,
        int seed,
        Func<string, IReadOnlyList<string>> convert)
    {
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(convert);

        var results = new List<CaseResult>(cases.Count);
        foreach (var item in cases)
        {
            var input = EncodeInput(item.Reading, scheme, keepRate, seed);
            var hypotheses = convert(input);
            var top = hypotheses.Count > 0 ? hypotheses[0] : string.Empty;
            var rank = ConversionMetrics.RankOf(hypotheses, item.Sentence);
            var charAccuracy = ConversionMetrics.CharacterAccuracy(item.Sentence, top);
            results.Add(new CaseResult(item, input, top, rank, charAccuracy));
        }

        return new EvalReport(scheme, LabelFor(keepRate), results);
    }

    private static double KeepRateOf(EvalInputMode mode) => mode switch
    {
        EvalInputMode.Consonant => 0.0,
        EvalInputMode.Full => 1.0,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>入力プロファイルの表示ラベル。端点は名前、中間は <c>p=0.50</c> 形式。</summary>
    public static string LabelFor(double keepRate) => keepRate switch
    {
        <= 0.0 => "Consonant",
        >= 1.0 => "Full",
        _ => $"p={keepRate.ToString("0.00", CultureInfo.InvariantCulture)}",
    };
}
