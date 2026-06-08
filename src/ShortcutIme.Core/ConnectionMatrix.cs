using System.Globalization;

namespace ShortcutIme.Core;

/// <summary>
/// Mozc の連接コスト行列（前単語の右文脈ID × 次単語の左文脈ID → コスト。小さいほど自然な連接）。
/// connection_single_column.txt：1行目に行列サイズ、以降 size×size 個のコストが
/// row-major（index = prevRightId * size + nextLeftId）で1列に並ぶ。
/// </summary>
public sealed class ConnectionMatrix
{
    private readonly int _size;
    private readonly short[] _costs;

    private ConnectionMatrix(int size, short[] costs)
    {
        _size = size;
        _costs = costs;
    }

    /// <summary>前単語の右文脈IDと次単語の左文脈IDから連接コストを返す（範囲外は 0）。</summary>
    public int Cost(int prevRightId, int nextLeftId)
    {
        if ((uint)prevRightId >= (uint)_size || (uint)nextLeftId >= (uint)_size)
        {
            return 0;
        }
        return _costs[(prevRightId * _size) + nextLeftId];
    }

    /// <summary>connection_single_column.txt を読み込む。</summary>
    public static ConnectionMatrix Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        using var reader = new StreamReader(path);
        var size = int.Parse(reader.ReadLine() ?? "0", CultureInfo.InvariantCulture);
        var costs = new short[size * size];
        for (var i = 0; i < costs.Length; i++)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                break;
            }
            costs[i] = short.Parse(line, CultureInfo.InvariantCulture);
        }
        return new ConnectionMatrix(size, costs);
    }
}
