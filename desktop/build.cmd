@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0.."
where node >nul 2>nul
if errorlevel 1 exit /b 1
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "REFS=/reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll"
set "SOURCES=desktop\App.cs desktop\TeamDesigner.cs desktop\ProjectDetails.cs desktop\Ui.cs"
"%CSC%" /nologo /target:winexe /platform:x64 /codepage:65001 /out:"共创团队.exe" /resource:desktop/English.json,Cofoundry.English.json %REFS% %SOURCES%
if errorlevel 1 exit /b 1
"%CSC%" /nologo /target:winexe /platform:x64 /codepage:65001 /define:ENGLISH /out:Cofoundry.exe /resource:desktop/English.json,Cofoundry.English.json %REFS% %SOURCES%
if errorlevel 1 exit /b 1
copy /y "共创团队.exe.config" Cofoundry.exe.config >nul
if not exist runtime mkdir runtime
for /f "delims=" %%N in ('where node') do if not exist runtime\node.exe copy /y "%%N" runtime\node.exe >nul
