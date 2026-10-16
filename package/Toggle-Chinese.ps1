# CONVRGENCE 繁體中文化開關
# 把翻譯資料夾在 zh-TW <-> zh-TW.off 之間改名，藉此開關漢化。
# 檔案本身不會被刪除，隨時可以切回來。
#
# 注意：關掉中文後玩過一次，翻譯外掛會自己建一個「空的」zh-TW 資料夾
# （只有 _Substitutions.txt 之類的空檔）。所以判斷開關狀態要看哪邊有
# convrgence*.txt，切換時那個空資料夾直接移除。

$base = Join-Path $PSScriptRoot 'BepInEx\Translation'
$on   = Join-Path $base 'zh-TW'
$off  = Join-Path $base 'zh-TW.off'

function Test-Translations($dir) {
    $text = Join-Path $dir 'Text'
    return (Test-Path $text) -and @(Get-ChildItem $text -Filter 'convrgence*.txt' -ErrorAction SilentlyContinue).Count -gt 0
}

function Clear-Stub($dir) {
    # 只刪沒有翻譯檔的空殼；真的有翻譯就改名備份，絕不刪
    if (-not (Test-Path $dir)) { return }
    if (Test-Translations $dir) {
        Rename-Item -LiteralPath $dir -NewName ((Split-Path $dir -Leaf) + '.bak-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    } else {
        Remove-Item -LiteralPath $dir -Recurse -Force
    }
}

if (Get-Process -Name CONVRGENCE -ErrorAction SilentlyContinue) {
    Write-Host '[!] 遊戲正在執行中，請先完全關閉再切換。' -ForegroundColor Yellow
    Read-Host '按 Enter 結束'
    exit 1
}

if (Test-Translations $on) {
    Clear-Stub $off
    Rename-Item -LiteralPath $on -NewName 'zh-TW.off'
    Write-Host '漢化已【關閉】—— 遊戲會顯示英文原文。' -ForegroundColor Cyan
    $dir = $off
}
elseif (Test-Translations $off) {
    Clear-Stub $on
    Rename-Item -LiteralPath $off -NewName 'zh-TW'
    Write-Host '漢化已【開啟】—— 遊戲會顯示繁體中文。' -ForegroundColor Green
    $dir = $on
}
else {
    Write-Host '[!] 找不到翻譯檔，這兩個位置都沒有 convrgence*.txt：' -ForegroundColor Red
    Write-Host "    $on"
    Write-Host "    $off"
    Write-Host '請重新執行安裝程式。'
    Read-Host '按 Enter 結束'
    exit 1
}

$n = @(Get-ChildItem (Join-Path $dir 'Text') -Filter 'convrgence*.txt').Count
Write-Host "（翻譯檔 $n 個，仍保留在 $dir）"
Write-Host ''
Write-Host '請從 Steam 重新啟動 CONVRGENCE 讓變更生效。'
Read-Host '按 Enter 結束'
