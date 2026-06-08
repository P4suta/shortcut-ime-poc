using System.Text;

namespace ShortcutIme.Evaluation;

/// <summary>
/// テンプレート＋スロット候補から、読みが正確な文を機械的に大量生成する（テスト大幅拡充用）。
/// スロット候補・リテラルとも (表層, 読み) を与えるので、連結した文の読みは構成上正しく「出題ミス」が起きない。
/// 文法はテンプレ依存で多様性は限定的だが、子音入力の曖昧性解消（リランカーの評価）には十分な実文に近い。
/// </summary>
public static class SentenceTemplateGenerator
{
    private readonly record struct Word(string Surface, string Reading);

    private static readonly Dictionary<string, Word[]> Fillers = new()
    {
        ["noun"] =
        [
            new("資料", "しりょう"), new("書類", "しょるい"), new("会議", "かいぎ"), new("予定", "よてい"),
            new("日程", "にってい"), new("担当者", "たんとうしゃ"), new("電話", "でんわ"), new("連絡", "れんらく"),
            new("商品", "しょうひん"), new("在庫", "ざいこ"), new("注文", "ちゅうもん"), new("見積もり", "みつもり"),
            new("契約", "けいやく"), new("報告", "ほうこく"), new("資金", "しきん"),
        ],
        ["noun2"] =
        [
            new("内容", "ないよう"), new("詳細", "しょうさい"), new("期限", "きげん"), new("価格", "かかく"),
            new("数量", "すうりょう"), new("条件", "じょうけん"), new("結果", "けっか"), new("状況", "じょうきょう"),
        ],
        ["verbTe"] =
        [
            new("送って", "おくって"), new("確認して", "かくにんして"), new("連絡して", "れんらくして"),
            new("準備して", "じゅんびして"), new("検討して", "けんとうして"), new("対応して", "たいおうして"),
            new("修正して", "しゅうせいして"), new("報告して", "ほうこくして"),
        ],
        ["verbMasu"] =
        [
            new("送ります", "おくります"), new("確認します", "かくにんします"), new("連絡します", "れんらくします"),
            new("準備します", "じゅんびします"), new("検討します", "けんとうします"), new("対応します", "たいおうします"),
            new("報告します", "ほうこくします"), new("お願いします", "おねがいします"),
        ],
        ["time"] =
        [
            new("本日", "ほんじつ"), new("明日", "あした"), new("来週", "らいしゅう"),
            new("来月", "らいげつ"), new("午後", "ごご"), new("今週", "こんしゅう"),
        ],
        ["adj"] =
        [
            new("必要", "ひつよう"), new("可能", "かのう"), new("大切", "たいせつ"),
            new("重要", "じゅうよう"), new("困難", "こんなん"),
        ],
    };

    // 各テンプレ：パーツ列。"=…" はリテラル（助詞・補助＝仮名なので表層＝読み）、それ以外はスロット名。
    private static readonly string[][] Templates =
    [
        ["noun", "=を", "verbMasu"],
        ["noun", "=を", "verbTe", "=ください"],
        ["noun", "=は", "adj", "=です"],
        ["time", "=は", "noun", "=を", "verbMasu"],
        ["noun", "=の", "noun2", "=を", "verbMasu"],
        ["noun", "=を", "verbTe", "=いただけますか"],
        ["noun", "=について", "verbMasu"],
        ["time", "=の", "noun", "=を", "verbTe", "=ください"],
    ];

    private const int PerTemplateCap = 60;

    /// <summary>テンプレ×スロットを展開して事例を生成する（表層で重複排除、<paramref name="max"/> 件で打ち切り）。</summary>
    public static IReadOnlyList<EvalCase> Generate(int max = 400)
    {
        var seen = new HashSet<string>();
        var result = new List<EvalCase>();
        foreach (var template in Templates)
        {
            var slots = template.Where(part => !part.StartsWith('=')).ToArray();
            var counts = slots.Select(slot => Fillers[slot].Length).ToArray();
            long total = 1;
            foreach (var count in counts)
            {
                total *= count;
            }

            var produced = 0;
            for (long t = 0; t < total && produced < PerTemplateCap; t++)
            {
                var indices = new Dictionary<string, int>(slots.Length);
                var remainder = t;
                for (var s = 0; s < slots.Length; s++)
                {
                    indices[slots[s]] = (int)(remainder % counts[s]);
                    remainder /= counts[s];
                }

                var surface = new StringBuilder();
                var reading = new StringBuilder();
                foreach (var part in template)
                {
                    if (part.StartsWith('='))
                    {
                        surface.Append(part, 1, part.Length - 1);
                        reading.Append(part, 1, part.Length - 1);
                    }
                    else
                    {
                        var word = Fillers[part][indices[part]];
                        surface.Append(word.Surface);
                        reading.Append(word.Reading);
                    }
                }

                var sentence = surface.ToString();
                if (seen.Add(sentence))
                {
                    result.Add(new EvalCase(sentence, reading.ToString()));
                    produced++;
                    if (result.Count >= max)
                    {
                        return result;
                    }
                }
            }
        }

        return result;
    }
}
