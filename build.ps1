$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw '.NET Framework 4.8 (64 bits) não encontrado.'
}
$outputDirectory = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$output = Join-Path $outputDirectory 'BrilhoDosMonitores.exe'
& $compiler /nologo /target:winexe /platform:x64 "/win32icon:$PSScriptRoot\BrilhoDosMonitores.ico" "/out:$output" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "$PSScriptRoot\BrilhoDosMonitores.cs"
if ($LASTEXITCODE -ne 0) { throw "Falha na compilação: código $LASTEXITCODE" }
Write-Output "Gerado: $output"
