$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Installation integration tests are restricted to disposable GitHub Actions runners.' }
$repo = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testDir = Join-Path $repo 'work\integration'
New-Item -ItemType Directory -Force -Path $testDir | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'dist\RemoteSupportSetup.exe') -Destination $testDir -Force
$runner = Join-Path $testDir 'Integration.exe'
& $compiler /nologo /target:exe /utf8output "/out:$runner" "/reference:$testDir\RemoteSupportSetup.exe" /reference:System.Web.Extensions.dll /reference:System.ServiceProcess.dll "$PSScriptRoot\Integration.cs"
if ($LASTEXITCODE -ne 0) { throw 'Integration test build failed.' }
& $runner
if ($LASTEXITCODE -ne 0) { throw 'Integration tests failed.' }
