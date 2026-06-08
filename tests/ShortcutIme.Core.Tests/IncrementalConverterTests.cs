namespace ShortcutIme.Core.Tests;

public class IncrementalConverterTests
{
    private static RomajiTrie HomonymTrie() => RomajiTrie.Build(
        [new("きょう", "今日", 1000), new("きょう", "京", 1000)],
        r => RomajiVariants.ExpandReading(r));

    [Fact]
    public void NextCandidates_LeftContextChangesRanking()
    {
        // え→今日 / に→京 を観測させ、確定左文脈で同音(きょう=今日/京)の順位が入れ替わることを確認＝leftContext 実信号。
        var trie = HomonymTrie();
        var lm = WordNGramLm.Build(
            new StringReader("え 今日\nえ 今日\nに 京\nに 京\n"), TokenMode.Word, lambdaBi: 0.7, floorNegLogProb: 10.0);
        var scorer = new LmStepScorer([new LmReranker.Component(lm, 1000.0)]);
        var inc = new IncrementalConverter(trie, connection: null, scorer, segmentPenalty: 0, vowelSkipPenalty: 500);

        var afterE = inc.NextCandidates("kyou", 0, [new Candidate("え", "え", 0)], 5);
        var afterNi = inc.NextCandidates("kyou", 0, [new Candidate("に", "に", 0)], 5);

        Assert.Equal("今日", afterE[0].Candidate.Surface);
        Assert.Equal("京", afterNi[0].Candidate.Surface);
    }

    [Fact]
    public void NextCandidates_ConsonantInput_FindsBothHomonyms()
    {
        var trie = HomonymTrie();
        var inc = new IncrementalConverter(trie, null, ZeroStepScorer.Instance, 0, 500);

        var surfaces = inc.NextCandidates("ky", 0, [], 10).Select(c => c.Candidate.Surface).ToList();

        Assert.Contains("今日", surfaces);
        Assert.Contains("京", surfaces);
    }

    [Fact]
    public void NextCandidates_EmptyAtEnd()
    {
        var trie = HomonymTrie();
        var inc = new IncrementalConverter(trie, null, ZeroStepScorer.Instance, 0, 500);

        Assert.Empty(inc.NextCandidates("kyou", 4, [], 5)); // start == length
    }
}
