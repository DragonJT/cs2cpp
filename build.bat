@echo off
dotnet run
if errorlevel 1 (
    echo ❌ Code generation failed.
    exit /b 1
)

echo 🛠 Launching MSVC compiler...
call build\run_msvc.bat