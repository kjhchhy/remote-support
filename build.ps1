$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x C# compiler is required.' }
$destination = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$exe = Join-Path $destination 'RemoteSupportSetup.exe'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output "/out:$exe" "/win32manifest:$PSScriptRoot\installer\app.manifest" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.ServiceProcess.dll "/resource:$PSScriptRoot\installer\app.manifest" "$PSScriptRoot\installer\Setup.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $destination 'SHA256SUMS.txt') -Value "$hash  RemoteSupportSetup.exe" -Encoding ascii
Write-Output "Built $exe"
