using ShortcutIme.Core;

namespace ShortcutIme.Evaluation.Tests;

public class MixedVowelEncoderTests
{
    // 設計の背骨を支える代表的な読み（音節母音・長音・促音・拗音・撥音・語境界を含む）。
    private static readonly string[] Readings =
    [
        "きょう", "きょういく", "きょうゆう", "あい", "いえ", "えき", "しゅうまつ",
        "せんせい", "をおくる", "おはよう", "がっこう", "ゆっくり", "かった", "こーひー",
        "ほん", "しゃちょう", "ありがとうございました", "しごと", "つづき", "ふじ",
    ];

    private static readonly RomajiScheme[] Schemes = [RomajiScheme.Kunrei, RomajiScheme.Hepburn];

    [Fact]
    public void KeepRateZero_EqualsConsonantEncoder()
    {
        foreach (var scheme in Schemes)
        {
            var consonant = new ConsonantEncoder(scheme);
            foreach (var reading in Readings)
            {
                foreach (var seed in new[] { 0, 1, 42, 12345 })
                {
                    // 短絡（keepRate≤0）でハッシュを消費しないため seed に依らず子音入力に厳密一致。
                    Assert.Equal(consonant.Encode(reading), new MixedVowelEncoder(scheme, 0.0, seed).Encode(reading));
                }
            }
        }
    }

    [Fact]
    public void KeepRateOne_EqualsRomajiEncoder()
    {
        foreach (var scheme in Schemes)
        {
            var full = new RomajiEncoder(scheme);
            foreach (var reading in Readings)
            {
                foreach (var seed in new[] { 0, 1, 42, 12345 })
                {
                    Assert.Equal(full.Encode(reading), new MixedVowelEncoder(scheme, 1.0, seed).Encode(reading));
                }
            }
        }
    }

    [Fact]
    public void SameSeed_IsDeterministic()
    {
        foreach (var reading in Readings)
        {
            var a = new MixedVowelEncoder(RomajiScheme.Kunrei, 0.5, seed: 7).Encode(reading);
            var b = new MixedVowelEncoder(RomajiScheme.Kunrei, 0.5, seed: 7).Encode(reading);
            Assert.Equal(a, b);
        }
    }

    [Fact]
    public void HigherKeepRate_IsSupersetSubsequence()
    {
        // Uniform 値は固定なので保持判定は keepRate に単調。p≤p' なら mixed(p) は mixed(p') の部分列。
        double[] rates = [0.0, 0.25, 0.5, 0.75, 1.0];
        foreach (var scheme in Schemes)
        {
            foreach (var reading in Readings)
            {
                foreach (var seed in new[] { 0, 3, 99 })
                {
                    for (var i = 0; i + 1 < rates.Length; i++)
                    {
                        var lo = new MixedVowelEncoder(scheme, rates[i], seed).Encode(reading);
                        var hi = new MixedVowelEncoder(scheme, rates[i + 1], seed).Encode(reading);
                        Assert.True(IsSubsequence(lo, hi),
                            $"\"{lo}\"(p={rates[i]}) は \"{hi}\"(p={rates[i + 1]}) の部分列でない（読み={reading}, seed={seed}）");
                    }
                }
            }
        }
    }

    [Fact]
    public void EveryMixedInput_IsFaithful()
    {
        // どの保持率・seed の混在入力も、必ず異形のいずれかの母音削除形＝照合可能（出題ミスなし）。
        double[] rates = [0.0, 0.25, 0.5, 0.75, 1.0];
        foreach (var scheme in Schemes)
        {
            foreach (var reading in Readings)
            {
                foreach (var seed in new[] { 0, 5, 777 })
                {
                    foreach (var rate in rates)
                    {
                        var input = new MixedVowelEncoder(scheme, rate, seed).Encode(reading);
                        Assert.True(InputFaithfulnessAuditor.IsFaithful(input, reading),
                            $"混在入力 \"{input}\"（読み={reading}, p={rate}, seed={seed}）が照合不能");
                    }
                }
            }
        }
    }

    [Fact]
    public void EveryMixedInput_ReachesGoldInTrie()
    {
        // operational 監査：実トライで gold へ到達できる（真の真実源）。
        DictionaryEntry[] entries =
        [
            new("きょう", "今日", 1000, 1, 1),
            new("きょういく", "教育", 1000, 1, 1),
            new("がっこう", "学校", 1000, 1, 1),
            new("かった", "買った", 1000, 1, 1),
            new("せんせい", "先生", 1000, 1, 1),
            new("しゃちょう", "社長", 1000, 1, 1),
            new("しごと", "仕事", 1000, 1, 1),
        ];
        var trie = RomajiTrie.Build(entries, reading => RomajiVariants.ExpandReading(reading));
        double[] rates = [0.0, 0.5, 1.0];
        foreach (var scheme in Schemes)
        {
            foreach (var entry in entries)
            {
                foreach (var seed in new[] { 0, 11 })
                {
                    foreach (var rate in rates)
                    {
                        var input = new MixedVowelEncoder(scheme, rate, seed).Encode(entry.Reading);
                        Assert.True(InputFaithfulnessAuditor.IsReachable(trie, input, entry.Surface),
                            $"\"{input}\"（読み={entry.Reading}, p={rate}, seed={seed}）から {entry.Surface} へ到達不能");
                    }
                }
            }
        }
    }

    private static bool IsSubsequence(string sub, string full)
    {
        var i = 0;
        foreach (var c in full)
        {
            if (i < sub.Length && sub[i] == c)
            {
                i++;
            }
        }

        return i == sub.Length;
    }
}
