namespace ShortcutIme.Core;

/// <summary>Mozc 辞書の1エントリ。</summary>
/// <param name="Reading">読み（ひらがな）。</param>
/// <param name="Surface">表層（漢字・かな・カタカナ）。</param>
/// <param name="Cost">単語生起コスト。小さいほど高頻度（尤度が高い）。</param>
/// <param name="LeftId">左文脈 ID（連接コスト用。連文節変換で使う想定で保持）。</param>
/// <param name="RightId">右文脈 ID（同上）。</param>
public sealed record DictionaryEntry(string Reading, string Surface, int Cost, int LeftId = 0, int RightId = 0);
