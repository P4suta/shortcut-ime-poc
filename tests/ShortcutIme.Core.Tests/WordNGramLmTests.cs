namespace ShortcutIme.Core.Tests;

public class WordNGramLmTests
{
    private static readonly string[] s_corpus =
    [
        "私 は 学生 です",
        "私 は 教師 です",
        "彼 は 学生 です",
    ];

    private static IReadOnlyList<Candidate> Words(params string[] surfaces) =>
        surfaces.Select(surface => new Candidate(surface, "", 0)).ToList();

    [Fact]
    public void NegLogProb_SingleWordCorpus_MatchesHandComputed()
    {
        // コーパス ["A"]：BOS A EOS。unigram A=1,EOS=1（uniTotal=2）。bigram (BOS,A)=1,(A,EOS)=1。
        // λ_bi=0.5 で各 interp = 0.5*1 + 0.5*(1/2) = 0.75。NegLogProb([A]) = -2*ln(0.75)。
        var lm = WordNGramLm.Build(["A"], TokenMode.Word, lambdaBi: 0.5, floorNegLogProb: 10.0);
        var expected = -2.0 * Math.Log(0.75);
        Assert.Equal(expected, lm.NegLogProb(Words("A"), ""), 4);
    }

    [Fact]
    public void Backoff_UnseenBigram_UsesScaledUnigram_NotFloor()
    {
        // (私,です) はコーパスに無いが私・です は既知 → tier2（floor ではなく (1-λ_bi)*unigram）で評価される。
        var lm = WordNGramLm.Build(s_corpus, TokenMode.Word, lambdaBi: 0.9, floorNegLogProb: 100.0);
        var unseenKnown = lm.NegLogProb(Words("私", "です"), ""); // (私,です) は未観測だが両語既知
        var withOov = lm.NegLogProb(Words("私", "未知語"), "");    // (私,未知語) は floor
        Assert.True(unseenKnown < withOov, $"tier2={unseenKnown} は floor 列={withOov} より小さいはず");
        Assert.True(unseenKnown < 100.0, $"tier2 を含む文 {unseenKnown} は floor(100) を踏むべきでない");
    }

    [Fact]
    public void Oov_StepUsesFloorExactly()
    {
        // floor を 50→80 に増やすと、OOV 語を含む文スコアは丁度 30 増える（OOV step = floor）。
        var lm1 = WordNGramLm.Build(s_corpus, TokenMode.Word, lambdaBi: 0.9, floorNegLogProb: 50.0);
        var lm2 = WordNGramLm.Build(s_corpus, TokenMode.Word, lambdaBi: 0.9, floorNegLogProb: 80.0);
        var a = lm1.NegLogProb(Words("未知"), "");
        var b = lm2.NegLogProb(Words("未知"), "");
        Assert.Equal(30.0, b - a, 4);
    }

    [Fact]
    public void LongerSentence_HasHigherNegLogProb()
    {
        var lm = WordNGramLm.Build(s_corpus, TokenMode.Word, lambdaBi: 0.9, floorNegLogProb: 20.0);
        var shortScore = lm.NegLogProb(Words("私", "は", "学生", "です"), "");
        var longScore = lm.NegLogProb(Words("私", "は", "学生", "です", "私", "は", "学生", "です"), "");
        Assert.True(longScore > shortScore, "EOS 項を含むチェインは長文ほど neglogprob が増える");
    }

    [Theory]
    [InlineData(TokenMode.Word)]
    [InlineData(TokenMode.Char)]
    public void SaveLoad_RoundTripsNegLogProb(TokenMode mode)
    {
        string[] corpus = mode == TokenMode.Word
            ? s_corpus
            : ["私は学生です", "私は教師です", "彼は学生です"];
        var lm = WordNGramLm.Build(corpus, mode, lambdaBi: 0.85, floorNegLogProb: 25.0);

        using var stream = new MemoryStream();
        lm.Save(stream);
        stream.Position = 0;
        var loaded = WordNGramLm.Load(stream);

        Assert.Equal(lm.Mode, loaded.Mode);
        Assert.Equal(lm.VocabSize, loaded.VocabSize);
        foreach (var probe in new[] { Words("私", "は", "学生", "です"), Words("彼", "です"), Words("未知") })
        {
            Assert.Equal(lm.NegLogProb(probe, ""), loaded.NegLogProb(probe, ""), 5);
        }
    }

    [Fact]
    public void Load_BadMagic_Throws()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0xDEADBEEFu);
            writer.Write(1);
        }

        stream.Position = 0;
        Assert.Throws<InvalidDataException>(() => WordNGramLm.Load(stream));
    }

    [Fact]
    public void Load_BadVersion_Throws()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x53494C4Du); // "SILM"
            writer.Write(999);
        }

        stream.Position = 0;
        Assert.Throws<InvalidDataException>(() => WordNGramLm.Load(stream));
    }

    [Fact]
    public void CharMode_NoFloorOnLearnedChars()
    {
        var lm = WordNGramLm.Build(["私は学生です"], TokenMode.Char, lambdaBi: 0.9, floorNegLogProb: 100.0);
        var probe = Words("私は", "学生", "です"); // 連結すると学習文字列そのもの
        Assert.True(lm.NegLogProb(probe, "") < 100.0, "学習済み文字のみの候補は floor を踏むべきでない");
        Assert.True(lm.HitRate(probe) > 0.5);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.0)]
    public void Build_InvalidLambdaBi_Throws(double lambdaBi)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WordNGramLm.Build(["A"], TokenMode.Word, lambdaBi, 10.0));
    }

    [Fact]
    public void Build_NonPositiveFloor_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WordNGramLm.Build(["A"], TokenMode.Word, 0.5, 0.0));
    }
}
