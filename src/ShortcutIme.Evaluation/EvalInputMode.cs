namespace ShortcutIme.Evaluation;

/// <summary>
/// 合成入力の打鍵モード。製品では母音を自由に混ぜられるため、両端を測る：
/// <see cref="Consonant"/>（最短・最も曖昧＝リランカーの価値が最大）と
/// <see cref="Full"/>（フル入力＝「打てば出る」非劣化保証）。
/// </summary>
public enum EvalInputMode
{
    /// <summary>子音のみ（母音を一切打たない最短入力）。</summary>
    Consonant,

    /// <summary>フルローマ字（母音をすべて打つ）。</summary>
    Full,
}
