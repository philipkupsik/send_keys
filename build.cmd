@echo off
rem Baut ClipboardTyper.exe mit dem in Windows enthaltenen C#-Compiler (.NET Framework 4.x).
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo .NET Framework 4.x C#-Compiler nicht gefunden.
    exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize+ /out:"%~dp0ClipboardTyper.exe" ^
    /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll "%~dp0src\*.cs"
if errorlevel 1 exit /b 1
echo Erstellt: %~dp0ClipboardTyper.exe
