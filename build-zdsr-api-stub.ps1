$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildDir = Join-Path $root 'build'
$source = Join-Path $buildDir 'ZDSRBrailleDisplayAddinStub.cs'
$output = Join-Path $buildDir 'ZDSRBrailleDisplayAddin.dll'

New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

@'
using System;
using System.Threading.Tasks;

namespace ZDSR.BrailleDisplayAddin
{
    public interface IBrailleDisplayAddin
    {
        string Name { get; }
        string Description { get; }
        int GetVersion();
        bool Initial(Action<int, int> setRowCell, Action<string> actionHandler, Action<int> routingKeyHandler);
        Task<bool> ConnectAsync();
        void Disconnect();
        void WriteCells(byte[] cells);
    }
}
'@ | Set-Content -LiteralPath $source -Encoding ascii

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
	throw 'The .NET Framework C# compiler was not found.'
}

& $csc /nologo /target:library /out:$output $source
if ($LASTEXITCODE -ne 0) {
	throw 'ZDSR API stub compilation failed.'
}

Write-Host "Built $output"
