param(
    [string]$CompilerPath
)

$ErrorActionPreference = 'Stop'
$helpRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$candidates = @(
    $CompilerPath,
    (Join-Path ${env:ProgramFiles(x86)} 'HTML Help Workshop\hhc.exe'),
    (Join-Path $env:ProgramFiles 'HTML Help Workshop\hhc.exe')
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

$compiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) {
    throw 'hhc.exe was not found. Install Microsoft HTML Help Workshop 1.3 or pass -CompilerPath.'
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('VisionFlowStudioHelp-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null

# HTML Help Workshop 1.3 predates UTF-8 project files. Keep the repository
# sources in UTF-8, but compile temporary CP936 copies so Chinese window titles,
# contents and index entries are preserved instead of becoming mojibake.
$utf8 = New-Object System.Text.UTF8Encoding($false)
$cp936 = [System.Text.Encoding]::GetEncoding(936)
foreach ($name in @('VisionFlowStudio.hhp', 'VisionFlowStudio.hhc', 'VisionFlowStudio.hhk')) {
    $text = [System.IO.File]::ReadAllText((Join-Path $helpRoot $name), $utf8)
    [System.IO.File]::WriteAllText((Join-Path $temporaryRoot $name), $text, $cp936)
}
foreach ($name in @('index.html', 'manual.html', 'style.css')) {
    Copy-Item -LiteralPath (Join-Path $helpRoot $name) -Destination (Join-Path $temporaryRoot $name)
}

Push-Location $temporaryRoot
try {
    & $compiler (Join-Path $temporaryRoot 'VisionFlowStudio.hhp')
    $compiled = Join-Path $temporaryRoot 'VisionFlowStudio.chm'
    if (-not (Test-Path -LiteralPath $compiled) -or (Get-Item -LiteralPath $compiled).Length -lt 1024) {
        throw 'HTML Help compiler did not generate a valid VisionFlowStudio.chm.'
    }
    $output = Join-Path $helpRoot 'VisionFlowStudio.chm'
    Copy-Item -LiteralPath $compiled -Destination $output -Force
    Write-Host "CHM generated: $output"
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
