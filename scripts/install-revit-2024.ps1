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

# redeploy.bat starts a bare PowerShell with -NoProfile, so whatever put dotnet on the
# interactive PATH is not there. Find the SDK where it actually lives instead of failing.
function Resolve-Dotnet {
    $onPath = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
    if ($onPath) {
        return $onPath.Source
    }

    $candidates = @()
    if ($env:DOTNET_ROOT) {
        $candidates += (Join-Path $env:DOTNET_ROOT 'dotnet.exe')
    }
    $candidates += (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe')
    if (${env:ProgramFiles(x86)}) {
        $candidates += (Join-Path ${env:ProgramFiles(x86)} 'dotnet\dotnet.exe')
    }
    $candidates += (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            Write-Host "dotnet is not on PATH; using $candidate"
            return $candidate
        }
    }

    throw 'Could not find dotnet. Install the .NET SDK, or set DOTNET_ROOT to the folder holding dotnet.exe, then run this installer again.'
}

if (-not $SkipBuild) {
    $dotnet = Resolve-Dotnet
    & $dotnet build $projectFile -c Release
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
