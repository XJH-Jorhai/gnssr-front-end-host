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

echo [2/3] Preparing local SDK workaround...
set "MSBuildEnableWorkloadResolver=false"

echo [3/4] Building solution...
dotnet build .\GNSSR.Host.sln -c Debug -nologo -m:1 -p:BuildInParallel=false
if errorlevel 1 (
    echo.
    echo Build failed. Please read the messages above.
    echo The current machine is using a temporary local workaround for a broken .NET workload resolver.
    echo.
    pause
    exit /b 1
)

echo [4/4] Launching GNSSR Host UI...
".\src\GNSSR.Host.UI\bin\Debug\net8.0-windows\GNSSR.Host.UI.exe"
if errorlevel 1 (
    echo.
    echo The application exited with an error code.
    echo If a startup error occurred, a dialog or startup-error.log should have appeared.
    echo.
    pause
    exit /b 1
)

exit /b 0
