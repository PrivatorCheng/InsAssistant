@echo off
setlocal EnableDelayedExpansion

set ROOT=%~dp0
set PID_FILE=%ROOT%server-pids.txt

if not exist "%PID_FILE%" (
  echo PID file not found: %PID_FILE%
  echo Nothing to stop.
  exit /b 0
)

for /f "tokens=1,2 delims==" %%a in ("%PID_FILE%") do (
  set NAME=%%a
  set PID=%%b

  if not "!PID!"=="" (
    echo !PID! | findstr /r "^[0-9][0-9]*$" >nul
    if !errorlevel! neq 0 (
      echo Skip !NAME!: invalid PID "!PID!"
    ) else (
      echo Stopping !NAME! with PID !PID! ...
      taskkill /PID !PID! /T /F >nul 2>&1
      if !errorlevel! equ 0 (
        echo !NAME! stopped.
      ) else (
        echo !NAME! was not running or could not be stopped.
      )
    )
  )
)

del "%PID_FILE%" >nul 2>&1

echo.
echo Stop command completed.

endlocal
