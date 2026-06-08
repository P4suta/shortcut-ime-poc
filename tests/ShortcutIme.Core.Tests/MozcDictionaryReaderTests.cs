namespace ShortcutIme.Core.Tests;

public class MozcDictionaryReaderTests
{
    [Fact]
    public void Read_ParsesFiveColumnTsv()
    {
        using var reader = new StringReader("きょうゆう\t1851\t1851\t5000\t共有\n");

        var entry = Assert.Single(MozcDictionaryReader.Read(reader));

        Assert.Equal("きょうゆう", entry.Reading);
        Assert.Equal("共有", entry.Surface);
        Assert.Equal(5000, entry.Cost);
        Assert.Equal(1851, entry.LeftId);
        Assert.Equal(1851, entry.RightId);
    }

    [Fact]
    public void Read_SkipsMalformedLines()
    {
        using var reader = new StringReader(
            "too\tfew\n" +
            "きょうゆう\t1851\t1851\tNaN\t共有\n" +
            "きょうぎ\t1851\t1851\t6000\t協議\n");

        var entry = Assert.Single(MozcDictionaryReader.Read(reader));

        Assert.Equal("協議", entry.Surface);
        Assert.Equal(6000, entry.Cost);
    }
}
