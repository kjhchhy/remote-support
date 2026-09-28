$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testDir = Join-Path $repo 'work\tests'
New-Item -ItemType Directory -Force -Path $testDir | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'dist\RemoteSupportSetup.exe') -Destination $testDir -Force
$runner = Join-Path $testDir 'Tests.exe'
& $compiler /nologo /target:exe /utf8output "/out:$runner" "/reference:$testDir\RemoteSupportSetup.exe" /reference:System.Web.Extensions.dll "$PSScriptRoot\Tests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
& $runner
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
