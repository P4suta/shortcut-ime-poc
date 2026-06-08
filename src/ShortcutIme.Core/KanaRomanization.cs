using System.Collections.Frozen;

namespace ShortcutIme.Core;

/// <summary>かなのモーラを訓令式ローマ字へ変換する（子音ショートカット索引の基盤）。</summary>
public static class KanaRomanization
{
    private static readonly FrozenDictionary<char, string> MoraTable = new Dictionary<char, string>
    {
        ['あ'] = "a", ['い'] = "i", ['う'] = "u", ['え'] = "e", ['お'] = "o",
        ['か'] = "ka", ['き'] = "ki", ['く'] = "ku", ['け'] = "ke", ['こ'] = "ko",
        ['が'] = "ga", ['ぎ'] = "gi", ['ぐ'] = "gu", ['げ'] = "ge", ['ご'] = "go",
        ['さ'] = "sa", ['し'] = "si", ['す'] = "su", ['せ'] = "se", ['そ'] = "so",
        ['ざ'] = "za", ['じ'] = "zi", ['ず'] = "zu", ['ぜ'] = "ze", ['ぞ'] = "zo",
        ['た'] = "ta", ['ち'] = "ti", ['つ'] = "tu", ['て'] = "te", ['と'] = "to",
        ['だ'] = "da", ['ぢ'] = "di", ['づ'] = "du", ['で'] = "de", ['ど'] = "do",
        ['な'] = "na", ['に'] = "ni", ['ぬ'] = "nu", ['ね'] = "ne", ['の'] = "no",
        ['は'] = "ha", ['ひ'] = "hi", ['ふ'] = "hu", ['へ'] = "he", ['ほ'] = "ho",
        ['ば'] = "ba", ['び'] = "bi", ['ぶ'] = "bu", ['べ'] = "be", ['ぼ'] = "bo",
        ['ぱ'] = "pa", ['ぴ'] = "pi", ['ぷ'] = "pu", ['ぺ'] = "pe", ['ぽ'] = "po",
        ['ま'] = "ma", ['み'] = "mi", ['む'] = "mu", ['め'] = "me", ['も'] = "mo",
        ['や'] = "ya", ['ゆ'] = "yu", ['よ'] = "yo",
        ['ら'] = "ra", ['り'] = "ri", ['る'] = "ru", ['れ'] = "re", ['ろ'] = "ro",
        ['わ'] = "wa", ['を'] = "wo", ['ん'] = "n",
        ['ゔ'] = "vu", // 外来音ヴ。
    }.ToFrozenDictionary();

    // 外来小書き音（ぁぃぅぇぉ を伴う2文字モーラ）。拗音（y挿入）ではないため専用表で扱う。
    // 値はキーボードで実際に打たれる綴り。ち=ti・つ=tu・ぢ=di（訓令式）との衝突を避けるため
    // てぃ→thi・でぃ→dhi・とぅ→twu・どぅ→dwu とする。
    private static readonly FrozenDictionary<string, (string Kunrei, string Hepburn)> ForeignMora =
        new Dictionary<string, (string, string)>
        {
            ["ふぁ"] = ("fa", "fa"), ["ふぃ"] = ("fi", "fi"), ["ふぇ"] = ("fe", "fe"), ["ふぉ"] = ("fo", "fo"),
            ["ゔぁ"] = ("va", "va"), ["ゔぃ"] = ("vi", "vi"), ["ゔぇ"] = ("ve", "ve"), ["ゔぉ"] = ("vo", "vo"),
            ["うぃ"] = ("wi", "wi"), ["うぇ"] = ("we", "we"), ["うぉ"] = ("wo", "wo"),
            ["てぃ"] = ("thi", "thi"), ["でぃ"] = ("dhi", "dhi"),
            ["とぅ"] = ("twu", "twu"), ["どぅ"] = ("dwu", "dwu"),
            ["つぁ"] = ("tsa", "tsa"), ["つぃ"] = ("tsi", "tsi"), ["つぇ"] = ("tse", "tse"), ["つぉ"] = ("tso", "tso"),
            ["しぇ"] = ("sye", "she"), ["ちぇ"] = ("tye", "che"), ["じぇ"] = ("zye", "je"),
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<char, char> SmallVowels = new Dictionary<char, char>
    {
        ['ゃ'] = 'a', ['ゅ'] = 'u', ['ょ'] = 'o',
    }.ToFrozenDictionary();

    // ヘボン式で訓令式と異なる単独モーラ。
    private static readonly FrozenDictionary<char, string> HepburnSingle = new Dictionary<char, string>
    {
        ['し'] = "shi", ['じ'] = "ji", ['ち'] = "chi", ['ぢ'] = "ji",
        ['つ'] = "tsu", ['づ'] = "zu", ['ふ'] = "fu",
    }.ToFrozenDictionary();

    // ヘボン式の拗音頭子音（し→sh, ち→ch, じ→j）。+小書き母音で sha/cha/ja。
    private static readonly FrozenDictionary<char, string> HepburnYoonOnset = new Dictionary<char, string>
    {
        ['し'] = "sh", ['ち'] = "ch", ['じ'] = "j", ['ぢ'] = "j",
    }.ToFrozenDictionary();

    /// <summary>
    /// 1モーラをローマ字へ変換する。拗音「きょ」→ kyo（頭子音+y+小書き母音）。
    /// 促音「っ」・長音「ー」・未知の文字は空文字。
    /// </summary>
    public static string Romanize(Mora mora)
    {
        var kana = mora.Kana;

        if (kana.Length == 2)
        {
            if (ForeignMora.TryGetValue(kana, out var foreign))
            {
                return foreign.Kunrei;
            }

            if (SmallVowels.TryGetValue(kana[1], out var smallVowel)
                && MoraTable.TryGetValue(kana[0], out var baseRomaji)
                && baseRomaji.Length >= 2)
            {
                return $"{baseRomaji[..^1]}y{smallVowel}";
            }
        }

        return MoraTable.TryGetValue(kana[0], out var romaji) ? romaji : "";
    }

    /// <summary>
    /// 指定方式で1モーラをローマ字へ変換する。<see cref="RomajiScheme.Hepburn"/> で差があるモーラのみ
    /// ヘボン式（し→shi・ちょ→cho 等）、それ以外は訓令式と同一。
    /// </summary>
    public static string Romanize(Mora mora, RomajiScheme scheme)
    {
        if (scheme == RomajiScheme.Kunrei)
        {
            return Romanize(mora);
        }

        var kana = mora.Kana;
        if (kana.Length == 2)
        {
            if (ForeignMora.TryGetValue(kana, out var foreign))
            {
                return foreign.Hepburn;
            }

            if (SmallVowels.TryGetValue(kana[1], out var smallVowel)
                && HepburnYoonOnset.TryGetValue(kana[0], out var onset))
            {
                return $"{onset}{smallVowel}"; // sh + a = sha
            }
        }

        if (kana.Length == 1 && HepburnSingle.TryGetValue(kana[0], out var hepburn))
        {
            return hepburn;
        }

        return Romanize(mora);
    }
}
