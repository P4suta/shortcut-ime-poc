using ShortcutIme.Core;

namespace ShortcutIme.Evaluation;

/// <summary>
/// 合成入力が「出題として公平か」＝必ず gold へ照合可能かを監査する（[[eval-input-must-be-faithful]] の一般化）。
/// 旧監査は「単一フル骨格の母音削除部分列か」だったが、を→o・ん→nn・活用追加で骨格が変わると破綻する。
/// 二層で検査する：
/// <list type="number">
///   <item><see cref="IsFaithful"/>＝文字列レベル。入力が異形集合のいずれかの<b>母音削除形</b>か（高速）。</item>
///   <item><see cref="IsReachable"/>＝operational。実際にトライで gold 表層へ到達できるか（真の真実源）。</item>
/// </list>
/// </summary>
public static class InputFaithfulnessAuditor
{
    private const string Vowels = "aiueo";

    /// <summary>
    /// 入力が、読みの異形のいずれかから母音だけを削って得られる形か。
    /// <paramref name="expand"/> 既定は <see cref="RomajiVariants.ExpandReading"/>（Stage 3 で癖込み展開に差し替え可能）。
    /// </summary>
    public static bool IsFaithful(string input, string reading, Func<string, IReadOnlyList<string>>? expand = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(reading);
        expand ??= r => RomajiVariants.ExpandReading(r);

        foreach (var variant in expand(reading))
        {
            if (IsSubsequence(input, variant) && RemoveVowels(input) == RemoveVowels(variant))
            {
                return true; // input は variant から母音だけ落とした形＝照合可能。
            }
        }

        return false;
    }

    /// <summary>トライ検索で gold 表層が入力から到達可能か（operational＝真の真実源）。</summary>
    public static bool IsReachable(RomajiTrie trie, string input, string surface)
    {
        ArgumentNullException.ThrowIfNull(trie);
        ArgumentNullException.ThrowIfNull(input);
        return input.Length > 0 && trie.Search(input).Any(c => c.Surface == surface);
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

    private static string RemoveVowels(string s) => string.Concat(s.Where(c => !Vowels.Contains(c)));
}
