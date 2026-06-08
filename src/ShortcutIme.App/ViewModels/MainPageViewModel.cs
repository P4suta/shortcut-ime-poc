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
/// 逐次文節確定の画面ロジック（docs/stage5-incremental-commit.md）。文の読み（フルローマ字／子音）を入力すると、
/// <see cref="LookaheadConverter"/> が「確定済み左文脈の下での次文節候補」を提示する。1つ確定すると左文脈に積まれ、
/// 残り入力の次文節候補へ更新される。これを繰り返して文を組む（候補UIで per-step ほぼ確実に正解が top-k に入る）。
/// </summary>
public partial class MainPageViewModel : ObservableObject
{
    private const int NBest = 100;
    // 活用辞書(dictionary98)込みで再学習した word.bin に対し dev で再調整（tune-interp）。
    private const double LambdaChar = 50.0;
    private const double LambdaWord = 500.0;
    private const int MaxCandidates = 30;   // 一覧に出す異なり表層の上限。
    private const int IncSegmentPenalty = 3000; // 逐次の過分割抑制（docs/stage5）。

    private LookaheadConverter? _incremental;
    private string _input = "";
    private int _pos;
    private readonly List<Candidate> _committed = []; // 確定済み左文脈（文節列）。
    private readonly List<NextSegment> _displayed = []; // Candidates と並行（dedup 後）。確定時の長さ参照用。

    /// <summary>次文節の候補一覧（最尤が先頭）。同一表層は先頭出現のみ。</summary>
    public ObservableCollection<CandidateChoice> Candidates { get; } = [];

    /// <summary>現在の入力をすべて消費し終えたか（view が入力欄をクリアする判断に使う）。</summary>
    public bool InputConsumed => _pos >= _input.Length;

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
            _incremental = await Task.Run(() => Build(dictDir));
            IsReady = true;
            StatusText = "準備完了。文の読みをローマ字/子音で打つと文節候補が出ます。Enter/ダブルクリックで文節を確定し、左から組み立てます。";
        }
        catch (IOException ex)
        {
            StatusText = $"読み込みに失敗しました：{ex.Message}";
        }
    }

    private static LookaheadConverter Build(string dictDir)
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

        // 逐次は過分割抑制のため segPenalty を効かせる（docs/stage5-incremental-commit.md）。
        var converter = new PhraseConverter(trie, connection, segmentPenalty: IncSegmentPenalty, vowelSkipPenalty: 500);
        return new LookaheadConverter(converter, BuildLmComponents(dictDir), NBest);
    }

    // char.bin + word.bin があれば char+word の成分を返す（確定左文脈の継続採点に使う）。無ければ空＝コストのみで並べる。
    private static List<LmReranker.Component> BuildLmComponents(string dictDir)
    {
        var components = new List<LmReranker.Component>();
        TryAddLm(components, Path.Combine(dictDir, "char.bin"), LambdaChar);
        TryAddLm(components, Path.Combine(dictDir, "word.bin"), LambdaWord);
        return components;
    }

    private static void TryAddLm(List<LmReranker.Component> components, string path, double lambda)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            components.Add(new LmReranker.Component(WordNGramLm.Load(path), lambda));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            // 壊れた blob は無視（変換はコストのみで動く）。
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

    /// <summary>入力（読み）を新たに受け取り、先頭文節の候補一覧を出す（位置・左文脈をリセット）。</summary>
    public void UpdateInput(string input)
    {
        _input = input ?? "";
        _pos = 0;
        _committed.Clear();
        RefreshCandidates();
    }

    // 現在位置・左文脈での次文節候補を出す（表層で dedup）。
    private void RefreshCandidates()
    {
        Candidates.Clear();
        _displayed.Clear();
        SelectedCandidateIndex = -1;
        if (_incremental is null || _pos >= _input.Length)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var next in _incremental.NextCandidates(_input, _pos, _committed, MaxCandidates * 2))
        {
            if (seen.Add(next.Candidate.Surface))
            {
                _displayed.Add(next);
                Candidates.Add(new CandidateChoice(Candidates.Count + 1, next.Candidate.Surface));
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

    /// <summary>選択中の文節を確定文へ追記し、左文脈へ積んで残り入力の次文節候補へ更新する。</summary>
    public void Commit()
    {
        if ((uint)SelectedCandidateIndex >= (uint)_displayed.Count)
        {
            return;
        }

        var chosen = _displayed[SelectedCandidateIndex];
        ComposedText += chosen.Candidate.Surface;
        _committed.Add(chosen.Candidate);
        _pos += chosen.Length;

        if (_pos >= _input.Length)
        {
            // 入力を消費し切った：状態をリセット（view が入力欄をクリアする）。
            _input = "";
            _pos = 0;
            _committed.Clear();
            Candidates.Clear();
            _displayed.Clear();
            SelectedCandidateIndex = -1;
        }
        else
        {
            RefreshCandidates();
        }
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
