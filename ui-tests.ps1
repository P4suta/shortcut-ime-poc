param([int]$AppPid = 0)

# winapp は UTF-8 出力。pwsh 既定（CP932）だと日本語が化けるため UTF-8 に固定。
$OutputEncoding = [Console]::OutputEncoding = [Console]::InputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'

if ($AppPid -eq 0) {
    $proc = Get-Process ShortcutIme.App -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $proc) { Write-Host "App not running"; exit 2 }
    $AppPid = $proc.Id
}
Write-Host "Testing PID $AppPid"

$pass = 0; $fail = 0; $results = @()
function Test-UI {
    param([string]$Name, [scriptblock]$Script)
    try {
        $out = & $Script 2>&1
        if ($LASTEXITCODE -eq 0) { $script:pass++; $script:results += @{ name = $Name; status = "PASS" } }
        else { $script:fail++; $script:results += @{ name = $Name; status = "FAIL"; detail = "$out" } }
    } catch { $script:fail++; $script:results += @{ name = $Name; status = "FAIL"; detail = "$_" } }
}

# 1. 辞書＋連接行列のロード完了（36MB あるので最大 120 秒）
Test-UI "ロード完了(準備完了表示)" { winapp ui wait-for "StatusBlock" -a $AppPid --value "準備完了" --contains -t 120000 }

# 2. 文の読みを一括入力 → 一括変換プレビュー
Test-UI "InputBox 存在" { winapp ui wait-for "InputBox" -a $AppPid -t 5000 }
Test-UI "文を入力" { winapp ui set-value "InputBox" "kyouhaarigatougozaimasita" -a $AppPid }
Start-Sleep -Milliseconds 1500
Test-UI "一括変換＝今日はありがとうございました" {
    winapp ui wait-for "PreviewBlock" -a $AppPid --value "今日はありがとうございました" --contains -t 5000
}

New-Item -ItemType Directory -Force -Path "screenshots" | Out-Null
winapp ui screenshot -a $AppPid -o "screenshots/phrase.png" 2>$null

Write-Host "`nPassed: $pass | Failed: $fail"
$results | Where-Object { $_.status -eq "FAIL" } | ForEach-Object { Write-Host "  FAIL: $($_.name) - $($_.detail)" -ForegroundColor Red }
if ($fail -gt 0) { exit 1 } else { exit 0 }
