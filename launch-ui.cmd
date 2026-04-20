@echo off
setlocal
cd /d "%~dp0"

echo [1/3] Checking .NET SDK...
dotnet --list-sdks | findstr /b "8." >nul
if errorlevel 1 (
    echo.
    echo .NET 8 SDK was not found.
    echo Please install .NET 8 SDK first, then run this file again.
    echo Download: https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

echo [2/3] Building solution...
dotnet build .\GNSSR.Host.sln -c Debug -nologo
if errorlevel 1 (
    echo.
    echo Build failed. Please read the messages above.
    echo.
    pause
    exit /b 1
)

echo [3/3] Launching GNSSR Host UI...
start "" ".\src\GNSSR.Host.UI\bin\Debug\net8.0-windows\GNSSR.Host.UI.exe"
exit /b 0
