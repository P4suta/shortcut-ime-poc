namespace ShortcutIme.Core.Tests;

public class MoraSplitterTests
{
    [Fact]
    public void Split_CombinesYoonIntoSingleMora()
    {
        var moras = MoraSplitter.Split("きょういく");

        Assert.Equal(["きょ", "う", "い", "く"], moras.Select(m => m.Kana));
    }

    [Fact]
    public void Split_TreatsSokuonAndChoonAsOwnMora()
    {
        var moras = MoraSplitter.Split("がっこー");

        Assert.Equal(["が", "っ", "こ", "ー"], moras.Select(m => m.Kana));
    }
}
