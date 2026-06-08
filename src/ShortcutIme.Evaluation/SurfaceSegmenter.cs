using System.Text;
using ShortcutIme.Core;

namespace ShortcutIme.Evaluation;

/// <summary>
/// 表層テキストを Mozc 辞書（単語生起コスト＋連接コスト）で最小コストビタビ分割し、辞書単位の surface 列を返す
/// オフライン専用のセグメンタ。語 LM の学習コーパスを reranker のトークン単位（<see cref="Candidate.Surface"/>）と
/// 揃えるために使う——これで rerank 時に word bigram が転移し OOV が構造的に最小化される。
/// 本番 <see cref="IReranker"/> 契約に持ち込まないよう Core ではなく Evaluation に置く（build_corpus.py の兄弟）。
/// </summary>
/// <remarks>
/// マッチは NFC 正規化キーで行うが、emit するトークンは辞書の <b>生の</b> surface（＝rerank 時の
/// <see cref="Candidate.Surface"/> と同一）にする。正規化形を emit するとスコア時のトークンと食い違うため。
/// 辞書で被覆できない部分文字列には1文字フォールバック辺を常設し、どの文も必ず全被覆できるようにする。
/// </remarks>
public sealed class SurfaceSegmenter
{
    // 辞書1候補。Reading は捨てる（LM は Surface のみ消費＝129万件のメモリ主因を削減）。Surface は生（emit 用）。
    private readonly record struct SegEntry(string Surface, int Cost, int LeftId, int RightId);

    // NFC 正規化済み surface → 候補配列（同表層異 ID の homograph は連接が ID 依存なので潰さず全展開）。
    private readonly Dictionary<string, SegEntry[]> _index;
    private readonly Dictionary<string, SegEntry[]>.AlternateLookup<ReadOnlySpan<char>> _lookup;
    private readonly ConnectionMatrix? _connection;
    private readonly int _maxLen;
    private readonly int _fallbackCost;
    private readonly int _segmentPenalty;

    /// <summary>分割結果。<see cref="Tokens"/> は空白区切りで LM 学習に渡せる。</summary>
    /// <param name="Tokens">辞書単位の surface 列（フォールバックは1文字）。</param>
    /// <param name="FallbackCount">フォールバック由来トークン数。</param>
    public readonly record struct Result(IReadOnlyList<string> Tokens, int FallbackCount);

    /// <param name="entries">Mozc 辞書エントリ。</param>
    /// <param name="connection">連接コスト行列（null なら連接 0）。reranking の分割と整合させるため含める。</param>
    /// <param name="fallbackCost">1文字フォールバック辺の固定コスト（辞書パスが必ず勝つよう高め・有限）。</param>
    /// <param name="segmentPenalty">1セグメントごとの加算ペナルティ。surface だけだと安い homograph で活用語尾が過分割
    /// される（例：しました→し/まし/た）ため、長い辞書単位（reading-lattice が作る reranker 単位）へ寄せる knob。</param>
    public SurfaceSegmenter(IEnumerable<DictionaryEntry> entries, ConnectionMatrix? connection = null, int fallbackCost = 30000, int segmentPenalty = 0)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _connection = connection;
        _fallbackCost = fallbackCost;
        _segmentPenalty = segmentPenalty;

        var grouped = new Dictionary<string, List<SegEntry>>(StringComparer.Ordinal);
        var seen = new HashSet<(string, int, int, int)>();
        var maxLen = 1; // フォールバックが常に長さ1の辺を張るので最低 1。
        foreach (var entry in entries)
        {
            var surface = entry.Surface;
            // 空白を含む surface は空白区切りトークン化を壊すので除外。
            if (ContainsWhitespace(surface))
            {
                continue;
            }

            // 完全重複（同 surface・同コスト・同 ID）はメモリ節約のため除外。homograph（ID 違い）は残す。
            if (!seen.Add((surface, entry.Cost, entry.LeftId, entry.RightId)))
            {
                continue;
            }

            var key = surface.Normalize(NormalizationForm.FormC);
            if (!grouped.TryGetValue(key, out var list))
            {
                list = [];
                grouped[key] = list;
            }

            list.Add(new SegEntry(surface, entry.Cost, entry.LeftId, entry.RightId));
            if (key.Length > maxLen)
            {
                maxLen = key.Length;
            }
        }

        _maxLen = maxLen;
        _index = new Dictionary<string, SegEntry[]>(grouped.Count, StringComparer.Ordinal);
        foreach (var (key, list) in grouped)
        {
            _index[key] = [.. list];
        }

        _lookup = _index.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>最大 surface 長（診断用）。</summary>
    public int MaxSurfaceLength => _maxLen;

    /// <summary>表層テキストを最小コストで辞書単位に分割する。空入力は空列。</summary>
    public Result Segment(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var s = text.Normalize(NormalizationForm.FormC);
        var n = s.Length;
        if (n == 0)
        {
            return new Result([], 0);
        }

        var cost = new long[n + 1];
        var back = new Step[n + 1];
        Array.Fill(cost, long.MaxValue);
        cost[0] = 0;

        for (var i = 0; i < n; i++)
        {
            if (cost[i] == long.MaxValue)
            {
                continue;
            }

            var prevRightId = i == 0 ? 0 : back[i].RightId; // 文頭は BOS(0)。
            var maxLen = Math.Min(_maxLen, n - i);

            // 辞書辺：位置 i から始まる各長の surface マッチを全 homograph 展開。
            for (var len = 1; len <= maxLen; len++)
            {
                if (!_lookup.TryGetValue(s.AsSpan(i, len), out var candidates))
                {
                    continue;
                }

                var j = i + len;
                foreach (var c in candidates)
                {
                    var connectionCost = _connection?.Cost(prevRightId, c.LeftId) ?? 0;
                    var next = cost[i] + c.Cost + connectionCost + _segmentPenalty;
                    if (next < cost[j])
                    {
                        cost[j] = next;
                        back[j] = new Step(i, c.Surface, c.RightId, IsFallback: false);
                    }
                }
            }

            // フォールバック辺：1文字。連接コストは 0 強制（辞書パスがあれば必ず負ける）。常設で全被覆を保証。
            {
                var fallbackNext = cost[i] + _fallbackCost + _segmentPenalty;
                if (fallbackNext < cost[i + 1])
                {
                    cost[i + 1] = fallbackNext;
                    back[i + 1] = new Step(i, s[i].ToString(), RightId: 0, IsFallback: true);
                }
            }
        }

        var tokens = new List<string>();
        var fallback = 0;
        for (var k = n; k > 0;)
        {
            var step = back[k];
            tokens.Add(step.Token);
            if (step.IsFallback)
            {
                fallback++;
            }

            k = step.Prev;
        }

        tokens.Reverse();
        return new Result(tokens, fallback);
    }

    // ビタビのバックポインタ。Token＝emit する生 surface（またはフォールバック1文字）、RightId＝次の連接用。
    private readonly record struct Step(int Prev, string Token, int RightId, bool IsFallback);

    private static bool ContainsWhitespace(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                return true;
            }
        }

        return false;
    }
}
