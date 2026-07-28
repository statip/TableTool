@echo off
chcp 65001 >nul
cd /d "%~dp0"

REM 直接运行即可！工具自动扫 excel/ 下所有 xlsx，无需配置表结构。
REM 如果你要改默认输出路径，加上 -o 和 -n 参数:
REM   dotnet run --project src\TableTool.Cli -- build -o D:/MyGame/Config -n MyGame.Config %*
dotnet run --project src\TableTool.Cli -- build %*
echo.
pause
