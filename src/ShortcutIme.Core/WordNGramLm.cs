using System.Runtime.InteropServices;
using System.Text;

namespace ShortcutIme.Core;

/// <summary>LM のトークン単位。語（文節表層）か文字か。Word/Char は別学習・別 blob（blob にモードを焼く）。</summary>
public enum TokenMode
{
    /// <summary>1トークン＝1文節（<see cref="Candidate.Surface"/>）。コーパスは空白区切り（fugashi 出力）。</summary>
    Word,

    /// <summary>1トークン＝1文字。語境界不変で OOV が出ない。コーパスは生文を文字分割。</summary>
    Char,
}

/// <summary>
/// 語/文字 bigram 言語モデル。学習済みコーパスから uni/bi-gram を数え、補間バックオフ
/// λ_bi·P(w|prev)+(1−λ_bi)·P(w) を確率空間で計算して log を事前計算し、CSR フラット配列＋文字列 intern で
/// 読み取り専用に圧縮する（<see cref="RomajiTrie"/> の直列化規律に倣う）。リランキングの加算項
/// <see cref="NegLogProb"/> を提供する。語(<see cref="TokenMode.Word"/>)/文字(<see cref="TokenMode.Char"/>)は別学習。
/// </summary>
public sealed class WordNGramLm
{
    // 番兵。語モードで実トークンと衝突しないよう制御文字を単一 const で持つ（Build と EOS step が同一値を参照）。
    private const string BosToken = "";
    private const string EosToken = "";
    private const int BosId = 0; // BOS は prev 専用（unigram 非計上）。
    private const int EosId = 1; // EOS は next 専用（unigram=文数、CSR 行は空）。

    private readonly TokenMode _mode;
    private readonly double _lambdaBi;
    private readonly double _floorNegLogProb;
    private readonly double _logOneMinusLambdaBi; // tier2 で使う ln(1−λ_bi) を事前計算。
    private readonly string[] _tokens;            // id→トークン（intern, id=index）。
    private readonly float[] _uniLogP;            // [V] ln P(w)。BOS スロットは floor 退避で finite。
    private readonly int[] _biRowStart;           // [V+1] prev の隣接は [_biRowStart[prev], _biRowStart[prev+1])。
    private readonly int[] _biNextId;             // [E] 行内 nextId 昇順（二分探索）。
    private readonly float[] _biLogP;             // [E] ln P_interp(next|prev)（補間済み）。
    private readonly Dictionary<string, int> _id; // トークン→id（Load 時に _tokens から派生）。

    private WordNGramLm(TokenMode mode, double lambdaBi, double floorNegLogProb,
        string[] tokens, float[] uniLogP, int[] biRowStart, int[] biNextId, float[] biLogP)
    {
        _mode = mode;
        _lambdaBi = lambdaBi;
        _floorNegLogProb = floorNegLogProb;
        _logOneMinusLambdaBi = Math.Log(1.0 - lambdaBi);
        _tokens = tokens;
        _uniLogP = uniLogP;
        _biRowStart = biRowStart;
        _biNextId = biNextId;
        _biLogP = biLogP;
        _id = new Dictionary<string, int>(tokens.Length, StringComparer.Ordinal);
        for (var i = 0; i < tokens.Length; i++)
        {
            _id[tokens[i]] = i;
        }
    }

    /// <summary>トークン単位（語/文字）。</summary>
    public TokenMode Mode => _mode;

    /// <summary>語彙数（BOS/EOS 番兵を含む）。</summary>
    public int VocabSize => _tokens.Length;

    /// <summary>モデルの規模統計（診断用）。</summary>
    public readonly record struct Stats(int Vocabulary, int DistinctBigrams);

    /// <summary>語彙数と異なり bigram 数を数える（診断用）。</summary>
    public Stats ComputeStats() => new(_tokens.Length, _biNextId.Length);

    /// <summary>
    /// 文候補（文節列）の負の対数尤度（nats, 正の有限）を返す。BOS→各トークン→EOS の bigram チェインを
    /// 三段バックオフ（観測 bigram／未観測は (1−λ_bi)·unigram／真の未知語は floor）で評価し合計する。
    /// EOS 項が文長の公平性を担保する。<paramref name="leftContext"/> は契約上受け取るが現状未使用。
    /// </summary>
    public double NegLogProb(IReadOnlyList<Candidate> segments, string leftContext)
    {
        ArgumentNullException.ThrowIfNull(segments);
        return NegLogProb(Tokenize(segments), leftContext);
    }

