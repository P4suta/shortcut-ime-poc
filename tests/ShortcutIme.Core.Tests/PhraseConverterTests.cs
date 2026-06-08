namespace ShortcutIme.Core.Tests;

public class PhraseConverterTests
{
    private static PhraseConverter BuildConverter()
    {
        var trie = RomajiTrie.Build(
        [
            new("きょう", "今日", 4000),
            new("は", "は", 2000),
            new("ありがとう", "ありがとう", 4000),
            new("ございました", "ございました", 5000),
        ], new RomajiEncoder());
        return new PhraseConverter(trie, segmentPenalty: 1000);
    }

    [Fact]
    public void Convert_FullRomaji_SplitsIntoExpectedPhrases()
    {
        var input = new RomajiEncoder().Encode("きょうはありがとうございました");

        var surfaces = string.Concat(BuildConverter().Convert(input).Select(c => c.Surface));

        Assert.Equal("今日はありがとうございました", surfaces);
    }

    [Fact]
    public void Convert_ConsonantOnly_StillCoversTheSentence()
    {
        var input = new ConsonantEncoder().Encode("きょうはありがとうございました");

        // 子音のみでも、この小辞書では全体を覆う分割が見つかる（中身は曖昧でも非空）
        Assert.NotEmpty(BuildConverter().Convert(input));
    }

    [Fact]
    public void Convert_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(BuildConverter().Convert(""));
    }

    [Fact]
    public void ConvertNBest_TopOne_MatchesConvert()
    {
        var converter = BuildConverter();
        var input = new RomajiEncoder().Encode("きょうはありがとうございました");

        var oneBest = string.Concat(converter.Convert(input).Select(c => c.Surface));
        var nbest = converter.ConvertNBest(input, 5);

        Assert.NotEmpty(nbest);
        Assert.Equal(oneBest, nbest[0].Surface);
    }

    [Fact]
    public void ConvertNBest_ReturnsCostAscending()
    {
        var converter = BuildConverter();
        var input = new ConsonantEncoder().Encode("きょうはありがとうございました");

        var nbest = converter.ConvertNBest(input, 10);

        for (var i = 1; i < nbest.Count; i++)
        {
            Assert.True(nbest[i - 1].Cost <= nbest[i].Cost);
        }
    }

    [Fact]
    public void ConvertNBest_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(BuildConverter().ConvertNBest("", 5));
    }
}
