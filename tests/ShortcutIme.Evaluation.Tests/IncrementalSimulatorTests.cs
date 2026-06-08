using ShortcutIme.Core;

namespace ShortcutIme.Evaluation.Tests;

public class IncrementalSimulatorTests
{
    private static readonly DictionaryEntry[] Entries =
    [
        new("きょう", "今日", 1000), new("は", "は", 500), new("はれ", "晴れ", 1000),
        new("はな", "花", 1200), new("れ", "れ", 5000),
    ];

    [Fact]
    public void Oracle_FindsGoldBunsetsuPerStep_AndAligns()
    {
        var trie = RomajiTrie.Build(Entries, r => RomajiVariants.ExpandReading(r));
        var conv = new PhraseConverter(trie, connection: null, segmentPenalty: 3000, vowelSkipPenalty: 500);
        var lm = WordNGramLm.Build(
            new StringReader("今日 は 晴れ\n今日 は 晴れ\n"), TokenMode.Word, lambdaBi: 0.7, floorNegLogProb: 10.0);
        var look = new LookaheadConverter(conv, [new LmReranker.Component(lm, 100.0)], nbest: 50);
        var sim = new IncrementalSimulator(look);

        var input = new RomajiEncoder().Encode("きょうははれ"); // 今日は晴れ
        var goldHyp = conv.ConvertNBest(input, 50).First(h => h.Surface == "今日は晴れ");

        var oracle = sim.RunOracleTopK(input, goldHyp.Segments, goldHyp.SegmentLengths!, k: 5);

        Assert.True(oracle.Aligned, "gold 長で前進して入力を丁度被覆するはず");
        Assert.Equal(oracle.Steps, oracle.Hits); // 全 step で gold∈top-5
        Assert.True(oracle.AllHit);
    }

    [Fact]
    public void Greedy_ReachesEnd_NoDeadEnd()
    {
        var trie = RomajiTrie.Build(Entries, r => RomajiVariants.ExpandReading(r));
        var conv = new PhraseConverter(trie, null, 3000, 500);
        var lm = WordNGramLm.Build(
            new StringReader("今日 は 晴れ\n今日 は 晴れ\n"), TokenMode.Word, 0.7, 10.0);
        var sim = new IncrementalSimulator(new LookaheadConverter(conv, [new LmReranker.Component(lm, 100.0)], 50));

        var input = new RomajiEncoder().Encode("きょうははれ");
        var greedy = sim.RunGreedy(input, "今日は晴れ");

        Assert.False(greedy.DeadEnd); // 全被覆できる
    }
}
