namespace ShortcutIme.Core.Tests;

public class ConsonantEncoderTests
{
    private readonly ConsonantEncoder _encoder = new();

    [Theory]
    [InlineData("きょう", "ky")]        // 今日（長音「おう」は省略）
    [InlineData("きょういく", "kyik")]  // 教育（育=いく の音節母音「い」は残す）
    [InlineData("きょうゆう", "kyy")]   // 共有（長音「う」は省略）
    [InlineData("きょうぎ", "kyg")]     // 協議
    [InlineData("きょよう", "kyy")]     // 許容（よう の長音「う」は省略）
    [InlineData("あい", "ai")]          // 愛（音節母音はどちらも残す）
    [InlineData("いえ", "ie")]          // 家（子音を持たない音節母音は残す）
    [InlineData("えき", "ek")]          // 駅
    [InlineData("しゅうまつ", "symt")]  // 週末（長音「しゅう」の「う」は省略）
    [InlineData("せんせい", "snsi")]    // えい は保守的に残す（語境界の誤脱落回避）
    [InlineData("をおくる", "wokr")]    // 助詞「を」＋「お」始まりの語：境界の「お」は残す
    [InlineData("おはよう", "ohy")]     // 語頭「お」は残し、長音「よう」の「う」は省略
    [InlineData("がっこう", "gkk")]     // 促音は頭子音を重ねる
    [InlineData("ゆっくり", "ykkr")]    // 促音
    [InlineData("かった", "ktt")]       // 促音（かた="kt" と区別できる）
    [InlineData("こーひー", "kh")]      // 長音「ー」は無視
    [InlineData("ほん", "hn")]          // 撥音「ん」→ n
    [InlineData("しゃちょう", "syty")]  // 連続する拗音
    [InlineData("ふぁいる", "fir")]     // 外来小書き音 ふぁ=fa（小母音が脱落しない）
    [InlineData("てぃーむ", "thm")]     // てぃ=thi、長音ー脱落
    public void Encode_ProducesExpectedConsonantKey(string reading, string expected)
        => Assert.Equal(expected, _encoder.Encode(reading));

    // 正確さの不変条件：子音入力は、フルローマ字（トライ索引の形）から母音だけ削った部分列でなければ
    // ならない。これが破れると実際の探索が gold にマッチできず、テスト生成が「出題ミス」になる。
    [Theory]
    [InlineData("きょう")]
    [InlineData("きょういく")]
    [InlineData("あい")]
    [InlineData("いえ")]
    [InlineData("がっこう")]
    [InlineData("かった")]
    [InlineData("ゆっくり")]
    [InlineData("こーひー")]
    [InlineData("ほん")]
    [InlineData("しゃちょう")]
    [InlineData("ありがとうございました")]
    [InlineData("ふぁいる")]
    [InlineData("ゔぁいおりん")]
    [InlineData("うぃーく")]
    [InlineData("てぃーむ")]
    public void ConsonantKey_IsVowelElidedSubsequenceOfFullRomaji(string reading)
    {
        var full = new RomajiEncoder().Encode(reading);
        var consonant = _encoder.Encode(reading);

        Assert.True(IsSubsequence(consonant, full), $"\"{consonant}\" は \"{full}\" の部分列でない");
        Assert.Equal(RemoveVowels(full), RemoveVowels(consonant)); // 子音骨格が一致（子音の脱落・追加なし）
    }

    [Theory]
    [InlineData("しごと", "shgt")]      // し→shi
    [InlineData("しゃちょう", "shch")]  // しゃ→sh, ちょ→ch
    [InlineData("つづき", "tszk")]      // つ→ts, づ→z
    [InlineData("ふじ", "fj")]          // ふ→f, じ→j
    [InlineData("きょう", "ky")]        // 方式差なし
    public void Encode_Hepburn_ProducesHepburnConsonantKey(string reading, string expected)
        => Assert.Equal(expected, new ConsonantEncoder(RomajiScheme.Hepburn).Encode(reading));

    [Theory]
    [InlineData("しごと")]
    [InlineData("しゃちょう")]
    [InlineData("つづき")]
    [InlineData("がっこう")]
    public void HepburnConsonantKey_IsVowelElidedSubsequenceOfHepburnFull(string reading)
    {
        var full = new RomajiEncoder(RomajiScheme.Hepburn).Encode(reading);
        var consonant = new ConsonantEncoder(RomajiScheme.Hepburn).Encode(reading);

        Assert.True(IsSubsequence(consonant, full), $"\"{consonant}\" は \"{full}\" の部分列でない");
        Assert.Equal(RemoveVowels(full), RemoveVowels(consonant));
    }

    private static bool IsSubsequence(string sub, string full)
    {
        var i = 0;
        foreach (var c in full)
        {
            if (i < sub.Length && sub[i] == c)
            {
                i++;
            }
        }

        return i == sub.Length;
    }

    private static string RemoveVowels(string s) => string.Concat(s.Where(c => !"aiueo".Contains(c)));
}
