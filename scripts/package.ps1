param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',

    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$srcDir = Join-Path $repoRoot 'src'
$appProj = Join-Path $srcDir 'WindowController.App\WindowController.App.csproj'

if (-not (Test-Path $appProj)) {
    throw "Project not found: $appProj"
}

# Read version from csproj
[xml]$xml = Get-Content $appProj -Raw
$versionNode = $xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
$version = if ($null -eq $versionNode) {
    '0.0.0'
}
elseif ($versionNode -is [string]) {
    $versionNode.Trim()
}
elseif ($versionNode.PSObject.Properties.Name -contains '#text') {
    ($versionNode.'#text' ?? '').Trim()
}
else {
    $versionNode.ToString().Trim()
}

$distDir = Join-Path $repoRoot 'dist'
$publishDir = Join-Path $distDir 'publish'
$stageDir = Join-Path $distDir 'stage'

$zipName = "WindowController-v$version-$Runtime.zip"
$zipPath = Join-Path $distDir $zipName

New-Item -ItemType Directory -Force -Path $distDir | Out-Null

# Clean previous outputs
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Write-Host "Publishing..." -ForegroundColor Cyan
Push-Location $srcDir
try {
    dotnet publish 'WindowController.App' -c $Configuration -r $Runtime -o $publishDir
}
finally {
    Pop-Location
}

$exePath = Join-Path $publishDir 'WindowController.exe'
if (-not (Test-Path $exePath)) {
    throw "Publish output not found: $exePath"
}

New-Item -ItemType Directory -Force -Path $stageDir | Out-Null
Copy-Item $exePath (Join-Path $stageDir 'WindowController.exe') -Force

Write-Host "Zipping..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $zipPath -Force

# Clean stage (keep publish for troubleshooting)
Remove-Item $stageDir -Recurse -Force

Write-Host "Done" -ForegroundColor Green
Write-Host "EXE : $exePath"
Write-Host "ZIP : $zipPath"
