namespace ShortcutIme.Core.Tests;

public class RomajiTrieTests
{
    private static RomajiTrie BuildSample() => RomajiTrie.Build(
    [
        new("きょうゆう", "共有", 3411),
        new("きょよう", "許容", 5000),
        new("きょう", "今日", 4000),
        new("あい", "愛", 6000),
    ], new RomajiEncoder());

    [Fact]
    public void Search_ConsonantOnly_MatchesAllVowelVariants()
    {
        var surfaces = BuildSample().Search("ky").Select(c => c.Surface).ToHashSet();

        Assert.Contains("今日", surfaces);   // kyou
        Assert.Contains("共有", surfaces);   // kyouyuu
        Assert.Contains("許容", surfaces);   // kyoyou
    }

    [Fact]
    public void Search_WithVowels_NarrowsCandidates()
    {
        var surfaces = BuildSample().Search("kyou").Select(c => c.Surface).ToHashSet();

        Assert.Contains("今日", surfaces);        // kyou
        Assert.Contains("共有", surfaces);        // kyouyuu（kyou で始まる）
        Assert.DoesNotContain("許容", surfaces);  // kyoyou は除外
    }

    [Fact]
    public void Search_VowelInitial_WorksWithOrWithoutVowel()
    {
        var trie = BuildSample();

        Assert.Contains("愛", trie.Search("a").Select(c => c.Surface));
        Assert.Contains("愛", trie.Search("ai").Select(c => c.Surface));
    }

    [Fact]
    public void Search_SortsByCostAscending()
    {
        // kyy（子音のみ）→ 共有(3411) と 許容(5000)
        var result = BuildSample().Search("kyy");

        Assert.Equal("共有", result[0].Surface);
    }

    [Fact]
    public void Build_WithVariantExpander_MatchesMixedSchemeInput()
    {
        // 質問=しつもん。し={si,shi}, つ={tu,tsu} の直積を索引。
        var trie = RomajiTrie.Build(
            [new("しつもん", "質問", 4000)],
            reading => RomajiVariants.ExpandReading(reading));

        Assert.Contains("質問", trie.Search("situmon").Select(c => c.Surface));    // 全訓令式
        Assert.Contains("質問", trie.Search("shitsumon").Select(c => c.Surface));  // 全ヘボン式
        Assert.Contains("質問", trie.Search("shitumon").Select(c => c.Surface));   // 混在
        Assert.Contains("質問", trie.Search("shtmn").Select(c => c.Surface));      // 子音のみ（混在 shi→sh, tu→t）
    }

    [Fact]
    public void SaveLoad_RoundTripsSearchResults()
    {
        var trie = BuildSample();
        using var stream = new MemoryStream();
        trie.Save(stream);
        stream.Position = 0;
        var loaded = RomajiTrie.Load(stream);

        foreach (var key in new[] { "ky", "kyou", "kyy", "a", "ai" })
        {
            var expected = trie.Search(key).Select(c => (c.Surface, c.Cost)).OrderBy(x => x.Surface).ToList();
            var actual = loaded.Search(key).Select(c => (c.Surface, c.Cost)).OrderBy(x => x.Surface).ToList();
            Assert.Equal(expected, actual);
        }
    }
}
