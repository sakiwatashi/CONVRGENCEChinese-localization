@echo off
rem ASCII only on purpose: cmd.exe parses .bat files with the ANSI codepage,
rem so non-ASCII text here corrupts the parser. All messages live in the .ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Toggle-Chinese.ps1"
