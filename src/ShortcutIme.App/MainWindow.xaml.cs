using Microsoft.UI.Xaml;

namespace ShortcutIme_App;

/// <summary>
/// アプリケーションウィンドウ。MainPage をホストし、グローバルホットキー召喚を仲介する。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly HotKeyService _hotKey;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        RootFrame.Navigate(typeof(MainPage));

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _hotKey = new HotKeyService(hwnd, OnSummon);
        Closed += (_, _) => _hotKey.Dispose();
    }

    /// <summary>ホットキー押下時：ウィンドウを前面化し、注入先（直前アプリ）を MainPage へ渡す。</summary>
    private void OnSummon()
    {
        Activate();
        if (RootFrame.Content is MainPage page)
        {
            page.SetInjectTarget(_hotKey.LastForegroundWindow);
        }
    }
}
