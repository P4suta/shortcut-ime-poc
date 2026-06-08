namespace ShortcutIme.Evaluation;

/// <summary>
/// 評価用テストセットの読み込み。各行は「表層文 &lt;TAB&gt; 読み」。'#' 始まりと空行は無視。
/// 表層・読みともに ASCII 空白を含まない前提のため、区切りはタブ優先・空白フォールバックで頑健に取る。
/// </summary>
public static class EvalDataset
{
    /// <summary>TSV ファイルから事例を読み込む。</summary>
    public static IReadOnlyList<EvalCase> LoadTsv(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var cases = new List<EvalCase>();
        var lineNumber = 0;
        foreach (var raw in File.ReadLines(path))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var separator = line.IndexOf('\t');
            if (separator < 0)
            {
                separator = line.IndexOf(' ');
            }

            if (separator <= 0)
            {
                throw new FormatException($"{lineNumber} 行目: 「表層<TAB>読み」を期待しましたが区切りがありません: {raw}");
            }

            var sentence = line[..separator].Trim();
            var reading = line[(separator + 1)..].Trim();
            if (sentence.Length == 0 || reading.Length == 0)
            {
                throw new FormatException($"{lineNumber} 行目: 表層または読みが空です: {raw}");
            }

            cases.Add(new EvalCase(sentence, reading));
        }

        return cases;
    }
}
