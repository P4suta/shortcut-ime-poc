namespace ShortcutIme.Core;

/// <summary>
/// 連文節ラティスの1辺。入力のある位置から <see cref="Length"/> 文字を消費してマッチした候補。
/// </summary>
/// <param name="Candidate">マッチした単語候補。</param>
/// <param name="Length">消費した入力文字数。</param>
/// <param name="VowelSkips">マッチ中にスキップした（入力に無い）母音の数。少ないほど入力に忠実。</param>
public readonly record struct SegmentMatch(Candidate Candidate, int Length, int VowelSkips);
