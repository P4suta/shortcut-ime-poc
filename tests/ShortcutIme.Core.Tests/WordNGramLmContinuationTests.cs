namespace ShortcutIme.Core.Tests;

public class WordNGramLmContinuationTests
{
    private static WordNGramLm BuildWord(string corpus)
        => WordNGramLm.Build(new StringReader(corpus), TokenMode.Word, lambdaBi: 0.7, floorNegLogProb: 10.0);

    private static Candidate Word(string surface) => new(surface, "", 0);

    [Fact]
    public void Continuation_UsesLeftContext()
    {
        // a→b は観測 bigram、x→b は未観測。左文脈が実際に prev として効くなら observed < unobserved。
        var lm = BuildWord("a b c\na b c\na b\nx y\n");
        Candidate[] next = [Word("b")];

        var observed = lm.NegLogProbContinuation(next, [Word("a")]);
        var unobserved = lm.NegLogProbContinuation(next, [Word("x")]);

        Assert.True(observed < unobserved, $"左文脈 a→b（観測）{observed} は x→b（未観測）{unobserved} より小さいはず");
    }

    [Fact]
    public void Continuation_DoesNotPenalizeWithEos()
    {
        // continuation は EOS を踏まない＝全文 NegLogProb（BOS→b→EOS）より EOS step ぶん小さい。
        var lm = BuildWord("a b c\na b c\na b\n");
        Candidate[] next = [Word("b")];

        var continuation = lm.NegLogProbContinuation(next, []);
        var full = lm.NegLogProb(next, "");

        Assert.True(continuation < full, $"continuation {continuation} は full(BOS→b→EOS) {full} より小さいはず（EOS 非加算）");
    }

    [Fact]
    public void Continuation_UnknownLeft_ResetsContext()
    {
        // 未知語 left は prev をリセット（文脈なし unigram）。既知 a→b と未知 left→b は異なるスコアになりうる。
        var lm = BuildWord("a b c\na b c\na b\n");
        Candidate[] next = [Word("b")];

        var afterKnown = lm.NegLogProbContinuation(next, [Word("a")]);
        var afterUnknown = lm.NegLogProbContinuation(next, [Word("zzz")]); // 語彙外

        Assert.True(afterKnown <= afterUnknown, $"観測文脈 {afterKnown} は未知文脈 {afterUnknown} 以下のはず");
    }
}
