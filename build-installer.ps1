$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $root 'build.ps1')

$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if ([String]::IsNullOrWhiteSpace($iscc)) {
	throw 'ISCC.exe was not found. Install Inno Setup 6 and add it to PATH.'
}

& $iscc (Join-Path $root 'installer\wenxingBraille-zdsr.iss')
if ($LASTEXITCODE -ne 0) {
	throw 'Inno Setup compilation failed.'
}
