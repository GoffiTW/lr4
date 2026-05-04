# tools/GenerateDocs.ps1
Set-Location $PSScriptRoot\..
if (-not (Get-Command docfx -ErrorAction SilentlyContinue)) {
    dotnet tool install -g docfx
}
docfx docs\docfx.json --serve
Write-Host "Документация сгенерирована в docs\_site"