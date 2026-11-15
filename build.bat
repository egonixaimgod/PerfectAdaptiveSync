@echo off
rem Lebuildeli a PerfectAdaptiveSync.exe-t a PerfectAdaptiveSync.cs-bol (Windows beepitett C# fordito, nem kell SDK), majd elinditja.
cd /d "%~dp0"
taskkill /im PerfectAdaptiveSync.exe /f >nul 2>&1
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ /win32icon:PerfectAdaptiveSync.ico /out:PerfectAdaptiveSync.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll PerfectAdaptiveSync.cs
if errorlevel 1 (
  echo.
  echo BUILD HIBA
  pause
  exit /b 1
)
echo Kesz: PerfectAdaptiveSync.exe
start "" PerfectAdaptiveSync.exe
