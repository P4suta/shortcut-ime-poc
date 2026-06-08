using System.Text;

namespace ShortcutIme.Core;

/// <summary>
/// 読みを打鍵列へ変換する単一の primitive。モーラ走査・促音重ね・長音判定を一元化し、
/// 「各 OPTIONAL 母音を残すか」だけを <paramref name="keepOptionalVowel"/> 述語に委ねる。
/// <list type="bullet">
///   <item><b>OPTIONAL 母音</b>＝子音を持つモーラの末尾母音（か→<c>k|a</c>、きょ→<c>ky|o</c>）と、
///   確信度の高い長音「う」（おう＝<c>(o,u)</c>・うう＝<c>(u,u)</c>）。保持は述語が決める。</item>
///   <item><b>MANDATORY 母音</b>＝それ以外の母音単独モーラ（あ・語頭い・を境界お・おお・えい）。
///   押せる子音が無く打つしかないため常に残す。</item>
/// </list>
/// これにより <c>keepOptionalVowel=_=>false</c> が <see cref="ConsonantEncoder"/>、
/// <c>=_=>true</c> が <see cref="RomajiEncoder"/> に構成的に一致する。
/// </summary>
public static class MoraKeystrokeWalker
{
    private const string Vowels = "aiueo";

    /// <summary>OPTIONAL 母音を残すか判定するための情報。</summary>
    /// <param name="OptionalVowelIndex">語内の OPTIONAL 母音の連番（0始まり、決定的判定用）。</param>
    /// <param name="Vowel">対象の母音文字（a/i/u/e/o）。</param>
    /// <param name="Reading">変換中の読み全体。</param>
    public readonly record struct VowelDecision(int OptionalVowelIndex, char Vowel, string Reading);

    /// <summary>読みを打鍵列へ変換する。OPTIONAL 母音の保持のみ述語に委ねる。</summary>
    public static string Encode(string reading, RomajiScheme scheme, Func<VowelDecision, bool> keepOptionalVowel)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(keepOptionalVowel);

        var key = new StringBuilder(reading.Length * 2);
        var prevVowel = '\0'; // 直前モーラの母音（長音判定用）。先頭・撥音後は番兵。
        var sokuon = false;   // 直前が促音「っ」か。
        var optionalIndex = 0;
        foreach (var mora in MoraSplitter.Split(reading))
        {
            if (mora.Kana == "っ")
            {
                sokuon = true; // 促音：次モーラの頭子音を重ねる。
                continue;
            }

            var romaji = KanaRomanization.Romanize(mora, scheme);
            if (romaji.Length == 0)
            {
                continue; // 長音「ー」・未知文字は打鍵に現れない。
            }

            var hasVowel = Vowels.Contains(romaji[^1]);
            var vowel = hasVowel ? romaji[^1] : '\0';
            var skeleton = hasVowel ? romaji[..^1] : romaji; // モーラ核は1母音＝末尾。ん等は母音なし。

            if (skeleton.Length > 0)
            {
                if (sokuon)
                {
                    key.Append(skeleton[0]); // 促音による頭子音の重ね。
                }

                key.Append(skeleton);

                if (hasVowel)
                {
                    AppendIfKept(key, keepOptionalVowel, ref optionalIndex, vowel, reading);
                }
            }
            else if (hasVowel)
            {
                // 子音を持たない母音モーラ。確信長音だけ OPTIONAL、それ以外は MANDATORY。
                if (IsLongVowel(prevVowel, vowel))
                {
                    AppendIfKept(key, keepOptionalVowel, ref optionalIndex, vowel, reading);
                }
                else
                {
                    key.Append(vowel);
                }
            }

            sokuon = false;
            prevVowel = hasVowel ? vowel : '\0';
        }

        return key.ToString();
    }

    private static void AppendIfKept(
        StringBuilder key, Func<VowelDecision, bool> keep, ref int optionalIndex, char vowel, string reading)
    {
        if (keep(new VowelDecision(optionalIndex, vowel, reading)))
        {
            key.Append(vowel);
        }

        optionalIndex++;
    }

    // 確信度の高い「う」長音だけを OPTIONAL 扱い。おお・えい・同母音などは語境界での誤脱落を避け MANDATORY。
    private static bool IsLongVowel(char prevVowel, char vowel) => (prevVowel, vowel) switch
    {
        ('o', 'u') => true, // おう → ô（きょう・ありがとう）
        ('u', 'u') => true, // うう → û（しゅう・すうじ）
        _ => false,
    };
}
