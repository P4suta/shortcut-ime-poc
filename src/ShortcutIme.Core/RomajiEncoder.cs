namespace ShortcutIme.Core;

/// <summary>
/// 読みをフルローマ字へ変換する <see cref="IReadingEncoder"/>（トライ索引用の完全形）。
/// <see cref="MoraKeystrokeWalker"/> に OPTIONAL 母音を常に残す方針（<c>_ => true</c>）を渡す薄ラッパ。
/// </summary>
public sealed class RomajiEncoder : IReadingEncoder
{
    private readonly RomajiScheme _scheme;

    /// <param name="scheme">ローマ字方式（既定は訓令式）。</param>
    public RomajiEncoder(RomajiScheme scheme = RomajiScheme.Kunrei) => _scheme = scheme;

    /// <inheritdoc />
    public string Encode(string reading) => MoraKeystrokeWalker.Encode(reading, _scheme, static _ => true);
}
