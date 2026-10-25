@echo off
rem Installiert ClipboardTyper als Windows-Dienst (als Administrator ausfuehren).
setlocal
net session >nul 2>&1 || (echo Bitte als Administrator ausfuehren. & exit /b 1)

if not exist "%~dp0ClipboardTyper.exe" call "%~dp0build.cmd" || exit /b 1

set TARGET=%ProgramFiles%\ClipboardTyper
sc stop ClipboardTyper >nul 2>&1
timeout /t 2 /nobreak >nul
taskkill /f /im ClipboardTyper.exe >nul 2>&1
if not exist "%TARGET%" mkdir "%TARGET%"
copy /y "%~dp0ClipboardTyper.exe" "%TARGET%\" >nul || exit /b 1

sc query ClipboardTyper >nul 2>&1
if errorlevel 1 (
    sc create ClipboardTyper binPath= "\"%TARGET%\ClipboardTyper.exe\"" start= auto DisplayName= "ClipboardTyper" || exit /b 1
)
sc description ClipboardTyper "Strg+Alt+Umschalt+Einfg sendet die Zwischenablage als Tastatureingabe." >nul
sc failure ClipboardTyper reset= 86400 actions= restart/5000/restart/5000/restart/30000 >nul
sc start ClipboardTyper
