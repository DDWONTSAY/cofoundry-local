$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /codepage:65001 "/out:$projectRoot\共创团队.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll "$PSScriptRoot\App.cs"
if ($LASTEXITCODE -ne 0) { throw 'Desktop compilation failed' }
$runtimeDirectory = Join-Path $projectRoot 'runtime'
New-Item -ItemType Directory -Path $runtimeDirectory -Force | Out-Null
$nodeSource = (Get-Command node -ErrorAction Stop).Source
Copy-Item -LiteralPath $nodeSource -Destination (Join-Path $runtimeDirectory 'node.exe') -Force
Write-Output 'Windows desktop executable built, Node runtime included.'
