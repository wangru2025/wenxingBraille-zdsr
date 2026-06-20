$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$distDir = Join-Path $root 'dist'
$appDir = Join-Path $distDir 'app'
$zdsrDir = ${env:ZDSR_DIR}
if ([String]::IsNullOrWhiteSpace($zdsrDir)) {
	$zdsrDir = 'C:\Program Files (x86)\zdsr\zdsr'
}

$zdsrApi = Join-Path $zdsrDir 'ZDSRBrailleDisplayAddin.dll'
if (-not (Test-Path -LiteralPath $zdsrApi)) {
	$zdsrApi = Join-Path $root 'build\ZDSRBrailleDisplayAddin.dll'
}
if (-not (Test-Path -LiteralPath $zdsrApi)) {
	throw "ZDSRBrailleDisplayAddin.dll was not found. Set ZDSR_DIR to your ZDSR installation directory, or run build-zdsr-api-stub.ps1 for CI builds."
}

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
	throw 'The .NET Framework C# compiler was not found.'
}

if (Test-Path -LiteralPath $appDir) {
	Remove-Item -LiteralPath $appDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $appDir | Out-Null

$output = Join-Path $appDir 'wenxingBraille.dll'
& $csc /nologo /target:library /platform:x86 /optimize+ /debug:pdbonly `
	/reference:System.dll `
	/reference:System.Core.dll `
	/reference:$zdsrApi `
	/out:$output `
	(Join-Path $root 'WenxingBrailleZdsrAddin.cs') `
	(Join-Path $root 'Properties\AssemblyInfo.cs')

if ($LASTEXITCODE -ne 0) {
	throw 'C# compilation failed.'
}

Write-Host "Built $output"
