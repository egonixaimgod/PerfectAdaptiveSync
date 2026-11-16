@echo off
setlocal enabledelayedexpansion
rem Onteszt -> build -> git commit + push -> GitHub Release (build-N) az exe-vel. N = a commitok szama, nem kell kulon verziofajl.
cd /d "%~dp0"

echo ==========================================
echo    Perfect Adaptive Sync - Rebuild ^& Release
echo ==========================================
echo.

echo [0/4] Ellenorzesek...
git remote get-url origin >nul 2>&1
if errorlevel 1 (
  echo   [HIBA] Ez a mappa nem git repo, vagy nincs "origin" tavoli repo beallitva.
  pause
  exit /b 1
)
gh auth status >nul 2>&1
if errorlevel 1 (
  echo   [HIBA] A "gh" nincs telepitve vagy nincs bejelentkezve - javitas: gh auth login
  pause
  exit /b 1
)

echo [1/4] Onteszt...
call "%~dp0test.bat" nopause
if errorlevel 1 (
  echo.
  echo   [HIBA] Az onteszt nem ment at - nincs kiadas.
  pause
  exit /b 1
)

echo.
echo [2/4] Build...
call "%~dp0build.bat" nostart
if errorlevel 1 exit /b 1

echo.
echo [3/4] Feltoltes a GitHubra...
for /f %%N in ('git rev-list --count HEAD') do set /a BUILD=%%N
git add -A
echo A commitba kerulo fajlok:
git --no-pager diff --cached --stat
git diff --cached --quiet
if errorlevel 1 (
  set /a BUILD+=1
  git commit -q -m "Release: Build !BUILD! (Auto-Build)"
  if errorlevel 1 (
    echo   [HIBA] A commit nem sikerult.
    pause
    exit /b 1
  )
) else (
  echo   nincs uj valtozas - a meglevo allapot megy ki
)
git push
if errorlevel 1 (
  echo.
  echo   [HIBA] A git push nem sikerult - a kiadas felbeszakadt. A commit helyben megvan.
  pause
  exit /b 1
)

echo.
echo [4/4] GitHub Release: build-!BUILD!
gh release view "build-!BUILD!" >nul 2>&1
if errorlevel 1 (
  gh release create "build-!BUILD!" PerfectAdaptiveSync.exe --title "Perfect Adaptive Sync - build !BUILD!" --notes "Automatikus kiadas. Build !BUILD!."
) else (
  echo   A kiadas mar letezik - az exe felulirasa...
  gh release upload "build-!BUILD!" PerfectAdaptiveSync.exe --clobber
)

rem a verdikt a visszaolvasas, nem a gh visszateresi kodja
echo.
gh release view "build-!BUILD!" >nul 2>&1
if errorlevel 1 (
  echo ==========================================
  echo    Build !BUILD! PUSHOLVA, DE A KIADAS KIMARADT!
  echo    A fenti gh-kimenetben ott a pontos hibauzenet.
  echo ==========================================
) else (
  echo ==========================================
  echo    SIKERES KIADAS: build-!BUILD!
  echo ==========================================
)
pause
