@echo off
cd /d "%~dp0"

REM Auto-detect all xlsx files in excel/ folder. No config needed.
REM Custom output: build.bat -o ../../MyGame/Config -n MyGame.Config %*
dotnet run --project src\TableTool.Cli -- build %*
echo.
pause
