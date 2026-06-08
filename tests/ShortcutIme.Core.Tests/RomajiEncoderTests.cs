namespace ShortcutIme.Core.Tests;

public class RomajiEncoderTests
{
    private readonly RomajiEncoder _encoder = new();

    [Theory]
    [InlineData("きょう", "kyou")]       // 今日
    [InlineData("きょういく", "kyouiku")] // 教育
    [InlineData("きょうゆう", "kyouyuu")] // 共有
    [InlineData("きょうぎ", "kyougi")]    // 協議
    [InlineData("きょよう", "kyoyou")]    // 許容
    [InlineData("あい", "ai")]            // 愛
    [InlineData("えき", "eki")]           // 駅
    [InlineData("がっこう", "gakkou")]    // 促音は次子音を重ねる
    [InlineData("かった", "katta")]       // 促音（かた と区別できる）
    [InlineData("ゆっくり", "yukkuri")]   // 促音
    [InlineData("こーひー", "kohi")]      // 長音記号「ー」は省略（簡易化・要再検討）
    [InlineData("ほん", "hon")]           // 撥音 ん→n
    [InlineData("しゃちょう", "syatyou")] // 連続する拗音
    public void Encode_ProducesFullRomaji(string reading, string expected)
        => Assert.Equal(expected, _encoder.Encode(reading));

    [Theory]
    [InlineData("しごと", "shigoto")]   // し→shi
    [InlineData("しゃちょう", "shachou")] // しゃ→sha, ちょ→cho
    [InlineData("つづき", "tsuzuki")]   // つ→tsu, づ→zu
    [InlineData("ふじ", "fuji")]        // ふ→fu, じ→ji
    [InlineData("きょう", "kyou")]      // 方式差なし
    [InlineData("がっこう", "gakkou")]  // 促音は方式非依存
    public void Encode_Hepburn_ProducesHepburnRomaji(string reading, string expected)
        => Assert.Equal(expected, new RomajiEncoder(RomajiScheme.Hepburn).Encode(reading));
}
