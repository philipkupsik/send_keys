@echo off
rem Entfernt den ClipboardTyper-Dienst (als Administrator ausfuehren).
net session >nul 2>&1 || (echo Bitte als Administrator ausfuehren. & exit /b 1)
sc stop ClipboardTyper >nul 2>&1
timeout /t 2 /nobreak >nul
taskkill /f /im ClipboardTyper.exe >nul 2>&1
sc delete ClipboardTyper
rd /s /q "%ProgramFiles%\ClipboardTyper" 2>nul
