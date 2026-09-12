param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'src\BuildingRegulationReview\BuildingRegulationReview.csproj'
$outputDirectory = Join-Path $projectRoot 'src\BuildingRegulationReview\bin\Release\net48'
$installDirectory = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2024\BuildingRegulationReview'
$manifestPath = Join-Path (Split-Path -Parent $installDirectory) 'BuildingRegulationReview.addin'

$runningRevit = Get-Process -Name Revit -ErrorAction SilentlyContinue
if ($runningRevit) {
    throw 'Revit is running and has locked BuildingRegulationReview.dll. Save the model, close every Revit window, then run this installer again.'
}

if (-not $SkipBuild) {
    dotnet build $projectFile -c Release
    if ($LASTEXITCODE -ne 0) {
        throw 'Build failed.'
    }
}

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
Copy-Item (Join-Path $outputDirectory 'BuildingRegulationReview.dll') $installDirectory -Force
Copy-Item (Join-Path $outputDirectory 'BuildingRegulationReview.pdb') $installDirectory -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $outputDirectory 'Data') $installDirectory -Recurse -Force

$assemblyPath = Join-Path $installDirectory 'BuildingRegulationReview.dll'
$manifestTemplate = Get-Content (Join-Path $projectRoot 'BuildingRegulationReview.addin') -Raw
$manifestTemplate.Replace('__ASSEMBLY_PATH__', $assemblyPath) |
    Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host "Installed Revit 2024 add-in: $manifestPath"
Write-Host 'Restart Revit 2024 to load the add-in.'
