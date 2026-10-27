param(
    [switch]$SkipZip
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSCommandPath
$project = Join-Path $root 'src\SideDesk\SideDesk.csproj'
$dist = Join-Path $root 'dist'
$tools = Join-Path $root 'tools'

Write-Host 'Publishing SideDesk (self-contained, single file)...' -ForegroundColor Cyan
if (Test-Path -LiteralPath $dist) {
    Remove-Item -LiteralPath $dist -Recurse -Force
}

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $dist

Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $tools 'restore-desktop-icons.ps1') -Destination $dist -Force

Get-ChildItem -LiteralPath $tools -Filter '*.cmd' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $dist -Force
}

Get-ChildItem -LiteralPath $dist -Filter '*.pdb' -ErrorAction SilentlyContinue | Remove-Item -Force

if (-not $SkipZip) {
    $zip = Join-Path $root 'SideDesk-1.4.1-win-x64.zip'
    if (Test-Path -LiteralPath $zip) {
        Remove-Item -LiteralPath $zip -Force
    }

    Write-Host 'Creating portable zip...' -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "Zip: $zip" -ForegroundColor Green
}

Write-Host "Done: $dist" -ForegroundColor Green
