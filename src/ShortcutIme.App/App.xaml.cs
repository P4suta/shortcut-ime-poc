using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ShortcutIme_App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// The main application window. Use <c>App.Window</c> from any class that needs
    /// the window reference (for dialogs, pickers, interop, etc.).
    /// </summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>
    /// The UI thread dispatcher. Use <c>App.DispatcherQueue</c> to marshal calls
    /// to the UI thread. Fully qualified to avoid CS0104 ambiguity with
    /// <see cref="Windows.System.DispatcherQueue"/>.
    /// </summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    /// <summary>
    /// The native window handle (HWND). Use for file pickers,
    /// <c>DataTransferManager</c>, and any WinRT interop that requires
    /// <c>InitializeWithWindow</c>.
    /// </summary>
    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    private static readonly string LogPath =
        System.IO.Path.Combine(AppContext.BaseDirectory, "startup.log");

    /// <summary>起動診断ログ（無音終了の原因切り分け用）。exe フォルダの startup.log へ追記。</summary>
    private static void Log(string message)
    {
        try
        {
            System.IO.File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // ログ失敗は無視（診断目的のみ）。
        }
    }

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        Log("App() ctor 開始");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log($"AppDomain 未処理例外: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) => { Log($"未観測タスク例外: {e.Exception}"); e.SetObserved(); };
        UnhandledException += (_, e) => Log($"XAML 未処理例外: {e.Message}\n{e.Exception}");
        InitializeComponent();
        Log("App() ctor 完了");
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            Log("OnLaunched 開始");
            Window = new MainWindow();
            Log("MainWindow 構築完了");
            DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            Window.Activate();
            Log("Window.Activate 完了");
        }
        catch (Exception ex)
        {
            Log($"OnLaunched 例外: {ex}");
            throw;
        }
    }
}
