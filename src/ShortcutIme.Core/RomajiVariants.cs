namespace ShortcutIme.Core;

/// <summary>
/// モーラのローマ字「異形」（訓令式/ヘボン式など）を扱う単一の真実源。実ユーザは方式を
/// ごちゃまぜに打つ（し=shi だが つ=tu 等）。よってトライ索引は<b>モーラ単位の異形の直積</b>で
/// 全組み合わせを登録し、どんな混在打鍵でも引けるようにする。生成器も同じ表から作るため、
/// 「生成した入力は必ず照合可能」という不変条件が保たれる。
/// </summary>
public static class RomajiVariants
{
    private const string Vowels = "aiueo";

    /// <summary>1モーラのローマ字異形（重複なし、訓令式が先頭）。っ・ー・未知は空。</summary>
    public static IReadOnlyList<string> ForMora(Mora mora)
    {
        var kunrei = KanaRomanization.Romanize(mora, RomajiScheme.Kunrei);
        if (kunrei.Length == 0)
        {
            return [];
        }

        var hepburn = KanaRomanization.Romanize(mora, RomajiScheme.Hepburn);
        return kunrei == hepburn ? [kunrei] : [kunrei, hepburn];
    }

    /// <summary>
    /// 読み全体のフルローマ字異形を列挙する（モーラ異形の直積＋促音重ね＋長音「ー」の脱落/延長）。混在方式の網羅に使う。
    /// 異形の積が <paramref name="cap"/> 以下なら全直積、超える稀な語では「全訓令式＋全ヘボン式」の
    /// 2本に退避する（＝どちらか一貫方式の打鍵は必ず引ける）。打鍵癖は含めない（<see cref="ExpandReadingWithHabits"/>）。
    /// </summary>
    public static IReadOnlyList<string> ExpandReading(string reading, int cap = 64)
    {
        ArgumentNullException.ThrowIfNull(reading);

        var slots = BuildSlots(reading, includeHabits: false);
        if (slots.Count == 0)
        {
            return [];
        }

        long product = 1;
        foreach (var slot in slots)
        {
            product *= slot.Variants.Count;
            if (product > cap)
            {
                return TwoConsistent(slots);
            }
        }

        return Cartesian(slots);
    }

    /// <summary>
    /// <see cref="ExpandReading"/> に打鍵癖（を→o、ん→nn）を予算内で上乗せした異形列。
    /// 癖は scheme 異形の<b>後ろ</b>に足す augmentation 層で、<see cref="ForMora"/>/<see cref="TwoConsistent"/>
    /// のアンカー契約には触れない。癖込み直積が <paramref name="cap"/> を超える場合は <see cref="ExpandReading"/>
    /// （scheme 異形のみ＝2アンカー保証）へ退避するため、<b>癖を足してもアンカーは絶対に失わない（非劣化）</b>。
    /// </summary>
    public static IReadOnlyList<string> ExpandReadingWithHabits(string reading, int cap = 64)
    {
        ArgumentNullException.ThrowIfNull(reading);

        var slots = BuildSlots(reading, includeHabits: true);
        if (slots.Count == 0)
        {
            return [];
        }

        long product = 1;
        foreach (var slot in slots)
        {
            product *= slot.Variants.Count;
            if (product > cap)
            {
                return ExpandReading(reading, cap); // 非劣化退避（アンカー保持）。
            }
        }

        return Cartesian(slots).Distinct().ToList();
    }

    // 1モーラの「枠」。Variants は索引する全異形（癖・ー延長を含む）。Kunrei/Hepburn は退避時の
    // 一貫方式アンカー（＝RomajiEncoder の各方式形に一致させる識別子で、癖は含めず・ー は脱落 ""）。
    private readonly record struct Slot(IReadOnlyList<string> Variants, string Kunrei, string Hepburn);

    // 各モーラの枠を作る。っ・未知は枠を作らない。長音「ー」は文脈（直前母音）が要るため
    // [脱落形, 直前母音の延長形] の Variants にし、アンカーは脱落 ""（エンコーダと一致）。
    // includeHabits=true なら を→o・ん→nn を Variants の末尾に足す（アンカーは温存）。
    private static List<Slot> BuildSlots(string reading, bool includeHabits)
    {
        var slots = new List<Slot>();
        var sokuon = false;
        var prevVowel = '\0';
        foreach (var mora in MoraSplitter.Split(reading))
        {
            if (mora.Kana == "っ")
            {
                sokuon = true;
                continue;
            }

            if (mora.Kana == "ー")
            {
                if (Vowels.Contains(prevVowel))
                {
                    // 脱落形 / 直前母音の延長形（こーひー→kohi / koohii）。アンカーは脱落（""）。
                    slots.Add(new Slot(["", prevVowel.ToString()], "", ""));
                }

                sokuon = false;
                continue; // 直前母音は据え置き（延長が連続しても保つ）。
            }

            var baseVariants = ForMora(mora);
            if (baseVariants.Count == 0)
            {
                prevVowel = '\0';
                sokuon = false;
                continue; // 未知文字。
            }

            var kunrei = baseVariants[0];
            var hepburn = baseVariants[^1];
            var variants = baseVariants;
            if (sokuon)
            {
                variants = variants.Select(DoubleOnset).ToList();
                kunrei = DoubleOnset(kunrei);
                hepburn = DoubleOnset(hepburn);
                sokuon = false;
            }

            if (includeHabits)
            {
                var habit = HabitForms(mora);
                if (habit.Count > 0)
                {
                    variants = [.. variants, .. habit];
                }
            }

            slots.Add(new Slot(variants, kunrei, hepburn));
            // 次モーラの長音判定用：このモーラの母音（scheme 異形間で共通＝アンカー末尾。促音重ねでも末尾母音は不変）。
            prevVowel = Vowels.Contains(kunrei[^1]) ? kunrei[^1] : '\0';
        }

        return slots;
    }

    // 打鍵癖の異形（scheme 標準形に追加する形）。を→o（助詞を母音だけで打つ）、ん→nn（撥音を二重 n で打つ）。
    private static IReadOnlyList<string> HabitForms(Mora mora) => mora.Kana switch
    {
        "を" => ["o"],
        "ん" => ["nn"],
        _ => [],
    };

    private static string DoubleOnset(string variant) =>
        variant.Length > 0 && !Vowels.Contains(variant[0]) ? variant[0] + variant : variant;

    private static IReadOnlyList<string> Cartesian(List<Slot> slots)
    {
        var results = new List<string> { string.Empty };
        foreach (var slot in slots)
        {
            var next = new List<string>(results.Count * slot.Variants.Count);
            foreach (var prefix in results)
            {
                foreach (var variant in slot.Variants)
                {
                    next.Add(prefix + variant);
                }
            }

            results = next;
        }

        return results;
    }

    // 退避：一貫方式の2アンカー（RomajiEncoder の訓令式/ヘボン式形に一致）。癖なし・ー脱落。
    private static IReadOnlyList<string> TwoConsistent(List<Slot> slots)
    {
        var kunrei = string.Concat(slots.Select(slot => slot.Kunrei));
        var hepburn = string.Concat(slots.Select(slot => slot.Hepburn));
        return kunrei == hepburn ? [kunrei] : [kunrei, hepburn];
    }
}
