@echo off
echo ============================================================
echo   YAKUZA 6 LOCALIZATION TOOL - COMPILATION SCRIPT (v0.4.1)
echo ============================================================
echo.
echo Select target compilation mode:
echo   [1] Single Executable (Framework Dependent - requires .NET 10.0 installed, small size)
echo   [2] Single Executable (Self-Contained - includes .NET 10.0 runtime, large size)
echo.
set /p choice="Enter choice (1 or 2): "

if "%choice%"=="1" (
    echo.
    echo Compiling Framework-Dependent Single-File Executable...
    dotnet publish Console.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishReadyToRun=true -o ./publish
    goto done
)

if "%choice%"=="2" (
    echo.
    echo Compiling Self-Contained Single-File Executable...
    dotnet publish Console.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -o ./publish
    goto done
)

echo Invalid choice.
exit /b 1

:done
echo.
if %errorlevel% equ 0 (
    echo ============================================================
    echo   [OK] Compilation completed successfully!
    echo   Executable is located in: Tool\Console\publish\Yakuza6LocalizationTool.exe
    echo ============================================================
) else (
    echo ============================================================
    echo   [ERR] Compilation failed!
    echo ============================================================
)
pause
