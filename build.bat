@echo off
chcp 65001 >nul
cd /d "%~dp0"

REM 如果你要改默认输出路径，在下面加上 -o 和 -n 参数
REM 例如: dotnet run --project src\TableTool.Cli -- build -o D:/MyGame/Config -n MyGame.Config %*
dotnet run --project src\TableTool.Cli -- build %*
echo.
pause
