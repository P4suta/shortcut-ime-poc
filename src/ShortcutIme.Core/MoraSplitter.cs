using System.Buffers;

namespace ShortcutIme.Core;

/// <summary>
/// ひらがなの読みをモーラ列へ分割する。小書き仮名（ゃゅょ・ぁぃぅぇぉ・ゎ）は
/// 直前の仮名と結合して1モーラとする。
/// </summary>
public static class MoraSplitter
{
    private static readonly SearchValues<char> SmallKana =
        SearchValues.Create("ゃゅょぁぃぅぇぉゎ");

    /// <summary>読み（ひらがな）をモーラ列に分割する。</summary>
    public static IReadOnlyList<Mora> Split(string reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        var moras = new List<Mora>(reading.Length);
        for (var i = 0; i < reading.Length; i++)
        {
            var isYoon = i + 1 < reading.Length && SmallKana.Contains(reading[i + 1]);
            var length = isYoon ? 2 : 1;
            moras.Add(new Mora(reading[i..(i + length)]));
            i += length - 1;
        }
        return moras;
    }
}
