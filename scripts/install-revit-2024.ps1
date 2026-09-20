param(
    [switch]$SkipBuild,
    [switch]$WaitForRevit,
    [int]$WaitTimeoutSeconds = 1200
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'src\BuildingRegulationReview\BuildingRegulationReview.csproj'
$outputDirectory = Join-Path $projectRoot 'src\BuildingRegulationReview\bin\Release\net48'
$installDirectory = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2024\BuildingRegulationReview'
$manifestPath = Join-Path (Split-Path -Parent $installDirectory) 'BuildingRegulationReview.addin'

$runningRevit = Get-Process -Name Revit -ErrorAction SilentlyContinue
if ($runningRevit) {
    if (-not $WaitForRevit) {
        throw 'Revit is running and has locked BuildingRegulationReview.dll. Save the model, close every Revit window, then run this installer again.'
    }
    Write-Host 'Revit is running. Save your model and close every Revit window - deployment will start automatically once it exits.'
    $elapsed = 0
    while (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
        if ($elapsed -ge $WaitTimeoutSeconds) {
            throw "Gave up waiting for Revit to close after $WaitTimeoutSeconds seconds."
        }
        Start-Sleep -Seconds 5
        $elapsed += 5
        Write-Host "Still waiting for Revit to close... (${elapsed}s)"
    }
    Write-Host 'Revit closed. Continuing deployment.'
}

if (-not $SkipBuild) {
    dotnet build $projectFile -c Release
    if ($LASTEXITCODE -ne 0) {
        throw 'Build failed.'
    }
}

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
$assemblies = @(
    'BuildingRegulationReview',
    'BuildingRegulationReview.Application',
    'BuildingRegulationReview.Domain',
    'BuildingRegulationReview.Revit'
)
foreach ($assembly in $assemblies) {
    $dll = Join-Path $outputDirectory "$assembly.dll"
    if (-not (Test-Path -LiteralPath $dll)) {
        throw "Missing build output: $dll"
    }
    Copy-Item -LiteralPath $dll -Destination $installDirectory -Force
    $pdb = Join-Path $outputDirectory "$assembly.pdb"
    if (Test-Path -LiteralPath $pdb) {
        Copy-Item -LiteralPath $pdb -Destination $installDirectory -Force
    }
}
Copy-Item (Join-Path $outputDirectory 'Data') $installDirectory -Recurse -Force

$assemblyPath = Join-Path $installDirectory 'BuildingRegulationReview.dll'
$manifestTemplate = Get-Content (Join-Path $projectRoot 'BuildingRegulationReview.addin') -Raw
$manifestTemplate.Replace('__ASSEMBLY_PATH__', $assemblyPath) |
    Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host "Installed Revit 2024 add-in: $manifestPath"
Write-Host 'Restart Revit 2024 to load the add-in.'
