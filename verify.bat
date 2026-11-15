@echo off
rem Minden "Javitas" gombot tenylegesen lefuttat (rossz ertek -> javitas -> fuggetlen visszaolvasas), utana visszaallit mindent.
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:exe /platform:x64 /main:VerifyFixes /out:"%TEMP%\pas-verify.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll PerfectAdaptiveSync.cs VerifyFixes.cs
if errorlevel 1 (
  echo BUILD HIBA
  pause
  exit /b 1
)
"%TEMP%\pas-verify.exe"
set RESULT=%errorlevel%
pause
exit /b %RESULT%
