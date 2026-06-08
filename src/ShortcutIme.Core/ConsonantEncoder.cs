namespace ShortcutIme.Core;

/// <summary>
/// 読みを「子音入力」の打鍵列へ変換する <see cref="IReadingEncoder"/>（実ユーザの最小打鍵を模す）。
/// <see cref="MoraKeystrokeWalker"/> に OPTIONAL 母音を一切残さない方針（<c>_ => false</c>）を渡す薄ラッパ。
/// 規則：
/// <list type="bullet">
///   <item>子音を持つモーラ（か→ka, きょ→kyo など）は頭子音のみ（母音は省ける）。</item>
///   <item>子音を持たない母音モーラ（あいうえお）は母音字を残す——押せる子音が無く、母音を打つしかないため。</item>
///   <item>長音の「う」（おう・うう）と長音記号「ー」のみ省略する。
///   おお・えい・同母音の連続などは、助詞・語境界をまたいだ別音節を誤って落とさないよう保守的に残す。</item>
///   <item>撥音「ん」→ n。促音「っ」・長音「ー」・未知文字は打鍵に現れない（省略）。</item>
/// </list>
/// 例：きょう→ky、きょういく→kyik、あい→ai、しゅうまつ→symt、せんせい→snsi。
/// </summary>
public sealed class ConsonantEncoder : IReadingEncoder
{
    private readonly RomajiScheme _scheme;

    /// <param name="scheme">ローマ字方式（既定は訓令式）。し→s/sh, ち→t/ch などが変わる。</param>
    public ConsonantEncoder(RomajiScheme scheme = RomajiScheme.Kunrei) => _scheme = scheme;

    /// <inheritdoc />
    public string Encode(string reading) => MoraKeystrokeWalker.Encode(reading, _scheme, static _ => false);
}
