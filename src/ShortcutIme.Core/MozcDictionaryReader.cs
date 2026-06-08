using System.Globalization;
using System.Text;

namespace ShortcutIme.Core;

/// <summary>
/// Mozc の dictionary_oss（5列 TSV：読み・左ID・右ID・コスト・表層、UTF-8）を読み取る。
/// </summary>
public static class MozcDictionaryReader
{
    private const int FieldCount = 5;
    private const int ReadingIndex = 0;
    private const int LeftIdIndex = 1;
    private const int RightIdIndex = 2;
    private const int CostIndex = 3;
    private const int SurfaceIndex = 4;

    /// <summary>
    /// テキストリーダから辞書エントリを遅延列挙する。列不足・数値不正・空フィールドの行は読み飛ばす。
    /// </summary>
    public static IEnumerable<DictionaryEntry> Read(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var fields = line.Split('\t');
            if (fields.Length < FieldCount)
            {
                continue;
            }

            if (!TryParseInt(fields[LeftIdIndex], out var leftId)
                || !TryParseInt(fields[RightIdIndex], out var rightId)
                || !TryParseInt(fields[CostIndex], out var cost))
            {
                continue;
            }

            var reading = fields[ReadingIndex];
            var surface = fields[SurfaceIndex];
            if (reading.Length == 0 || surface.Length == 0)
            {
                continue;
            }

            yield return new DictionaryEntry(reading, surface, cost, leftId, rightId);
        }
    }

    /// <summary>指定パスの辞書ファイル（UTF-8）を遅延列挙する。</summary>
    public static IEnumerable<DictionaryEntry> ReadFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        using var reader = new StreamReader(path, Encoding.UTF8);
        foreach (var entry in Read(reader))
        {
            yield return entry;
        }
    }

    private static bool TryParseInt(string value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
}
