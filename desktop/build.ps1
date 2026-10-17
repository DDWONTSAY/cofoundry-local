$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @('App.cs','TeamDesigner.cs','ProjectDetails.cs','Ui.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$references = @('/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Net.Http.dll','/reference:System.Web.Extensions.dll')
foreach ($edition in @(@{Name='共创团队';Define='/define:CHINESE'},@{Name='Cofoundry';Define='/define:ENGLISH'})) {
  & $compiler /nologo /target:winexe /platform:x64 /codepage:65001 $edition.Define "/out:$projectRoot\$($edition.Name).exe" "/resource:$PSScriptRoot\English.json,Cofoundry.English.json" $references $sources
  if ($LASTEXITCODE -ne 0) { throw 'Desktop compilation failed' }
}
Copy-Item -LiteralPath (Join-Path $projectRoot '共创团队.exe.config') -Destination (Join-Path $projectRoot 'Cofoundry.exe.config') -Force
$runtimeDirectory = Join-Path $projectRoot 'runtime'
New-Item -ItemType Directory -Path $runtimeDirectory -Force | Out-Null
$nodeSource = (Get-Command node -ErrorAction Stop).Source
Copy-Item -LiteralPath $nodeSource -Destination (Join-Path $runtimeDirectory 'node.exe') -Force
Write-Output 'Chinese and English desktop applications built.'
