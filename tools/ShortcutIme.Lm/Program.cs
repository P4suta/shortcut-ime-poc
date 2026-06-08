using System.Globalization;
using ShortcutIme.Core;

// corpus.tsv（1行1文）から n-gram LM を学習し blob へ直列化する（オフライン事前処理→起動時 Load）。
// 使い方: dotnet run --project tools/ShortcutIme.Lm -- <corpus.tsv> <out.bin> <word|char> <lambdaBi> <floor> [addK]

if (args.Length < 5)
{
    Console.Error.WriteLine("usage: <corpus.tsv> <out.bin> <word|char> <lambdaBi> <floor> [addK]");
    return 1;
}

var corpusPath = args[0];
var outPath = args[1];
var mode = args[2].ToLowerInvariant() switch
{
    "word" => TokenMode.Word,
    "char" => TokenMode.Char,
    _ => throw new ArgumentException($"未知のモード: '{args[2]}'（word|char）"),
};
var lambdaBi = double.Parse(args[3], CultureInfo.InvariantCulture);
var floor = double.Parse(args[4], CultureInfo.InvariantCulture);
var addK = args.Length > 5 ? double.Parse(args[5], CultureInfo.InvariantCulture) : 0.0;

Console.WriteLine($"コーパス: {Path.GetFullPath(corpusPath)}（mode={mode}, λ_bi={lambdaBi}, floor={floor}, addK={addK}）");
using var reader = new StreamReader(corpusPath);
var lm = WordNGramLm.Build(reader, mode, lambdaBi, floor, addK);
var stats = lm.ComputeStats();
Console.WriteLine($"学習完了: vocab={stats.Vocabulary:N0}, distinct-bigrams={stats.DistinctBigrams:N0}");
lm.Save(outPath);
Console.WriteLine($"保存: {Path.GetFullPath(outPath)}");
return 0;
