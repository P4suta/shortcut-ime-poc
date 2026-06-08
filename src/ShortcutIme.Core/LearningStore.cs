using System.Text.Json;

namespace ShortcutIme.Core;

/// <summary>
/// 選択された表層の「最終選択通番」を保持する学習ストア。最近選んだ候補ほど大きい値を返し、
/// ランキングの recency ブーストに使う。JSON で永続化できる。
/// </summary>
public sealed class LearningStore
{
    private readonly Dictionary<string, long> _lastUsed;
    private long _sequence;

    /// <summary>空の学習ストアを作る。</summary>
    public LearningStore()
        : this(0, [])
    {
    }

    private LearningStore(long sequence, Dictionary<string, long> lastUsed)
    {
        _sequence = sequence;
        _lastUsed = lastUsed;
    }

    /// <summary>表層の recency（未選択は0、最近選んだものほど大きい）。</summary>
    public long RecencyOf(string surface) => _lastUsed.GetValueOrDefault(surface);

    /// <summary>表層の選択を記録する。</summary>
    public void Record(string surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _lastUsed[surface] = ++_sequence;
    }

    /// <summary>学習内容を JSON ファイルへ保存する。</summary>
    public void Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var state = new PersistedState(_sequence, _lastUsed);
        File.WriteAllText(path, JsonSerializer.Serialize(state));
    }

    /// <summary>JSON ファイルから読み込む。存在しなければ空のストアを返す。</summary>
    public static LearningStore Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            return new LearningStore();
        }

        var state = JsonSerializer.Deserialize<PersistedState>(File.ReadAllText(path));
        return state is null ? new LearningStore() : new LearningStore(state.Sequence, state.LastUsed);
    }

    private sealed record PersistedState(long Sequence, Dictionary<string, long> LastUsed);
}
