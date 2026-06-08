using System.Runtime.InteropServices;
using System.Text;

namespace ShortcutIme.Core;

/// <summary>
/// 候補をフルローマ字で格納し、母音をオプションとして走査するトライ。
/// 子音は必須、母音（aiueo）は入力で省略可能 → "ky" でも "kyou" でも きょう系を引ける（簡拼スペクトラム）。
///
/// メモリ最適化：ポインタ木（per-node <c>Dictionary&lt;char,Node&gt;</c>）ではなく、CSR 風のフラット配列で保持する。
/// 子辺は <see cref="_childStart"/> でノードごとの区間に分け、ラベルは ASCII バイト配列。候補は dedup＋文字列 intern
/// 済みの <see cref="_candidates"/> を id 参照する。構築は一度きり・読み取り専用（可変な学習は別管理）。
/// </summary>
public sealed class RomajiTrie
{
    // CSR: ノード i の子辺は [_childStart[i], _childStart[i+1])。ラベルは _edgeLabel、遷移先は _childTarget。
    private readonly int[] _childStart;
    private readonly byte[] _edgeLabel;
    private readonly int[] _childTarget;

    // CSR: ノード i の候補 id は [_candStart[i], _candStart[i+1]) の _candId。実体は dedup 済み _candidates。
    private readonly int[] _candStart;
    private readonly int[] _candId;
    private readonly Candidate[] _candidates;

    private RomajiTrie(
        int[] childStart, byte[] edgeLabel, int[] childTarget,
        int[] candStart, int[] candId, Candidate[] candidates)
    {
        _childStart = childStart;
        _edgeLabel = edgeLabel;
        _childTarget = childTarget;
        _candStart = candStart;
        _candId = candId;
        _candidates = candidates;
    }

    /// <summary>トライの構造統計（メモリ設計の計測用）。</summary>
    public readonly record struct Stats(long Nodes, long NodesWithChildren, long ChildEdges, long CandidateLists, long Candidates);

    /// <summary>ノード数・子区間数・候補数などを数える（診断用）。</summary>
    public Stats ComputeStats()
    {
        long nodes = _childStart.Length - 1;
        long withChildren = 0;
        long candidateLists = 0;
        for (var i = 0; i < nodes; i++)
        {
            if (_childStart[i] < _childStart[i + 1])
            {
                withChildren++;
            }

            if (_candStart[i] < _candStart[i + 1])
            {
                candidateLists++;
            }
        }

        return new Stats(nodes, withChildren, _edgeLabel.Length, candidateLists, _candId.Length);
    }

    private const uint Magic = 0x54524953; // "SIRT"
    private const int FormatVersion = 1;

