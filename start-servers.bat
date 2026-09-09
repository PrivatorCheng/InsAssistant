@echo off
setlocal EnableDelayedExpansion

set ROOT=%~dp0
set PID_FILE=%ROOT%server-pids.txt

if exist "%PID_FILE%" del "%PID_FILE%"

echo Starting API server...
for /f %%i in ('powershell -NoProfile -ExecutionPolicy Bypass -Command "(Start-Process dotnet -ArgumentList @('run','--project','API/API.csproj','--launch-profile','https') -WorkingDirectory '%ROOT%' -PassThru | Select-Object -ExpandProperty Id)"') do set API_PID=%%i

echo Starting Web server...
for /f %%i in ('powershell -NoProfile -ExecutionPolicy Bypass -Command "(Start-Process dotnet -ArgumentList @('run','--project','Web/Web.csproj','--launch-profile','https') -WorkingDirectory '%ROOT%' -PassThru | Select-Object -ExpandProperty Id)"') do set WEB_PID=%%i

if not defined API_PID (
	echo Failed to start API process.
	exit /b 1
)

if not defined WEB_PID (
	echo Failed to start Web process.
	exit /b 1
)

echo API_PID=%API_PID%>"%PID_FILE%"
echo WEB_PID=%WEB_PID%>>"%PID_FILE%"

echo Waiting for servers to become ready...
set API_READY=
set WEB_READY=
for /l %%n in (1,1,30) do (
	if not defined API_READY (
		for /f %%x in ('netstat -ano ^| findstr ":5189"') do set API_READY=1
	)
	if not defined WEB_READY (
		for /f %%x in ('netstat -ano ^| findstr ":5219"') do set WEB_READY=1
	)

	if defined API_READY if defined WEB_READY goto :READY
	timeout /t 1 >nul
)

:READY

echo.
echo Servers started successfully.
echo API PID: %API_PID%
echo Web PID: %WEB_PID%
echo PID file: %PID_FILE%
if defined API_READY (
	echo API is listening on http://localhost:5189
) else (
	echo WARNING: API not detected on http://localhost:5189
)
if defined WEB_READY (
	echo Web is listening on http://localhost:5219
) else (
	echo WARNING: Web not detected on http://localhost:5219
)
echo.
echo Use stop-servers.bat to stop both servers.

endlocal
