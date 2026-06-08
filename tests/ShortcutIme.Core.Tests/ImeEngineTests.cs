namespace ShortcutIme.Core.Tests;

public class ImeEngineTests
{
    private static ImeEngine BuildEngine() => new(
        RomajiTrie.Build(
        [
            new("きょうゆう", "共有", 3411),
            new("きょよう", "許容", 5000),
        ], new RomajiEncoder()),
        new LearningStore());

    [Fact]
    public void Convert_WithoutLearning_OrdersByCost()
    {
        var engine = BuildEngine();

        Assert.Equal("共有", engine.Convert("kyy")[0].Surface);
    }

    [Fact]
    public void Convert_AfterCommit_PromotesLearnedCandidate()
    {
        var engine = BuildEngine();
        var kyoyou = engine.Convert("kyoyou").Single(c => c.Surface == "許容");

        engine.Commit(kyoyou);

        // 学習後は kyy でも 許容 が先頭（コストは高いが recency で浮上）
        Assert.Equal("許容", engine.Convert("kyy")[0].Surface);
    }
}
