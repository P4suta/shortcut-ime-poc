using System.Globalization;

namespace ShortcutIme.Core;

/// <summary>
/// LightGBM の text モデル（<c>booster.save_model</c> 出力）を読み込み、特徴ベクトルをスコアリングする純 C# 評価器。
/// 外部依存なし（Core は純 C# のまま）＝[[architecture-beauty-first]]。数値分割のみ（categorical 非対応・本用途は全数値）。
/// 予測＝各木で到達した葉値の総和（lambdarank の raw スコア。大きいほど上位）。leaf_value は学習率込みで保存される
/// ため追加の shrinkage 乗算は不要（Python 予測とのクロスチェックで担保）。
/// </summary>
public sealed class GradientBoostedTrees
{
    private readonly Tree[] _trees;

    /// <summary>モデルが期待する特徴数（max_feature_idx+1）。</summary>
    public int FeatureCount { get; }

    private GradientBoostedTrees(Tree[] trees, int featureCount)
    {
        _trees = trees;
        FeatureCount = featureCount;
    }

    /// <summary>特徴ベクトルのスコア（全木の葉値の総和）。</summary>
    public double Evaluate(double[] features)
    {
        ArgumentNullException.ThrowIfNull(features);
        var sum = 0.0;
        foreach (var tree in _trees)
        {
            sum += tree.Evaluate(features);
        }

        return sum;
    }

    /// <summary>LightGBM text モデルファイルを読み込む。</summary>
    public static GradientBoostedTrees Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var reader = new StreamReader(path);
        return Load(reader);
    }

    /// <summary>LightGBM text モデルを読み込む（テスト用に TextReader を受ける）。</summary>
    public static GradientBoostedTrees Load(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var trees = new List<Tree>();
        var featureCount = 0;

        // 1 木ブロックぶんの key→value を貯めてから Tree を構築する。
        var block = new Dictionary<string, string>(StringComparer.Ordinal);
        var inTree = false;

        void Flush()
        {
            if (inTree && block.Count > 0)
            {
                trees.Add(Tree.Parse(block));
            }

            block.Clear();
        }

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("Tree=", StringComparison.Ordinal))
            {
                Flush();
                inTree = true;
                continue;
            }

            if (line.StartsWith("end of trees", StringComparison.Ordinal))
            {
                Flush();
                inTree = false;
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = line[..eq];
            var value = line[(eq + 1)..];
            if (!inTree && key == "max_feature_idx")
            {
                featureCount = int.Parse(value, CultureInfo.InvariantCulture) + 1;
            }

            if (inTree)
            {
                block[key] = value;
            }
        }

        Flush();
        if (trees.Count == 0)
        {
            throw new InvalidDataException("LightGBM モデルに木が見つからない。");
        }

        return new GradientBoostedTrees([.. trees], featureCount);
    }

    private sealed class Tree
    {
        private readonly int[] _splitFeature;
        private readonly double[] _threshold;
        private readonly int[] _leftChild;
        private readonly int[] _rightChild;
        private readonly double[] _leafValue;

        private Tree(int[] splitFeature, double[] threshold, int[] leftChild, int[] rightChild, double[] leafValue)
        {
            _splitFeature = splitFeature;
            _threshold = threshold;
            _leftChild = leftChild;
            _rightChild = rightChild;
            _leafValue = leafValue;
        }

        public double Evaluate(double[] features)
        {
            // 葉のみ（num_leaves==1 で分割なし）の木は単一葉値。
            if (_splitFeature.Length == 0)
            {
                return _leafValue.Length > 0 ? _leafValue[0] : 0.0;
            }

            var node = 0; // 内部ノード 0 から降下。
            while (true)
            {
                var child = features[_splitFeature[node]] <= _threshold[node] ? _leftChild[node] : _rightChild[node];
                if (child < 0)
                {
                    return _leafValue[~child]; // child<0 は葉。葉番号 = ~child（= -child-1）。
                }

                node = child;
            }
        }

        public static Tree Parse(Dictionary<string, string> block)
        {
            var splitFeature = ParseInts(block.GetValueOrDefault("split_feature"));
            var threshold = ParseDoubles(block.GetValueOrDefault("threshold"));
            var leftChild = ParseInts(block.GetValueOrDefault("left_child"));
            var rightChild = ParseInts(block.GetValueOrDefault("right_child"));
            var leafValue = ParseDoubles(block.GetValueOrDefault("leaf_value"));
            return new Tree(splitFeature, threshold, leftChild, rightChild, leafValue);
        }

        private static int[] ParseInts(string? s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return [];
            }

            var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var result = new int[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                result[i] = int.Parse(parts[i], CultureInfo.InvariantCulture);
            }

            return result;
        }

        private static double[] ParseDoubles(string? s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return [];
            }

            var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var result = new double[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                result[i] = double.Parse(parts[i], CultureInfo.InvariantCulture);
            }

            return result;
        }
    }
}
