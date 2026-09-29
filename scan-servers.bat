@echo off
setlocal

rem Ensure commands run from workspace root
pushd "%~dp0"

echo ========================================
echo Rebuild API with analyzers and code style
echo ========================================
dotnet build "API\API.csproj" /t:Rebuild /p:RunAnalyzersDuringBuild=true /p:EnforceCodeStyleInBuild=true
if errorlevel 1 (
    echo [ERROR] API rebuild failed.
    popd
    exit /b 1
)

echo.
echo ========================================
echo Rebuild Web with analyzers and code style
echo ========================================
dotnet build "Web\Web.csproj" /t:Rebuild /p:RunAnalyzersDuringBuild=true /p:EnforceCodeStyleInBuild=true
if errorlevel 1 (
    echo [ERROR] Web rebuild failed.
    popd
    exit /b 1
)

echo.
echo ========================================
echo Rebuild completed successfully.
echo ========================================

popd
exit /b 0
