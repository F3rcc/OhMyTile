@echo off
chcp 65001 >nul
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" %*
if errorlevel 1 (
  echo.
  echo 安装失败，请查看上面的提示。
) else (
  echo.
  echo 完成。可从开始菜单搜索 OhMyTile 启动。
)
pause
