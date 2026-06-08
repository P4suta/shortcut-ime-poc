using ShortcutIme.Evaluation;

namespace ShortcutIme.Evaluation.Tests;

public class ConversionMetricsTests
{
    [Theory]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "", 3)]
    [InlineData("abc", "abc", 0)]
    [InlineData("今日", "教育", 2)]
    public void Levenshtein_ReturnsEditDistance(string a, string b, int expected)
    {
        Assert.Equal(expected, ConversionMetrics.Levenshtein(a, b));
    }

    [Fact]
    public void CharacterAccuracy_ExactMatch_IsOne()
    {
        Assert.Equal(1.0, ConversionMetrics.CharacterAccuracy("今日は晴れ", "今日は晴れ"));
    }

    [Fact]
    public void CharacterAccuracy_BothEmpty_IsOne()
    {
        Assert.Equal(1.0, ConversionMetrics.CharacterAccuracy("", ""));
    }

    [Fact]
    public void CharacterAccuracy_OneSubstitutionOfFour_IsThreeQuarters()
    {
        // 編集距離 1 / 最大長 4 = 0.25 → 精度 0.75
        Assert.Equal(0.75, ConversionMetrics.CharacterAccuracy("abcd", "abxd"), 5);
    }

    [Fact]
    public void RankOf_ReturnsOneBasedPositionOfFirstMatch()
    {
        Assert.Equal(2, ConversionMetrics.RankOf(["今日", "教育", "今日"], "教育"));
    }

    [Fact]
    public void RankOf_NotPresent_IsZero()
    {
        Assert.Equal(0, ConversionMetrics.RankOf(["今日", "教育"], "愛"));
    }

    [Theory]
    [InlineData(0.5, "教育")] // 2 位 → 1/2
    [InlineData(1.0, "今日")] // 1 位 → 1/1
    [InlineData(0.0, "愛")]   // 圏外 → 0
    public void ReciprocalRank_IsInverseOfRank(double expected, string target)
    {
        Assert.Equal(expected, ConversionMetrics.ReciprocalRank(["今日", "教育"], target), 5);
    }

    [Fact]
    public void SentenceMatch_ComparesExactly()
    {
        Assert.True(ConversionMetrics.SentenceMatch("今日は", "今日は"));
        Assert.False(ConversionMetrics.SentenceMatch("今日は", "今日わ"));
    }
}
