@echo off
rem Leforditja es lefuttatja az ontesztet (SelfTest.cs) a program forrasaval egyutt. A gepen semmit nem modosit.
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:exe /platform:x64 /main:SelfTest /out:"%TEMP%\pas-selftest.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll PerfectAdaptiveSync.cs SelfTest.cs
if errorlevel 1 (
  echo BUILD HIBA
  pause
  exit /b 1
)
"%TEMP%\pas-selftest.exe"
set RESULT=%errorlevel%
if /i not "%~1"=="nopause" pause
exit /b %RESULT%
