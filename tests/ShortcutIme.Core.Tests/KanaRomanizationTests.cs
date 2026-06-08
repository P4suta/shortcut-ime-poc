namespace ShortcutIme.Core.Tests;

public class KanaRomanizationTests
{
    [Theory]
    [InlineData("か", "ka")]
    [InlineData("し", "si")]
    [InlineData("きょ", "kyo")]
    [InlineData("しゃ", "sya")]
    [InlineData("ん", "n")]
    [InlineData("っ", "")]
    [InlineData("ー", "")]
    [InlineData("ゔ", "vu")]      // 外来音ヴ（単独）
    [InlineData("ふぁ", "fa")]    // 外来小書き音（拗音ではない）
    [InlineData("ふぉ", "fo")]
    [InlineData("ゔぁ", "va")]
    [InlineData("うぃ", "wi")]
    [InlineData("てぃ", "thi")]   // ち=ti との衝突回避
    [InlineData("でぃ", "dhi")]
    [InlineData("とぅ", "twu")]
    [InlineData("つぁ", "tsa")]
    [InlineData("しぇ", "sye")]   // 訓令式
    public void Romanize_ConvertsMora(string kana, string expected)
        => Assert.Equal(expected, KanaRomanization.Romanize(new Mora(kana)));

    [Theory]
    [InlineData("ふぁ", "fa")]    // 方式差なし
    [InlineData("てぃ", "thi")]
    [InlineData("しぇ", "she")]   // ヘボン式（訓令式 sye と異なる）
    [InlineData("ちぇ", "che")]
    [InlineData("じぇ", "je")]
    [InlineData("し", "shi")]     // 既存の方式差（回帰確認）
    public void Romanize_Hepburn_ConvertsMora(string kana, string expected)
        => Assert.Equal(expected, KanaRomanization.Romanize(new Mora(kana), RomajiScheme.Hepburn));
}
