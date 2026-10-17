@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo 共创团队：http://127.0.0.1:4317
echo 请在浏览器打开上述地址，关闭此窗口将停止平台。
set "COFOUNDRY_NODE=node"
where node >nul 2>nul
if errorlevel 1 set "COFOUNDRY_NODE=%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe"
"%COFOUNDRY_NODE%" server.mjs
pause