    /// <summary>構築済みトライをバイナリ直列化する（オフライン事前処理→起動時の高速ロード用）。</summary>
    public void Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.Create(path);
        Save(stream);
    }

    /// <summary>構築済みトライをストリームへ直列化する。</summary>
    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(FormatVersion);
        WriteInts(writer, _childStart);
        WriteByteArray(writer, _edgeLabel);
        WriteInts(writer, _childTarget);
        WriteInts(writer, _candStart);
        WriteInts(writer, _candId);

        // 候補は表層/読みをプール化（intern を直列化でも維持）して列で書く。
        var surfaces = new List<string>();
        var surfaceIndex = new Dictionary<string, int>();
        var readings = new List<string>();
        var readingIndex = new Dictionary<string, int>();
        var refs = new (int Surface, int Reading)[_candidates.Length];
        for (var i = 0; i < _candidates.Length; i++)
        {
            refs[i] = (Pool(surfaces, surfaceIndex, _candidates[i].Surface), Pool(readings, readingIndex, _candidates[i].Reading));
        }

        WriteStringPool(writer, surfaces);
        WriteStringPool(writer, readings);
        writer.Write(_candidates.Length);
        for (var i = 0; i < _candidates.Length; i++)
        {
            var candidate = _candidates[i];
            writer.Write(refs[i].Surface);
            writer.Write(refs[i].Reading);
            writer.Write(candidate.Cost);
            writer.Write(candidate.LeftId);
            writer.Write(candidate.RightId);
        }
    }

    /// <summary>直列化済みトライを読み込む（テキスト辞書の再パース・再構築なし）。</summary>
    public static RomajiTrie Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    /// <summary>直列化済みトライをストリームから読み込む。</summary>
    public static RomajiTrie Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt32() != Magic)
        {
            throw new InvalidDataException("RomajiTrie: マジックが不正です。");
        }

        var version = reader.ReadInt32();
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"RomajiTrie: 非対応のフォーマットバージョン {version}。");
        }

        var childStart = ReadInts(reader);
        var edgeLabel = ReadByteArray(reader);
        var childTarget = ReadInts(reader);
        var candStart = ReadInts(reader);
        var candId = ReadInts(reader);
        var surfaces = ReadStringPool(reader);
        var readings = ReadStringPool(reader);
        var count = reader.ReadInt32();
        var candidates = new Candidate[count];
        for (var i = 0; i < count; i++)
        {
            var surface = surfaces[reader.ReadInt32()];
            var reading = readings[reader.ReadInt32()];
            var cost = reader.ReadInt32();
            var leftId = reader.ReadInt32();
            var rightId = reader.ReadInt32();
            candidates[i] = new Candidate(surface, reading, cost, leftId, rightId);
        }

        return new RomajiTrie(childStart, edgeLabel, childTarget, candStart, candId, candidates);
    }

    private static int Pool(List<string> pool, Dictionary<string, int> index, string value)
    {
        if (!index.TryGetValue(value, out var id))
        {
            id = pool.Count;
            pool.Add(value);
            index[value] = id;
        }

        return id;
    }

    private static void WriteStringPool(BinaryWriter writer, List<string> pool)
    {
        writer.Write(pool.Count);
        foreach (var value in pool)
        {
            writer.Write(value);
        }
    }

    private static string[] ReadStringPool(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var pool = new string[count];
        for (var i = 0; i < count; i++)
        {
            pool[i] = reader.ReadString();
        }

        return pool;
    }

    private static void WriteInts(BinaryWriter writer, int[] values)
    {
        writer.Write(values.Length);
        writer.Write(MemoryMarshal.AsBytes(values.AsSpan()));
    }

    private static int[] ReadInts(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        var values = new int[length];
        reader.BaseStream.ReadExactly(MemoryMarshal.AsBytes(values.AsSpan()));
        return values;
    }

    private static void WriteByteArray(BinaryWriter writer, byte[] values)
    {
        writer.Write(values.Length);
        writer.Write(values);
    }

    private static byte[] ReadByteArray(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        var values = new byte[length];
        reader.BaseStream.ReadExactly(values);
        return values;
    }

    /// <summary>
    /// 辞書エントリをローマ字化してトライを構築する。複数のエンコーダ（＝入力方式）を渡すと、
    /// 各エントリを全方式のローマ字で索引する。混在方式を網羅するには
    /// <see cref="RomajiVariants.ExpandReading"/> を使う下のオーバーロードを推奨。
    /// </summary>
    public static RomajiTrie Build(IEnumerable<DictionaryEntry> entries, params IReadingEncoder[] romajiEncoders)
    {
        ArgumentNullException.ThrowIfNull(romajiEncoders);
        if (romajiEncoders.Length == 0)
        {
            romajiEncoders = [new RomajiEncoder()];
        }

        return Build(entries, reading => romajiEncoders.Select(encoder => encoder.Encode(reading)).ToList());
    }

    /// <summary>
    /// 読み→ローマ字異形列の関数でトライを構築する。<see cref="RomajiVariants.ExpandReading"/> を渡すと
    /// モーラ単位の異形の直積を全登録し、訓令式/ヘボン式の混在打鍵でも引ける。
    /// 同一(ローマ字, 表層)は最小コストで集約し、候補は dedup＋文字列 intern してから格納する。
    /// </summary>
    public static RomajiTrie Build(IEnumerable<DictionaryEntry> entries, Func<string, IReadOnlyList<string>> romanizeVariants)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(romanizeVariants);

        var best = new Dictionary<(string Romaji, string Surface), Candidate>();
        foreach (var entry in entries)
        {
            foreach (var romaji in romanizeVariants(entry.Reading))
            {
                if (romaji.Length == 0)
                {
                    continue;
                }

                var identity = (romaji, entry.Surface);
                if (!best.TryGetValue(identity, out var existing) || entry.Cost < existing.Cost)
                {
                    best[identity] = new Candidate(entry.Surface, entry.Reading, entry.Cost, entry.LeftId, entry.RightId);
                }
            }
        }

        var builder = new Builder();
        foreach (var (identity, candidate) in best)
        {
            builder.Insert(identity.Romaji, candidate);
        }

        return builder.ToCompact();
    }

    /// <summary>入力（子音必須・母音オプション）にマッチする候補をコスト昇順で返す。</summary>
    public IReadOnlyList<Candidate> Search(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var reached = new HashSet<int>();
        Descend(0, input, 0, reached);

        var collected = new List<Candidate>();
        var visited = new HashSet<int>();
        foreach (var node in reached)
        {
            Collect(node, collected, visited);
        }

        collected.Sort(static (a, b) => a.Cost.CompareTo(b.Cost));
        return collected;
    }

    private void Descend(int node, string input, int index, HashSet<int> reached)
    {
        if (index == input.Length)
        {
            reached.Add(node);
            return;
        }

        for (var e = _childStart[node]; e < _childStart[node + 1]; e++)
        {
            var ch = (char)_edgeLabel[e];
            if (ch == input[index])
            {
                Descend(_childTarget[e], input, index + 1, reached);
            }
            else if (IsVowel(ch))
            {
                Descend(_childTarget[e], input, index, reached);
            }
        }
    }

    private void Collect(int node, List<Candidate> accumulator, HashSet<int> visited)
    {
        if (!visited.Add(node))
        {
            return;
        }

        for (var k = _candStart[node]; k < _candStart[node + 1]; k++)
        {
            accumulator.Add(_candidates[_candId[k]]);
        }

        for (var e = _childStart[node]; e < _childStart[node + 1]; e++)
        {
            Collect(_childTarget[e], accumulator, visited);
        }
    }

    /// <summary>
    /// 入力の位置 <paramref name="start"/> から始まり母音オプションでマッチする全単語を、
    /// 消費した入力長とともに列挙する（連文節変換のラティス構築用）。
    /// </summary>
    public IReadOnlyList<SegmentMatch> SegmentsFrom(string input, int start)
    {
        ArgumentNullException.ThrowIfNull(input);
        var matches = new List<SegmentMatch>();
        WalkSegments(0, input, start, start, 0, matches);
        return matches;
    }

    private void WalkSegments(int node, string input, int start, int pos, int skips, List<SegmentMatch> acc)
    {
        var length = pos - start;
        if (length > 0)
        {
            for (var k = _candStart[node]; k < _candStart[node + 1]; k++)
            {
                acc.Add(new SegmentMatch(_candidates[_candId[k]], length, skips));
            }
        }

        for (var e = _childStart[node]; e < _childStart[node + 1]; e++)
        {
            var ch = (char)_edgeLabel[e];
            if (pos < input.Length && ch == input[pos])
            {
                WalkSegments(_childTarget[e], input, start, pos + 1, skips, acc); // 入力を1文字消費
            }
            else if (IsVowel(ch))
            {
                WalkSegments(_childTarget[e], input, start, pos, skips + 1, acc); // 母音は入力に無くスキップ（+1）
            }
        }
    }

    private static bool IsVowel(char c) => c is 'a' or 'i' or 'u' or 'e' or 'o';

    /// <summary>
    /// 構築時のみ使うポインタ木ビルダ。候補は record 値等価で dedup、文字列は intern する。
    /// 構築後に <see cref="ToCompact"/> で CSR フラット配列へ変換する。
    /// </summary>
    private sealed class Builder
    {
        private sealed class Node
        {
            public SortedDictionary<char, Node>? Children { get; set; }
            public List<int>? CandidateIds { get; set; }
        }

        private readonly Node _root = new();
        private readonly List<Candidate> _candidates = [];
        private readonly Dictionary<Candidate, int> _candidateIds = [];
        private readonly Dictionary<string, string> _stringPool = [];

        public void Insert(string romaji, Candidate raw)
        {
            var id = Intern(raw);
            var node = _root;
            foreach (var c in romaji)
            {
                node.Children ??= [];
                if (!node.Children.TryGetValue(c, out var child))
                {
                    child = new Node();
                    node.Children[c] = child;
                }

                node = child;
            }

            (node.CandidateIds ??= []).Add(id);
        }

        private int Intern(Candidate raw)
        {
            var candidate = new Candidate(Pool(raw.Surface), Pool(raw.Reading), raw.Cost, raw.LeftId, raw.RightId);
            if (!_candidateIds.TryGetValue(candidate, out var id))
            {
                id = _candidates.Count;
                _candidates.Add(candidate);
                _candidateIds[candidate] = id;
            }

            return id;
        }

        private string Pool(string value) => _stringPool.TryGetValue(value, out var pooled) ? pooled : _stringPool[value] = value;

        public RomajiTrie ToCompact()
        {
            // BFS でノードに連番を振る（親→子の順、子は親より大きい index）。
            var order = new List<Node> { _root };
            var index = new Dictionary<Node, int>(ReferenceEqualityComparer.Instance) { [_root] = 0 };
            var queue = new Queue<Node>();
            queue.Enqueue(_root);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (node.Children is null)
                {
                    continue;
                }

                foreach (var child in node.Children.Values)
                {
                    index[child] = order.Count;
                    order.Add(child);
                    queue.Enqueue(child);
                }
            }

            var nodeCount = order.Count;
            var edgeCount = 0;
            var candidateSlots = 0;
            foreach (var node in order)
            {
                edgeCount += node.Children?.Count ?? 0;
                candidateSlots += node.CandidateIds?.Count ?? 0;
            }

            var childStart = new int[nodeCount + 1];
            var edgeLabel = new byte[edgeCount];
            var childTarget = new int[edgeCount];
            var candStart = new int[nodeCount + 1];
            var candId = new int[candidateSlots];

            var edgeCursor = 0;
            var candCursor = 0;
            for (var i = 0; i < nodeCount; i++)
            {
                var node = order[i];
                childStart[i] = edgeCursor;
                if (node.Children is not null)
                {
                    foreach (var (label, child) in node.Children) // SortedDictionary → ラベル昇順で安定
                    {
                        edgeLabel[edgeCursor] = (byte)label;
                        childTarget[edgeCursor] = index[child];
                        edgeCursor++;
                    }
                }

                candStart[i] = candCursor;
                if (node.CandidateIds is not null)
                {
                    foreach (var id in node.CandidateIds)
                    {
                        candId[candCursor++] = id;
                    }
                }
            }

            childStart[nodeCount] = edgeCursor;
            candStart[nodeCount] = candCursor;

            return new RomajiTrie(childStart, edgeLabel, childTarget, candStart, candId, [.. _candidates]);
        }
    }
}
