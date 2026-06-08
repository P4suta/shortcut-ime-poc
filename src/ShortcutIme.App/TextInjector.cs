using System.Runtime.InteropServices;
using ShortcutIme_App.Interop;
using Windows.ApplicationModel.DataTransfer;

namespace ShortcutIme_App;

/// <summary>
/// 確定文をクリップボード経由でフォーカス中（直前の）アプリへ貼り付け注入する。
/// PoC ではクリップボードを復元しない（非同期ペーストとの競合を避けるため確定文を残す）。
/// </summary>
internal static class TextInjector
{
    /// <summary>ターゲットウィンドウを前面化し、Ctrl+V で <paramref name="text"/> を貼り付ける。</summary>
    public static void InjectViaPaste(nint targetHwnd, string text)
    {
        if (targetHwnd == 0 || string.IsNullOrEmpty(text))
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);

        NativeMethods.SetForegroundWindow(targetHwnd);
        Thread.Sleep(80); // 前面化が反映されるまでの猶予
        SendCtrlV();
    }

    private static void SendCtrlV()
    {
        NativeMethods.INPUT[] inputs =
        [
            Key(NativeMethods.VkControl, isUp: false),
            Key(NativeMethods.VkV, isUp: false),
            Key(NativeMethods.VkV, isUp: true),
            Key(NativeMethods.VkControl, isUp: true),
        ];
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT Key(ushort vk, bool isUp) => new()
    {
        Type = NativeMethods.InputKeyboard,
        U = new NativeMethods.InputUnion
        {
            Ki = new NativeMethods.KEYBDINPUT
            {
                Vk = vk,
                Flags = isUp ? NativeMethods.KeyEventKeyUp : 0,
            },
        },
    };
}
