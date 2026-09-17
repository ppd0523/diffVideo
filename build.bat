@echo off
setlocal

set "PROJECT_ROOT=%~dp0"
set "DOTNET_EXE="

if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    set "DOTNET_EXE=%USERPROFILE%\.dotnet\dotnet.exe"
) else (
    where dotnet.exe >nul 2>nul
    if not errorlevel 1 set "DOTNET_EXE=dotnet.exe"
)

if not defined DOTNET_EXE (
    echo [ERROR] .NET SDK 10.0.400 or later was not found.
    echo Install the required SDK and run build.bat again.
    exit /b 1
)

echo Building DiffVideo portable package...
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%PROJECT_ROOT%build\Publish-Portable.ps1" -DotnetPath "%DOTNET_EXE%" %*
set "BUILD_EXIT_CODE=%ERRORLEVEL%"

if not "%BUILD_EXIT_CODE%"=="0" (
    echo [ERROR] Build failed with exit code %BUILD_EXIT_CODE%.
    exit /b %BUILD_EXIT_CODE%
)

echo.
echo Build completed. Output files are in:
echo   %PROJECT_ROOT%artifacts
exit /b 0
