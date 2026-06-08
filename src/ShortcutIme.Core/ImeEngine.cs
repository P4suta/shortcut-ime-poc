namespace ShortcutIme.Core;

/// <summary>
/// 子音補完エンジンのファサード。入力（子音必須・母音オプション）から候補を引き、
/// 学習 recency 降順 → コスト昇順で並べて返す。UI はこの型だけを使えばよい。
/// </summary>
public sealed class ImeEngine
{
    private readonly RomajiTrie _trie;
    private readonly LearningStore _learning;

    /// <summary>トライと学習ストアからエンジンを作る。</summary>
    public ImeEngine(RomajiTrie trie, LearningStore learning)
    {
        ArgumentNullException.ThrowIfNull(trie);
        ArgumentNullException.ThrowIfNull(learning);
        _trie = trie;
        _learning = learning;
    }

    /// <summary>入力にマッチする候補を、学習 recency 降順 → コスト昇順で返す。</summary>
    public IReadOnlyList<Candidate> Convert(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return _trie.Search(input)
            .OrderByDescending(candidate => _learning.RecencyOf(candidate.Surface))
            .ThenBy(candidate => candidate.Cost)
            .ToList();
    }

    /// <summary>確定した候補を学習する（次回以降の recency ブースト）。</summary>
    public void Commit(Candidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        _learning.Record(candidate.Surface);
    }
}
