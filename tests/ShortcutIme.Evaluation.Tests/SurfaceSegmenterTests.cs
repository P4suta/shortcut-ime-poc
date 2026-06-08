using ShortcutIme.Core;
using ShortcutIme.Evaluation;

namespace ShortcutIme.Evaluation.Tests;

public class SurfaceSegmenterTests
{
    private static DictionaryEntry E(string surface, int cost, int leftId = 0, int rightId = 0) =>
        new(surface, surface, cost, leftId, rightId);

    [Fact]
    public void Segment_Empty_ReturnsEmpty()
    {
        var seg = new SurfaceSegmenter([E("東", 100)]);
        var result = seg.Segment("");
        Assert.Empty(result.Tokens);
        Assert.Equal(0, result.FallbackCount);
    }

    [Fact]
    public void Segment_PrefersMinCostTiling()
    {
        // 単一「東京」(150) vs 分割「東」+「京」(200)。最小コストの単一単位が勝つ。
        var seg = new SurfaceSegmenter([E("東京", 150), E("東", 100), E("京", 100)]);
        var result = seg.Segment("東京");
        Assert.Equal(["東京"], result.Tokens);
        Assert.Equal(0, result.FallbackCount);
    }

    [Fact]
    public void Segment_SegmentPenalty_MergesIntoLongerUnit()
    {
        // penalty=0 なら分割「東」+「京」(200) が単一「東京」(250) に勝つ。
        var entries = new[] { E("東京", 250), E("東", 100), E("京", 100) };
        Assert.Equal(["東", "京"], new SurfaceSegmenter(entries).Segment("東京").Tokens);

        // penalty=100 で分割は 2×penalty を払う（400）→ 単一（350）が勝つ。活用語尾の過分割矯正と同型。
        Assert.Equal(["東京"], new SurfaceSegmenter(entries, segmentPenalty: 100).Segment("東京").Tokens);
    }

    [Fact]
    public void Segment_UncoveredChar_FallsBackToSingleChar()
    {
        // 「東」は辞書、「X」は非被覆 → 1文字フォールバックで全被覆を保ち、フォールバック数を報告する。
        var seg = new SurfaceSegmenter([E("東", 100)]);
        var result = seg.Segment("東X");
        Assert.Equal(["東", "X"], result.Tokens);
        Assert.Equal(1, result.FallbackCount);
    }

    [Fact]
    public void Segment_AllUncovered_AllFallback()
    {
        var seg = new SurfaceSegmenter([E("無", 100)]);
        var result = seg.Segment("abc");
        Assert.Equal(["a", "b", "c"], result.Tokens);
        Assert.Equal(3, result.FallbackCount);
    }

    [Fact]
    public void Segment_EmitsRawSurface_NotNormalizedKey()
    {
        // emit トークンは生 surface（＝rerank 時の Candidate.Surface と同一）。
        var seg = new SurfaceSegmenter([E("Ａ", 100)]); // 全角Ａ
        var result = seg.Segment("Ａ");
        Assert.Equal(["Ａ"], result.Tokens);
    }

    [Fact]
    public void MaxSurfaceLength_ReflectsLongestEntry()
    {
        var seg = new SurfaceSegmenter([E("東", 100), E("東京都", 200)]);
        Assert.Equal(3, seg.MaxSurfaceLength);
    }

    [Fact]
    public void Constructor_NullEntries_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SurfaceSegmenter(null!));
    }
}