    /// <summary>
    /// 明示トークン列の負の対数尤度（BOS→各トークン→EOS の bigram チェイン）。トークン化を呼び手に委ねる版で、
    /// 読みステージ（segment.Reading を語/モーラに分割した列）など表層以外を採点するのに使う。
    /// </summary>
    public double NegLogProb(IEnumerable<string> tokens, string leftContext)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        _ = leftContext;
        var sum = 0.0;
        var prev = BosId;
        foreach (var token in tokens)
        {
            var wid = _id.GetValueOrDefault(token, -1);
            sum += StepNeg(prev, wid);
            prev = wid; // 未知語（wid<0）なら次段は文脈なし扱い。
        }

        sum += StepNeg(prev, EosId);
        return sum;
    }

    // 1ステップの負対数確率。全 tier finite（OrderBy を壊さない）。
    private double StepNeg(int prev, int wid)
    {
        if (wid < 0)
        {
            return _floorNegLogProb; // tier3: 真の未知語。
        }

        if (prev < 0)
        {
            return -_uniLogP[wid]; // prev が未知語 → 文脈なしの unigram。
        }

        var e = FindBigram(prev, wid);
        if (e >= 0)
        {
            return -_biLogP[e]; // tier1: 観測 bigram（補間済み）。
        }

        return -(_logOneMinusLambdaBi + _uniLogP[wid]); // tier2: 未観測 bigram の残り unigram 質量（floor ではない）。
    }

    /// <summary>
    /// 文候補を<b>読み</b>で採点した負の対数尤度。<see cref="NegLogProb(IReadOnlyList{Candidate},string)"/> の読み版で、
    /// 二段化の「読みを先に当てる」を STAGE でなく FEATURE（リランカーの一成分）として使うための採点。
    /// word モードは文節読みをトークン、char モードは読み連結を文字（モーラ）分割。
    /// </summary>
    public double NegLogProbReading(IReadOnlyList<Candidate> segments, string leftContext)
    {
        ArgumentNullException.ThrowIfNull(segments);
        return NegLogProb(TokenizeReading(segments), leftContext);
    }

    // 文節列をトークン列へ。word/char が切り替わる唯一の箇所。
    private IEnumerable<string> Tokenize(IReadOnlyList<Candidate> segments)
    {
        if (_mode == TokenMode.Word)
        {
            return segments.Select(segment => segment.Surface);
        }

        return string.Concat(segments.Select(segment => segment.Surface)).Select(ch => ch.ToString());
    }

    // 読みでのトークン化（Tokenize の Reading 版）。
    private IEnumerable<string> TokenizeReading(IReadOnlyList<Candidate> segments)
    {
        if (_mode == TokenMode.Word)
        {
            return segments.Select(segment => segment.Reading);
        }

        return string.Concat(segments.Select(segment => segment.Reading)).Select(ch => ch.ToString());
    }

    // prev 行内を nextId 昇順で二分探索。見つからなければ -1。
    private int FindBigram(int prev, int next)
    {
        var lo = _biRowStart[prev];
        var len = _biRowStart[prev + 1] - lo;
        if (len == 0)
        {
            return -1;
        }

        var idx = Array.BinarySearch(_biNextId, lo, len, next);
        return idx >= 0 ? idx : -1;
    }

    /// <summary>tier1（観測 bigram）ヒット率（診断用）。低いと文スコアが floor だらけ＝ノイズ化の兆候。</summary>
    public double HitRate(IReadOnlyList<Candidate> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var prev = BosId;
        var hits = 0;
        var total = 0;
        foreach (var token in Tokenize(segments))
        {
            var wid = _id.GetValueOrDefault(token, -1);
            total++;
            if (wid >= 0 && prev >= 0 && FindBigram(prev, wid) >= 0)
            {
                hits++;
            }

            prev = wid;
        }

        total++;
        if (prev >= 0 && FindBigram(prev, EosId) >= 0)
        {
            hits++;
        }

        return total == 0 ? 0.0 : (double)hits / total;
    }

    private const uint Magic = 0x53494C4D; // "SILM"
    private const int FormatVersion = 1;

    /// <summary>学習済みモデルをバイナリ直列化する（オフライン構築→起動時の高速ロード用）。</summary>
    public void Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.Create(path);
        Save(stream);
    }

    /// <summary>学習済みモデルをストリームへ直列化する。</summary>
    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write((int)_mode);
        writer.Write(_lambdaBi);
        writer.Write(_floorNegLogProb);
        writer.Write(_tokens.Length);
        WriteFloats(writer, _uniLogP);
        WriteStringPool(writer, _tokens);
        WriteInts(writer, _biRowStart);
        WriteInts(writer, _biNextId);
        WriteFloats(writer, _biLogP);
    }

    /// <summary>直列化済みモデルを読み込む。</summary>
    public static WordNGramLm Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    /// <summary>直列化済みモデルをストリームから読み込む。</summary>
    public static WordNGramLm Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt32() != Magic)
        {
            throw new InvalidDataException("WordNGramLm: マジックが不正です。");
        }

        var version = reader.ReadInt32();
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"WordNGramLm: 非対応のフォーマットバージョン {version}。");
        }

        var mode = (TokenMode)reader.ReadInt32();
        var lambdaBi = reader.ReadDouble();
        var floorNegLogProb = reader.ReadDouble();
        var vocab = reader.ReadInt32();
        var uniLogP = ReadFloats(reader);
        var tokens = ReadStringPool(reader);
        var biRowStart = ReadInts(reader);
        var biNextId = ReadInts(reader);
        var biLogP = ReadFloats(reader);

        if (tokens.Length != vocab || uniLogP.Length != vocab ||
            biRowStart.Length != vocab + 1 || biNextId.Length != biLogP.Length)
        {
            throw new InvalidDataException("WordNGramLm: 配列長が不整合です。");
        }

        return new WordNGramLm(mode, lambdaBi, floorNegLogProb, tokens, uniLogP, biRowStart, biNextId, biLogP);
    }

    private static void WriteInts(BinaryWriter writer, int[] values)
    {
        writer.Write(values.Length);
        writer.Write(MemoryMarshal.AsBytes(values.AsSpan()));
    }

    private static int[] ReadInts(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        var values = new int[length];
        reader.BaseStream.ReadExactly(MemoryMarshal.AsBytes(values.AsSpan()));
        return values;
    }

    private static void WriteFloats(BinaryWriter writer, float[] values)
    {
        writer.Write(values.Length);
        writer.Write(MemoryMarshal.AsBytes(values.AsSpan()));
    }

    private static float[] ReadFloats(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        var values = new float[length];
        reader.BaseStream.ReadExactly(MemoryMarshal.AsBytes(values.AsSpan()));
        return values;
    }

    private static void WriteStringPool(BinaryWriter writer, IReadOnlyList<string> pool)
    {
        writer.Write(pool.Count);
        foreach (var value in pool)
        {
            writer.Write(value);
        }
    }

    private static string[] ReadStringPool(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var pool = new string[count];
        for (var i = 0; i < count; i++)
        {
            pool[i] = reader.ReadString();
        }

        return pool;
    }

    /// <summary>テキストリーダ（1行1文）からモデルを学習する。word は空白区切り、char は生文を文字分割。</summary>
    public static WordNGramLm Build(TextReader corpus, TokenMode mode, double lambdaBi, double floorNegLogProb, double addK = 0.0)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        return Build(ReadLines(corpus), mode, lambdaBi, floorNegLogProb, addK);
    }

    /// <summary>文の列からモデルを学習する（小コーパス・テスト用）。</summary>
    public static WordNGramLm Build(IEnumerable<string> sentences, TokenMode mode, double lambdaBi, double floorNegLogProb, double addK = 0.0)
    {
        ArgumentNullException.ThrowIfNull(sentences);
        if (lambdaBi is <= 0.0 or >= 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(lambdaBi), "補間重み λ_bi は (0,1) の範囲で指定する。");
        }

        if (floorNegLogProb <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(floorNegLogProb), "OOV floor は正の値で指定する。");
        }

        if (addK < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(addK), "加算スムージング addK は非負で指定する。");
        }

        var builder = new Builder(mode);
        foreach (var sentence in sentences)
        {
            builder.AddSentence(sentence);
        }

        return builder.ToCompact(lambdaBi, floorNegLogProb, addK);
    }

    private static IEnumerable<string> ReadLines(TextReader reader)
    {
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            yield return line;
        }
    }

    /// <summary>
    /// 構築時のみ使う可変カウンタ。BOS/EOS を pre-seed し、語/文字を intern しながら uni/bi-gram を数える。
    /// <see cref="ToCompact"/> で補間 log 確率を焼いた CSR フラット配列へ変換する。
    /// </summary>
    private sealed class Builder
    {
        private readonly TokenMode _mode;
        private readonly Dictionary<string, int> _id = new(StringComparer.Ordinal);
        private readonly List<string> _tokens = [];
        private readonly List<long> _uniCount = [];
        private readonly Dictionary<int, Dictionary<int, long>> _biCount = [];

        public Builder(TokenMode mode)
        {
            _mode = mode;
            Intern(BosToken); // id 0
            Intern(EosToken); // id 1
        }

        public void AddSentence(string sentence)
        {
            ArgumentNullException.ThrowIfNull(sentence);
            var prev = BosId;
            foreach (var token in Tokenize(sentence))
            {
                var id = Intern(token);
                _uniCount[id]++;
                AddBigram(prev, id);
                prev = id;
            }

            AddBigram(prev, EosId); // 文末（短文が不当に得しないよう EOS を価格付け）。
            _uniCount[EosId]++;
        }

        private IEnumerable<string> Tokenize(string sentence)
        {
            if (_mode == TokenMode.Word)
            {
                return sentence.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            }

            return sentence.Where(ch => !char.IsWhiteSpace(ch)).Select(ch => ch.ToString());
        }

        private int Intern(string token)
        {
            if (!_id.TryGetValue(token, out var id))
            {
                id = _tokens.Count;
                _id[token] = id;
                _tokens.Add(token);
                _uniCount.Add(0);
            }

            return id;
        }

        private void AddBigram(int prev, int next)
        {
            if (!_biCount.TryGetValue(prev, out var row))
            {
                row = [];
                _biCount[prev] = row;
            }

            row[next] = row.GetValueOrDefault(next) + 1;
        }

        public WordNGramLm ToCompact(double lambdaBi, double floorNegLogProb, double addK)
        {
            var vocab = _tokens.Count;
            long uniTotal = 0;
            for (var i = 0; i < vocab; i++)
            {
                uniTotal += _uniCount[i];
            }

            // unigram log 確率（BOS は count=0＝floor 退避で finite に保つ）。
            var uniLogP = new float[vocab];
            var uniProb = new double[vocab];
            var denom = uniTotal + (addK * vocab);
            for (var i = 0; i < vocab; i++)
            {
                if (_uniCount[i] == 0 && addK == 0.0)
                {
                    uniProb[i] = 0.0;
                    uniLogP[i] = (float)(-floorNegLogProb);
                }
                else
                {
                    var p = (_uniCount[i] + addK) / denom;
                    uniProb[i] = p;
                    uniLogP[i] = (float)Math.Log(p);
                }
            }

            // bigram CSR（行内 next 昇順、補間済み log を tier1 として焼く）。
            var distinctBigrams = 0;
            for (var prev = 0; prev < vocab; prev++)
            {
                if (_biCount.TryGetValue(prev, out var row))
                {
                    distinctBigrams += row.Count;
                }
            }

            var biRowStart = new int[vocab + 1];
            var biNextId = new int[distinctBigrams];
            var biLogP = new float[distinctBigrams];
            var cursor = 0;
            for (var prev = 0; prev < vocab; prev++)
            {
                biRowStart[prev] = cursor;
                if (_biCount.TryGetValue(prev, out var row))
                {
                    long rowSum = 0;
                    foreach (var count in row.Values)
                    {
                        rowSum += count;
                    }

                    foreach (var pair in row.OrderBy(p => p.Key))
                    {
                        var pmle = pair.Value / (double)rowSum;
                        var interpolated = (lambdaBi * pmle) + ((1.0 - lambdaBi) * uniProb[pair.Key]);
                        biNextId[cursor] = pair.Key;
                        biLogP[cursor] = (float)Math.Log(interpolated);
                        cursor++;
                    }
                }
            }

            biRowStart[vocab] = cursor;

            return new WordNGramLm(_mode, lambdaBi, floorNegLogProb, [.. _tokens], uniLogP, biRowStart, biNextId, biLogP);
        }
    }
}
