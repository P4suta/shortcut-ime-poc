namespace ShortcutIme.Core.Tests;

public class LmRerankerTests
{
    private static readonly string[] s_corpus =
    [
        "私 は 学生 です",
        "私 は 学生 です",
        "彼 は 教師 です",
    ];

    private static Hypothesis Hyp(long cost, params string[] surfaces) =>
        new(surfaces.Select(surface => new Candidate(surface, "", 0)).ToList(), cost);

    private static WordNGramLm Lm() =>
        WordNGramLm.Build(s_corpus, TokenMode.Word, lambdaBi: 0.9, floorNegLogProb: 50.0);

    [Fact]
    public void Rerank_PromotesFluentSentence_WhenLambdaLarge()
    {
        // 同コストの2候補。コーパス頻出の「私 は 学生 です」を不自然な「彼 は 学生 です」より上へ。
        var good = Hyp(1000, "私", "は", "学生", "です");
        var bad = Hyp(1000, "彼", "は", "学生", "です");
        var reranker = new LmReranker(Lm(), lambda: 500.0);
        var result = reranker.Rerank("", "", [bad, good]);
        Assert.Equal(good, result[0]);
    }

    [Fact]
    public void Rerank_LambdaZero_PreservesCostOrder()
    {
        var a = Hyp(1000, "彼", "は", "教師", "です");
        var b = Hyp(1001, "私", "は", "学生", "です");
        var reranker = new LmReranker(Lm(), lambda: 0.0);
        var result = reranker.Rerank("", "", [a, b]);
        Assert.Equal(a, result[0]); // λ=0 はコスト順（入力順）を保つ＝identity 等価
        Assert.Equal(b, result[1]);
    }

    [Fact]
    public void Rerank_NullHypotheses_Throws()
    {
        var reranker = new LmReranker(Lm(), lambda: 1.0);
        Assert.Throws<ArgumentNullException>(() => reranker.Rerank("", "", null!));
    }

    [Fact]
    public void Rerank_PreservesMultiset()
    {
        var hyps = new[]
        {
            Hyp(1000, "彼", "は", "教師", "です"),
            Hyp(1001, "私", "は", "学生", "です"),
            Hyp(1002, "未知"),
        };
        var reranker = new LmReranker(Lm(), lambda: 300.0);
        var result = reranker.Rerank("", "", hyps);
        Assert.Equal(hyps.Length, result.Count);
        Assert.Equal(
            hyps.Select(h => h.Surface).OrderBy(s => s, StringComparer.Ordinal),
            result.Select(h => h.Surface).OrderBy(s => s, StringComparer.Ordinal));
    }

    [Fact]
    public void Constructor_NullLm_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new LmReranker(null!, 1.0));
    }

    [Fact]
    public void Interp_SumsWeightedNegLogProb()
    {
        // 2つの同一 LM を λ=250 ずつで補間 ⇒ 単一 LM λ=500 と同じスコア順になる（純加算なので）。
        var lm = Lm();
        var good = Hyp(1000, "私", "は", "学生", "です");
        var bad = Hyp(1000, "彼", "は", "学生", "です");
        var interp = new LmReranker([(lm, 250.0), (lm, 250.0)]);
        var single = new LmReranker(lm, 500.0);
        Assert.Equal(single.Rerank("", "", [bad, good]), interp.Rerank("", "", [bad, good]));
        Assert.Equal(good, interp.Rerank("", "", [bad, good])[0]);
    }

    [Fact]
    public void Interp_AllLambdaZero_PreservesCostOrder()
    {
        var lm = Lm();
        var a = Hyp(1000, "彼", "は", "教師", "です");
        var b = Hyp(1001, "私", "は", "学生", "です");
        var reranker = new LmReranker([(lm, 0.0), (lm, 0.0)]);
        var result = reranker.Rerank("", "", [a, b]);
        Assert.Equal(a, result[0]); // 全 λ=0 は identity 等価
        Assert.Equal(b, result[1]);
    }

    [Fact]
    public void Interp_EmptyComponents_Throws()
    {
        Assert.Throws<ArgumentException>(() => new LmReranker(Array.Empty<(WordNGramLm, double)>()));
    }
}
