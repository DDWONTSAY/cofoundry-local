@echo off
setlocal
cd /d "%~dp0.."
where node >nul 2>nul
if errorlevel 1 (
  echo Install Node.js 22 or newer and add it to PATH first.
  exit /b 1
)
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /codepage:65001 /out:"共创团队.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll desktop\App.cs
if errorlevel 1 exit /b 1
if not exist runtime mkdir runtime
for /f "delims=" %%N in ('where node') do if not exist runtime\node.exe copy /y "%%N" runtime\node.exe >nul
echo Desktop build completed.
