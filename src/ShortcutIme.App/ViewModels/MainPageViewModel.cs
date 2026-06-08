using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShortcutIme.Core;
using Windows.ApplicationModel.DataTransfer;

namespace ShortcutIme_App.ViewModels;

/// <summary>候補一覧の1項目（1始まりの順位＋表層）。</summary>
public sealed record CandidateChoice(int Number, string Surface)
{
    /// <summary>一覧表示用（例：「1.  今日は」）。x:Bind を string 一本にするため。</summary>
    public string Display => $"{Number}.  {Surface}";
}

/// <summary>
/// 文章一括変換の画面ロジック。文の読み（フルローマ字／子音）を入力すると、
/// <see cref="PhraseConverter"/> がビタビ＋連接コストで n-best を出し、char+word リランカーで
/// 並べ替えた候補一覧を提示する。選んだ候補を確定で文へつなげる。
/// </summary>
public partial class MainPageViewModel : ObservableObject
{
    // リランキングの運用パラメータ（docs/stage1-wordlm.md §5 の frozen 値）。
    private const int NBest = 100;
    // 活用辞書(dictionary98)込みで再学習した word.bin に対し dev で再調整（tune-interp）。
    private const double LambdaChar = 50.0;
    private const double LambdaWord = 500.0;
    private const int MaxCandidates = 30; // 一覧に出す異なり表層の上限。

    private readonly RomajiEncoder _romaji = new();

    private PhraseConverter? _converter;
    private IReranker _reranker = IdentityReranker.Instance;

    /// <summary>リランク後の候補一覧（最尤が先頭）。同一表層は先頭出現のみ。</summary>
    public ObservableCollection<CandidateChoice> Candidates { get; } = [];

    /// <summary>一覧で選択中の候補インデックス（確定対象）。候補が無ければ -1。</summary>
    [ObservableProperty]
    public partial int SelectedCandidateIndex { get; set; } = -1;

    /// <summary>確定済みの文。</summary>
    [ObservableProperty]
    public partial string ComposedText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsReady { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "辞書・連接行列を読み込み中…";

    /// <summary>ホットキー召喚直前にフォーカスしていた、注入先ウィンドウ。</summary>
    public nint InjectTarget { get; set; }

    public MainPageViewModel() => _ = InitializeAsync();

    private async Task InitializeAsync()
    {
        try
        {
            var dictDir = Path.Combine(AppContext.BaseDirectory, "dict");
            (_converter, _reranker) = await Task.Run(() => Build(dictDir));
            IsReady = true;
            var lm = _reranker is LmReranker ? "char+word LM リランカー有効" : "リランカーなし（LM blob 未配置）";
            StatusText = $"準備完了（{lm}）。文の読みをローマ字/子音で打ち、Enter で確定して文をつなげます。";
        }
        catch (IOException ex)
        {
            StatusText = $"読み込みに失敗しました：{ex.Message}";
        }
    }

    private static (PhraseConverter Converter, IReranker Reranker) Build(string dictDir)
    {
        var triePath = Path.Combine(dictDir, "trie.bin");

        // 直列化済みトライがあれば高速ロード（テキスト再パース・再構築なし）、無ければ構築してキャッシュ。
        RomajiTrie trie;
        try
        {
            trie = File.Exists(triePath) ? RomajiTrie.Load(triePath) : BuildAndCacheTrie(dictDir, triePath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            trie = BuildAndCacheTrie(dictDir, triePath); // 壊れた/古いキャッシュは作り直す
        }

        var connectionPath = Path.Combine(dictDir, "connection_single_column.txt");
        var connection = File.Exists(connectionPath) ? ConnectionMatrix.Load(connectionPath) : null;

        // Measure のスイープで全文一致が得られた設定（seg=0 / skip=500）。
        var converter = new PhraseConverter(trie, connection, segmentPenalty: 0, vowelSkipPenalty: 500);
        return (converter, BuildReranker(dictDir));
    }

    // char.bin + word.bin が配置されていれば char+word 補間リランカーを、無ければ identity を返す。
    // 両 LM は表層しか見ないので方式/母音レベルに依存しない（docs/stage1-wordlm.md）。
    private static IReranker BuildReranker(string dictDir)
    {
        var components = new List<(WordNGramLm Lm, double Lambda)>();
        TryAddLm(components, Path.Combine(dictDir, "char.bin"), LambdaChar);
        TryAddLm(components, Path.Combine(dictDir, "word.bin"), LambdaWord);
        return components.Count > 0 ? new LmReranker(components) : IdentityReranker.Instance;
    }

    private static void TryAddLm(List<(WordNGramLm Lm, double Lambda)> components, string path, double lambda)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            components.Add((WordNGramLm.Load(path), lambda));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            // 壊れた blob は無視してリランカーから外す（変換自体は WFST 1-best で動く）。
        }
    }

