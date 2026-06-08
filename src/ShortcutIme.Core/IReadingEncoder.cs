namespace ShortcutIme.Core;

/// <summary>読み（ひらがな）を子音ショートカットキーへ変換する戦略。</summary>
public interface IReadingEncoder
{
    /// <summary>読みを子音キーへ変換する。</summary>
    string Encode(string reading);
}
