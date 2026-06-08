using ShortcutIme_App.Interop;

namespace ShortcutIme_App;

/// <summary>
/// グローバルホットキー（Ctrl+Alt+Space）でアプリを召喚し、押下直前のフォアグラウンドウィンドウを記憶する。
/// WM_HOTKEY はウィンドウサブクラス化で受信する。
/// </summary>
internal sealed class HotKeyService : IDisposable
{
    private const int HotkeyId = 1;
    private const nuint SubclassId = 1;
    private const ushort VkSpace = 0x20;

    private readonly nint _hwnd;
    private readonly NativeMethods.SubclassProc _subclassProc; // GC されないようフィールドで保持
    private readonly Action _onSummon;
    private bool _registered;

    /// <summary>ホットキー押下直前のフォアグラウンドウィンドウ（注入先）。</summary>
    public nint LastForegroundWindow { get; private set; }

    /// <summary>ホットキー登録に成功したか。</summary>
    public bool IsRegistered => _registered;

    public HotKeyService(nint hwnd, Action onSummon)
    {
        _hwnd = hwnd;
        _onSummon = onSummon;
        _subclassProc = SubclassCallback;
        NativeMethods.SetWindowSubclass(_hwnd, _subclassProc, SubclassId, 0);
        _registered = NativeMethods.RegisterHotKey(
            _hwnd,
            HotkeyId,
            NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat,
            VkSpace);
    }

    private nint SubclassCallback(nint hWnd, uint uMsg, nuint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData)
    {
        if (uMsg == NativeMethods.WmHotkey && (int)wParam == HotkeyId)
        {
            LastForegroundWindow = NativeMethods.GetForegroundWindow();
            NativeMethods.SetForegroundWindow(_hwnd);
            _onSummon();
        }
        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
            _registered = false;
        }
        NativeMethods.RemoveWindowSubclass(_hwnd, _subclassProc, SubclassId);
    }
}
