using ShortcutIme.Core;
using ShortcutIme.Evaluation;

namespace ShortcutIme.Evaluation.Tests;

public class OracleRerankerTests
{
    private static Hypothesis Hyp(long cost, string surface) =>
        new([new Candidate(surface, surface, 0)], cost);

    [Fact]
    public void Rerank_PromotesGoldToTop_WhenInNBest()
    {
        var gold = new Dictionary<string, string> { ["abc"] = "正解" };

        var reranked = new OracleReranker(gold).Rerank("abc", "", [Hyp(100, "誤り"), Hyp(200, "正解")]);

        Assert.Equal("正解", reranked[0].Surface);
        Assert.Equal(2, reranked.Count); // 候補は捨てず、先頭へ移すだけ
    }

    [Fact]
    public void Rerank_LeavesUnchanged_WhenGoldNotInNBest()
    {
        var gold = new Dictionary<string, string> { ["abc"] = "圏外" };
        IReadOnlyList<Hypothesis> input = [Hyp(100, "あ"), Hyp(200, "い")];

        var reranked = new OracleReranker(gold).Rerank("abc", "", input);

        Assert.Same(input, reranked); // 触らず同一参照を返す
    }

    [Fact]
    public void Rerank_LeavesUnchanged_WhenInputHasNoGold()
    {
        var gold = new Dictionary<string, string> { ["xyz"] = "正解" };
        IReadOnlyList<Hypothesis> input = [Hyp(100, "あ"), Hyp(200, "正解")];

        var reranked = new OracleReranker(gold).Rerank("abc", "", input);

        Assert.Same(input, reranked);
    }
}
