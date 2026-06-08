namespace ShortcutIme.Evaluation;

/// <summary>
/// 評価用の1事例。<see cref="Reading"/> から合成入力（子音のみ／フルローマ字）を作り、
/// <see cref="Sentence"/> を正解（gold）として変換結果と突き合わせる。
/// </summary>
/// <param name="Sentence">正解の表層文（例：今日はありがとうございました）。</param>
/// <param name="Reading">全文の読み（ひらがな、助詞も表記どおり。例：きょうはありがとうございました）。</param>
public sealed record EvalCase(string Sentence, string Reading);
