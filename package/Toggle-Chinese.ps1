# CONVRGENCE 繁體中文化開關
# 把翻譯資料夾在 zh-TW <-> zh-TW.off 之間改名，藉此開關漢化。
# 檔案本身不會被刪除，隨時可以切回來。

$base = Join-Path $PSScriptRoot 'BepInEx\Translation'
$on   = Join-Path $base 'zh-TW'
$off  = Join-Path $base 'zh-TW.off'

if (Get-Process -Name CONVRGENCE -ErrorAction SilentlyContinue) {
    Write-Host '[!] 遊戲正在執行中，請先完全關閉再切換。' -ForegroundColor Yellow
    Read-Host '按 Enter 結束'
    exit 1
}

if (Test-Path $on) {
    Rename-Item -LiteralPath $on -NewName 'zh-TW.off'
    Write-Host '漢化已【關閉】—— 遊戲會顯示英文原文。' -ForegroundColor Cyan
}
elseif (Test-Path $off) {
    Rename-Item -LiteralPath $off -NewName 'zh-TW'
    Write-Host '漢化已【開啟】—— 遊戲會顯示繁體中文。' -ForegroundColor Green
}
else {
    Write-Host '[!] 找不到翻譯資料夾，這兩個位置都是空的：' -ForegroundColor Red
    Write-Host "    $on"
    Write-Host "    $off"
    Read-Host '按 Enter 結束'
    exit 1
}

$n = 0
$dir = if (Test-Path $on) { $on } else { $off }
$textDir = Join-Path $dir 'Text'
if (Test-Path $textDir) {
    $n = (Get-ChildItem $textDir -Filter 'convrgence*.txt').Count
}
Write-Host "（翻譯檔 $n 個，仍保留在 $dir）"
Write-Host ''
Write-Host '請從 Steam 重新啟動 CONVRGENCE 讓變更生效。'
Read-Host '按 Enter 結束'

