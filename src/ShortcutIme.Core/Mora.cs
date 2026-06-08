namespace ShortcutIme.Core;

/// <summary>
/// 日本語の音の最小単位（モーラ）。拗音「きょ」は1モーラとして扱う。
/// </summary>
/// <param name="Kana">モーラを構成するひらがな（1〜2文字）。</param>
public readonly record struct Mora(string Kana);
