namespace ShortcutIme.Core;

/// <summary>
/// 子音（母音オプション）の入力列を文節に分割し、最尤の単語列へ一括変換する（連文節変換）。
/// 生起コストの和＋文節数ペナルティをビタビ探索で最小化する。連接コスト（lid/rid）は未使用——
/// 生起コストのみで精度が不足する場合に追加する想定。
/// </summary>
public sealed class PhraseConverter
{
    private readonly RomajiTrie _trie;
    private readonly ConnectionMatrix? _connection;
    private readonly int _segmentPenalty;
    private readonly int _vowelSkipPenalty;

    /// <param name="trie">フルローマ字のトライ。</param>
    /// <param name="connection">連接コスト行列（null なら連接コストを 0 として扱う）。</param>
    /// <param name="segmentPenalty">1文節ごとに加算するペナルティ（短い分割の乱立を抑える）。</param>
    /// <param name="vowelSkipPenalty">スキップした母音1つごとのペナルティ（入力に忠実な＝母音を打った経路を優先）。</param>
    public PhraseConverter(RomajiTrie trie, ConnectionMatrix? connection = null, int segmentPenalty = 3000, int vowelSkipPenalty = 2000)
    {
        ArgumentNullException.ThrowIfNull(trie);
        _trie = trie;
        _connection = connection;
        _segmentPenalty = segmentPenalty;
        _vowelSkipPenalty = vowelSkipPenalty;
    }

    /// <summary>入力列を最尤の文節（単語）列へ変換する。全体を覆う分割が無ければ空を返す。</summary>
    public IReadOnlyList<Candidate> Convert(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var n = input.Length;
        if (n == 0)
        {
            return [];
        }

        var cost = new long[n + 1];
        var back = new (int Prev, Candidate Word)?[n + 1];
        Array.Fill(cost, long.MaxValue);
        cost[0] = 0;

        for (var i = 0; i < n; i++)
        {
            if (cost[i] == long.MaxValue)
            {
                continue;
            }

            var prevRightId = back[i]?.Word.RightId ?? 0; // 文頭は BOS(0)
            foreach (var segment in _trie.SegmentsFrom(input, i))
            {
                var j = i + segment.Length;
                if (j > n)
                {
                    continue;
                }

                var connectionCost = _connection?.Cost(prevRightId, segment.Candidate.LeftId) ?? 0;
                var next = cost[i] + segment.Candidate.Cost + connectionCost
                    + _segmentPenalty + (_vowelSkipPenalty * segment.VowelSkips);
                if (next < cost[j])
                {
                    cost[j] = next;
                    back[j] = (i, segment.Candidate);
                }
            }
        }

        if (back[n] is null)
        {
            return [];
        }

        var result = new List<Candidate>();
        for (var k = n; k > 0;)
        {
            var (prev, word) = back[k]!.Value;
            result.Add(word);
            k = prev;
        }

        result.Reverse();
        return result;
    }

    // n-best 用の部分経路。Cost＝総コスト、RightId＝末尾語の右文脈ID（連接コスト用）、
    // (PrevPos, PrevIndex)＝直前位置の partials 中の参照、Word＝この経路で最後に置いた語。BOS は PrevPos=-1。
    private readonly record struct Partial(long Cost, int RightId, int PrevPos, int PrevIndex, Candidate? Word);

    /// <summary>
    /// 入力列を最尤から順に <paramref name="n"/> 件の文候補（n-best）へ変換する。位置ごとに上位 K=n の
    /// 部分経路を保持する k-best ビタビ。連接コストは各部分経路の末尾語を見るため 1-best より精緻。
    /// 全体を覆う分割が無ければ空を返す。
    /// </summary>
    public IReadOnlyList<Hypothesis> ConvertNBest(string input, int n)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(n);
        var length = input.Length;
        if (length == 0)
        {
            return [];
        }

        var partials = new List<Partial>[length + 1];
        partials[0] = [new Partial(0, 0, -1, -1, null)]; // BOS

        for (var i = 0; i < length; i++)
        {
            var reachable = partials[i];
            if (reachable is null || reachable.Count == 0)
            {
                continue;
            }

            foreach (var segment in _trie.SegmentsFrom(input, i))
            {
                var j = i + segment.Length;
                if (j > length)
                {
                    continue;
                }

                var word = segment.Candidate;
                var emission = word.Cost + _segmentPenalty + ((long)_vowelSkipPenalty * segment.VowelSkips);
                var slot = partials[j] ??= [];
                for (var k = 0; k < reachable.Count; k++)
                {
                    var prev = reachable[k];
                    var connectionCost = _connection?.Cost(prev.RightId, word.LeftId) ?? 0;
                    AddPartial(slot, new Partial(prev.Cost + emission + connectionCost, word.RightId, i, k, word), n);
                }
            }
        }

        var final = partials[length];
        if (final is null)
        {
            return [];
        }

        var hypotheses = new List<Hypothesis>(final.Count);
        foreach (var end in final)
        {
            var segments = new List<Candidate>();
            var lengths = new List<int>();
            var current = end;
            var pos = length; // この部分経路の終端位置。各文節の入力長 = pos - PrevPos。
            while (current.PrevPos >= 0)
            {
                segments.Add(current.Word!);
                lengths.Add(pos - current.PrevPos);
                pos = current.PrevPos;
                current = partials[current.PrevPos][current.PrevIndex];
            }

            segments.Reverse();
            lengths.Reverse();
            hypotheses.Add(new Hypothesis(segments, end.Cost, lengths));
        }

        return hypotheses;
    }

    // 位置ごとの上位 K リストへコスト昇順で挿入（同コストは既存を優先＝1-best と整合）。
    private static void AddPartial(List<Partial> slot, Partial candidate, int k)
    {
        if (slot.Count >= k && candidate.Cost >= slot[^1].Cost)
        {
            return;
        }

        var pos = slot.Count;
        while (pos > 0 && slot[pos - 1].Cost > candidate.Cost)
        {
            pos--;
        }

        slot.Insert(pos, candidate);
        if (slot.Count > k)
        {
            slot.RemoveAt(slot.Count - 1);
        }
    }
}
