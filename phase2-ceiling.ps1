$OutputEncoding = [Console]::OutputEncoding = [Console]::InputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'

$app = Get-Process ShortcutIme.App -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $app) { Write-Host 'App not running'; exit 2 }
$appPid = $app.Id
Write-Host "App PID: $appPid"

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Si {
    [StructLayout(LayoutKind.Explicit, Size=40)]
    public struct INPUT {
        [FieldOffset(0)]  public uint type;
        [FieldOffset(8)]  public ushort wVk;
        [FieldOffset(10)] public ushort wScan;
        [FieldOffset(12)] public uint dwFlags;
        [FieldOffset(16)] public uint time;
        [FieldOffset(24)] public IntPtr dwExtraInfo;
    }
    [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] p, int size);
    static INPUT K(ushort vk, bool up){ var i=new INPUT(); i.type=1; i.wVk=vk; i.dwFlags=up?2u:0u; return i; }
    public static uint CtrlAltSpace(){
        var a = new INPUT[]{ K(0x11,false),K(0x12,false),K(0x20,false),K(0x20,true),K(0x12,true),K(0x11,true) };
        return SendInput((uint)a.Length, a, Marshal.SizeOf(typeof(INPUT)));
    }
}
"@

# Notepad 起動・フォーカス
$np = Start-Process notepad -PassThru
Start-Sleep -Milliseconds 1800
Write-Host "Notepad PID: $($np.Id)"

# Ctrl+Alt+Space を low-level 送出 → アプリ召喚（直前=Notepad を記憶）
$sent = [Si]::CtrlAltSpace()
Write-Host "SendInput events: $sent"
Start-Sleep -Milliseconds 1000

# 子音入力・確定（共有）
winapp ui set-value "InputBox" "kyy" -a $appPid 2>$null | Out-Null
Start-Sleep -Milliseconds 800
$res = winapp ui search "共有" -a $appPid --json 2>$null | ConvertFrom-Json
$item = $res.matches | Where-Object { $_.type -eq 'ListItem' -and $_.name -match 'Surface = 共有,' } | Select-Object -First 1
if ($item) { winapp ui invoke $item.selector -a $appPid 2>$null | Out-Null; Write-Host "確定: 共有" }
else { Write-Host "確定候補が見つからない" }
Start-Sleep -Milliseconds 400

# 送信 → Notepad へ注入
winapp ui invoke "送信" -a $appPid 2>$null | Out-Null
Start-Sleep -Milliseconds 1200

# Notepad 本文確認（UIA）
$tree = winapp ui inspect -a $($np.Id) --interactive --json 2>$null | ConvertFrom-Json
$injected = $tree.elements | Where-Object { ($_.name -match '共有') -or ($_.value -match '共有') }
if ($injected) { Write-Host "RESULT: PASS - Notepad に『共有』が注入された" }
else { Write-Host "RESULT: FAIL - Notepad 本文に『共有』が見つからない（フォーカス制約の可能性）" }

winapp ui screenshot -a $($np.Id) -o "screenshots/notepad-injected.png" 2>$null | Out-Null
Stop-Process -Id $np.Id -Force -ErrorAction SilentlyContinue
