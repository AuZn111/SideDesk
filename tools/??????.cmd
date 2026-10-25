@echo off
setlocal
cd /d "%~dp0"

if exist "SideDesk.exe" (
  start "" /wait "SideDesk.exe" --restore-icons
  if not errorlevel 1 (
    echo Desktop icons restored.
    goto :done
  )
)

if exist "restore-desktop-icons.ps1" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "restore-desktop-icons.ps1"
) else (
  echo SideDesk.exe and restore-desktop-icons.ps1 were not found.
)

:done
echo.
pause
