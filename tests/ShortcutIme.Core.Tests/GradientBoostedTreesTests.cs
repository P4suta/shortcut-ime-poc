namespace ShortcutIme.Core.Tests;

public class GradientBoostedTreesTests
{
    // LightGBM text モデルを模した最小例：2木、各2葉、特徴0/1で分割。
    private const string Model = """
        tree
        version=v4
        num_class=1
        num_tree_per_iteration=1
        max_feature_idx=1
        objective=lambdarank

        Tree=0
        num_leaves=2
        split_feature=0
        threshold=1.5
        decision_type=2
        left_child=-1
        right_child=-2
        leaf_value=10 20

        Tree=1
        num_leaves=2
        split_feature=1
        threshold=0.5
        decision_type=2
        left_child=-1
        right_child=-2
        leaf_value=1 2

        end of trees
        """;

    [Theory]
    [InlineData(0.0, 0.0, 11.0)] // 木0: 0<=1.5→葉0=10、木1: 0<=0.5→葉0=1 → 11
    [InlineData(2.0, 0.0, 21.0)] // 木0: 2>1.5→葉1=20、木1: 0<=0.5→葉0=1 → 21
    [InlineData(0.0, 1.0, 12.0)] // 木0: 10、木1: 1>0.5→葉1=2 → 12
    [InlineData(2.0, 1.0, 22.0)] // 20 + 2 → 22
    public void Evaluate_SumsLeafValuesAcrossTrees(double f0, double f1, double expected)
    {
        var gbt = GradientBoostedTrees.Load(new StringReader(Model));
        Assert.Equal(2, gbt.FeatureCount);
        Assert.Equal(expected, gbt.Evaluate([f0, f1]), 6);
    }

    [Fact]
    public void Load_ThrowsWhenNoTrees()
        => Assert.Throws<InvalidDataException>(() => GradientBoostedTrees.Load(new StringReader("version=v4\nend of trees\n")));
}
