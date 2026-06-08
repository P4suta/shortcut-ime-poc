namespace ShortcutIme.Core;

/// <summary>変換候補。</summary>
/// <param name="Surface">表層（例：共有）。</param>
/// <param name="Reading">読み（例：きょうゆう）。</param>
/// <param name="Cost">単語生起コスト。小さいほど高頻度。</param>
/// <param name="LeftId">左文脈 ID（連接コスト用）。</param>
/// <param name="RightId">右文脈 ID（連接コスト用）。</param>
public sealed record Candidate(string Surface, string Reading, int Cost, int LeftId = 0, int RightId = 0);
