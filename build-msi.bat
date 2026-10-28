@echo off
echo ============================================================
echo   Building Nusamba NodeCore Agent v3.0.0 (x64 and ARM64)
echo ============================================================

where dotnet >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [ERROR] .NET 8 SDK not found in PATH! Silakan install .NET 8 SDK.
    pause
    exit /b 1
)

where wix >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [INFO] WiX Toolset not found. Installing WiX v4 tool...
    dotnet tool install --global wix
)

echo.
echo [1/4] Publishing x64 Single-File Binary...
dotnet publish PCSpecAgent/PCSpecAgent.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Failed to publish win-x64 binary!
    pause
    exit /b 1
)

echo.
echo [2/4] Building NusambaNodeCore-Setup.msi (x64)...
wix build -arch x64 Package.wxs -o NusambaNodeCore-Setup.msi
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Failed to build x64 MSI!
    pause
    exit /b 1
)

echo.
echo [3/4] Publishing ARM64 Single-File Binary...
dotnet publish PCSpecAgent/PCSpecAgent.csproj -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true -o publish-arm64
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Failed to publish win-arm64 binary!
    pause
    exit /b 1
)

echo.
echo [4/4] Building NusambaNodeCore-Setup-arm64.msi...
wix build -arch arm64 Package.arm64.wxs -o NusambaNodeCore-Setup-arm64.msi
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Failed to build arm64 MSI!
    pause
    exit /b 1
)

echo.
echo ============================================================
echo   BUILD SUCCEEDED!
echo   - NusambaNodeCore-Setup.msi (x64)
echo   - NusambaNodeCore-Setup-arm64.msi (ARM64)
echo ============================================================
pause
