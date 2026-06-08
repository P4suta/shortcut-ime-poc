using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ShortcutIme_App.ViewModels;

namespace ShortcutIme_App;

/// <summary>
/// 文章一括変換の入力画面。読みの入力でプレビューを更新し、Enter で確定文へ追記する。
/// </summary>
public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; } = new();

    public MainPage()
    {
        InitializeComponent();
    }

    private void OnInputChanged(object sender, TextChangedEventArgs e)
        => ViewModel.UpdateInput(InputBox.Text);

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Enter:
                CommitSelected();
                e.Handled = true;
                break;
            // 入力欄にフォーカスを置いたまま ↑↓ で候補を選べる（キャレット移動は奪う）。
            case Windows.System.VirtualKey.Down:
                ViewModel.MoveSelection(1);
                ScrollSelectionIntoView();
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Up:
                ViewModel.MoveSelection(-1);
                ScrollSelectionIntoView();
                e.Handled = true;
                break;
        }
    }

    /// <summary>候補のダブルクリックでその候補を確定する。</summary>
    private void OnCandidateInvoked(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        => CommitSelected();

    private void CommitSelected()
    {
        ViewModel.Commit();
        InputBox.Text = "";
        InputBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
    }

    private void ScrollSelectionIntoView()
    {
        if (ViewModel.SelectedCandidateIndex >= 0)
        {
            CandidateList.ScrollIntoView(CandidateList.SelectedItem);
        }
    }

    /// <summary>ホットキー召喚時に注入先ウィンドウを ViewModel へ渡す。</summary>
    public void SetInjectTarget(nint target) => ViewModel.InjectTarget = target;
}