    // 多方式（訓令式/ヘボン式の混在）でトライを構築し、blob にキャッシュする。
    private static RomajiTrie BuildAndCacheTrie(string dictDir, string triePath)
    {
        var entries = Directory.EnumerateFiles(dictDir, "dictionary*.txt")
            .SelectMany(MozcDictionaryReader.ReadFile);
        var trie = RomajiTrie.Build(entries, reading => RomajiVariants.ExpandReadingWithHabits(reading));
        try
        {
            trie.Save(triePath);
        }
        catch (IOException)
        {
            // キャッシュ保存は best-effort（読み取り専用配置などでは保存できなくてよい）。
        }

        return trie;
    }

    /// <summary>入力（読み）を n-best 変換し、char+word リランカーで並べ替えた候補一覧を更新する。</summary>
    public void UpdateInput(string input)
    {
        Candidates.Clear();
        SelectedCandidateIndex = -1;
        if (_converter is null || string.IsNullOrEmpty(input))
        {
            return;
        }

        var nbest = _converter.ConvertNBest(input, NBest);
        if (nbest.Count == 0)
        {
            return;
        }

        // 異なる分割が同じ表層を生むため、表層で dedup（順位は維持）し上位 MaxCandidates 件を出す。
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hypothesis in _reranker.Rerank(input, "", nbest))
        {
            if (seen.Add(hypothesis.Surface))
            {
                Candidates.Add(new CandidateChoice(Candidates.Count + 1, hypothesis.Surface));
                if (Candidates.Count >= MaxCandidates)
                {
                    break;
                }
            }
        }

        if (Candidates.Count > 0)
        {
            SelectedCandidateIndex = 0;
        }
    }

    /// <summary>選択中（既定は最尤）の候補を確定文へ追記する。</summary>
    public void Commit()
    {
        if ((uint)SelectedCandidateIndex >= (uint)Candidates.Count)
        {
            return;
        }

        ComposedText += Candidates[SelectedCandidateIndex].Surface;
        Candidates.Clear();
        SelectedCandidateIndex = -1;
    }

    /// <summary>候補一覧の選択を上下に移動する（範囲内にクランプ）。</summary>
    public void MoveSelection(int delta)
    {
        if (Candidates.Count == 0)
        {
            return;
        }

        var index = SelectedCandidateIndex + delta;
        SelectedCandidateIndex = Math.Clamp(index, 0, Candidates.Count - 1);
    }

    /// <summary>確定文をクリップボードへコピーする。</summary>
    [RelayCommand]
    private void CopyComposed()
    {
        if (string.IsNullOrEmpty(ComposedText))
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(ComposedText);
        Clipboard.SetContent(package);
    }

    /// <summary>確定文をクリアする。</summary>
    [RelayCommand]
    private void ClearComposed() => ComposedText = "";

    /// <summary>確定文を、直前にフォーカスしていたアプリへ貼り付け注入する。</summary>
    [RelayCommand]
    private void Send()
    {
        if (!string.IsNullOrEmpty(ComposedText))
        {
            TextInjector.InjectViaPaste(InjectTarget, ComposedText);
        }
    }
}
