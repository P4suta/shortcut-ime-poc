namespace ShortcutIme.Core.Tests;

public class RomajiVariantsTests
{
    [Fact]
    public void ForMora_ReturnsKunreiAndHepburnWhenTheyDiffer()
    {
        Assert.Equal(["si", "shi"], RomajiVariants.ForMora(new Mora("し")));
        Assert.Equal(["tu", "tsu"], RomajiVariants.ForMora(new Mora("つ")));
    }

    [Fact]
    public void ForMora_SingleVariantWhenSchemesAgree()
    {
        Assert.Equal(["ka"], RomajiVariants.ForMora(new Mora("か")));
    }

    [Fact]
    public void ExpandReading_ProducesConsistentSchemes()
    {
        var variants = RomajiVariants.ExpandReading("しごと"); // し={si,shi}

        Assert.Contains("sigoto", variants);
        Assert.Contains("shigoto", variants);
    }

    [Fact]
    public void ExpandReading_CoversMixedSchemes()
    {
        var variants = RomajiVariants.ExpandReading("しつ"); // し={si,shi}, つ={tu,tsu}

        Assert.Contains("situ", variants);    // 全訓令式
        Assert.Contains("shitsu", variants);  // 全ヘボン式
        Assert.Contains("shitu", variants);   // 混在
        Assert.Contains("sitsu", variants);   // 混在
    }

    [Fact]
    public void ExpandReading_DoublesSokuon()
    {
        Assert.Contains("gakkou", RomajiVariants.ExpandReading("がっこう"));
    }

    [Fact]
    public void ExpandReading_SingleResultWhenNoVariants()
    {
        Assert.Equal(["kyou"], RomajiVariants.ExpandReading("きょう"));
    }

    [Fact]
    public void ExpandReading_LongMark_DropAndElongation()
    {
        var variants = RomajiVariants.ExpandReading("こーひー"); // こ ー ひ ー

        Assert.Contains("kohi", variants);   // 両ー脱落（=RomajiEncoder 形）
        Assert.Contains("koohii", variants); // 両ー延長（直前母音の重ね）
    }

    [Fact]
    public void ExpandReadingWithHabits_AddsParticleWoHabit()
    {
        var variants = RomajiVariants.ExpandReadingWithHabits("を");

        Assert.Contains("wo", variants); // 標準
        Assert.Contains("o", variants);  // 癖（助詞を母音だけで打つ）
    }

    [Fact]
    public void ExpandReadingWithHabits_AddsNnHabit()
    {
        var variants = RomajiVariants.ExpandReadingWithHabits("ほん");

        Assert.Contains("hon", variants);  // 標準
        Assert.Contains("honn", variants); // 癖（撥音を二重 n）
    }

    [Fact]
    public void ExpandReadingWithHabits_IsSupersetOfExpandReading()
    {
        // 非劣化：癖を足しても scheme 異形は必ず全て含む。
        foreach (var reading in new[] { "しつ", "ほん", "を", "がっこう", "こーひー", "せんせい" })
        {
            var baseSet = RomajiVariants.ExpandReading(reading).ToHashSet();
            var withHabits = RomajiVariants.ExpandReadingWithHabits(reading).ToHashSet();
            Assert.True(baseSet.IsSubsetOf(withHabits), $"癖込みが scheme 異形を欠落（読み={reading}）");
        }
    }

    [Fact]
    public void ExpandReadingWithHabits_OverCap_FallsBackToAnchorsWithoutHabits()
    {
        // cap=1 で癖込み直積が超過 → ExpandReading（2アンカー）へ退避。アンカーは保持、癖は付かない。
        var variants = RomajiVariants.ExpandReadingWithHabits("しつ", cap: 1);

        Assert.Contains("situ", variants);    // 全訓令式アンカー
        Assert.Contains("shitsu", variants);  // 全ヘボン式アンカー
    }
}
