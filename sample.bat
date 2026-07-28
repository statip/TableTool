@echo off
cd /d "%~dp0"

dotnet run --project src\TableTool.Cli -- sample
echo.
pause
