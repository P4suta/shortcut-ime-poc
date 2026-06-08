namespace ShortcutIme.Core;

/// <summary>
/// ローマ字入力方式。子音入力は方式で打鍵が変わるため、索引・生成の両方をこの軸で扱う。
/// </summary>
public enum RomajiScheme
{
    /// <summary>訓令式（し=si, ち=ti, つ=tu, じ=zi, ふ=hu, しゃ=sya）。既定・正準形。</summary>
    Kunrei,

    /// <summary>ヘボン式（し=shi, ち=chi, つ=tsu, じ=ji, ふ=fu, しゃ=sha）。</summary>
    Hepburn,
}
