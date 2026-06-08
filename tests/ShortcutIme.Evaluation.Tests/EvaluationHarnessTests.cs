using ShortcutIme.Core;
using ShortcutIme.Evaluation;

namespace ShortcutIme.Evaluation.Tests;

public class EvaluationHarnessTests
{
    [Theory]
    [InlineData("きょう", RomajiScheme.Kunrei, EvalInputMode.Consonant, "ky")]
    [InlineData("きょう", RomajiScheme.Kunrei, EvalInputMode.Full, "kyou")]
    [InlineData("きょういく", RomajiScheme.Kunrei, EvalInputMode.Consonant, "kyik")]
    [InlineData("あい", RomajiScheme.Kunrei, EvalInputMode.Consonant, "ai")]
    [InlineData("しごと", RomajiScheme.Hepburn, EvalInputMode.Consonant, "shgt")]
    [InlineData("しごと", RomajiScheme.Hepburn, EvalInputMode.Full, "shigoto")]
    public void EncodeInput_UsesEncoderForSchemeAndMode(string reading, RomajiScheme scheme, EvalInputMode mode, string expected)
    {
        var harness = new EvaluationHarness();

        Assert.Equal(expected, harness.EncodeInput(reading, scheme, mode));
    }

    [Fact]
    public void Run_AggregatesTop1_Mrr_TopK_AndConvertedCounts()
    {
        var harness = new EvaluationHarness();
        EvalCase[] cases =
        [
            new("今日", "きょう"),      // 子音キー "ky"
            new("教育", "きょういく"),  // 子音キー "kyik"
            new("愛", "あい"),          // 子音キー "ai"
        ];

        // 入力（子音キー）ごとに順位付き仮説を返すスタブ変換器。
        IReadOnlyList<string> Convert(string input) => input switch
        {
            "ky" => ["今日", "京"],        // 正解が 1 位
            "kyik" => ["教育う", "教育"],  // 正解が 2 位（top-1 は誤り）
            "ai" => [],                     // 変換失敗（圏外）
            _ => [],
        };

        var report = harness.Run(cases, RomajiScheme.Kunrei, EvalInputMode.Consonant, Convert);

        Assert.Equal(3, report.Total);
        Assert.Equal(1, report.Top1Correct);                  // 今日 のみ
        Assert.Equal(2, report.ConvertedCount);               // ky, kyk は変換成功、a は失敗
        Assert.Equal(1.0 / 3, report.Top1Accuracy, 5);
        Assert.Equal((1.0 + 0.5 + 0.0) / 3, report.Mrr, 5);   // 1/1 + 1/2 + 0
        Assert.Equal(1.0 / 3, report.TopKAccuracy(1), 5);     // 1 位以内＝今日 のみ
        Assert.Equal(2.0 / 3, report.TopKAccuracy(2), 5);     // 2 位以内＝今日・教育
    }

    [Fact]
    public void Run_RecordsInputCharacterAccuracyAndRankPerCase()
    {
        var harness = new EvaluationHarness();
        EvalCase[] cases = [new("今日", "きょう")];

        var report = harness.Run(cases, RomajiScheme.Kunrei, EvalInputMode.Consonant, _ => ["今日"]);
        var only = Assert.Single(report.Cases);

        Assert.Equal("ky", only.Input);
        Assert.Equal("今日", only.TopHypothesis);
        Assert.Equal(1, only.Rank);
        Assert.True(only.IsTop1);
        Assert.True(only.Converted);
        Assert.Equal(1.0, only.CharacterAccuracy);
    }
}
