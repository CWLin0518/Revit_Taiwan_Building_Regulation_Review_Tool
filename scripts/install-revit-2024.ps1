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
# Copy the CONTENTS of Data, not the folder. `Copy-Item <dir> <dest> -Recurse -Force` only forces the
# destination path itself: once $installDirectory\Data exists, the files inside it are never
# overwritten again. That silently pinned the deployed fire-review-rules.json at whatever version was
# there when the folder was first created, so every rule change since then reached the tests and the
# build output but not Revit. The assemblies above were unaffected because they are copied file by
# file. Copying each file explicitly, and reporting what landed, keeps that from going unnoticed
# again.
$dataSource = Join-Path $outputDirectory 'Data'
if (-not (Test-Path -LiteralPath $dataSource)) {
    throw "Missing build output: $dataSource"
}
$dataTarget = Join-Path $installDirectory 'Data'
New-Item -ItemType Directory -Path $dataTarget -Force | Out-Null
$dataFiles = Get-ChildItem -LiteralPath $dataSource -File -Recurse
if ($dataFiles.Count -eq 0) {
    throw "No data files found under $dataSource"
}
foreach ($file in $dataFiles) {
    $relative = $file.FullName.Substring($dataSource.Length).TrimStart('\', '/')
    $destination = Join-Path $dataTarget $relative
    $destinationParent = Split-Path -Parent $destination
    if (-not (Test-Path -LiteralPath $destinationParent)) {
        New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
    }
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
}

$assemblyPath = Join-Path $installDirectory 'BuildingRegulationReview.dll'
$manifestTemplate = Get-Content (Join-Path $projectRoot 'BuildingRegulationReview.addin') -Raw
$manifestTemplate.Replace('__ASSEMBLY_PATH__', $assemblyPath) |
    Set-Content -LiteralPath $manifestPath -Encoding UTF8

# What actually landed. A deployment that silently kept an old file is the failure this script had,
# so it now says enough for the next person to see it at a glance.
Write-Host ''
Write-Host 'Deployed files:'
Get-ChildItem -LiteralPath $installDirectory -File -Recurse |
    Sort-Object FullName |
    ForEach-Object {
        Write-Host ('  {0,-48} {1,8:N0} bytes  {2:yyyy-MM-dd HH:mm:ss}' -f
            $_.FullName.Substring($installDirectory.Length).TrimStart('\'), $_.Length, $_.LastWriteTime)
    }

Write-Host ''
Write-Host "Installed Revit 2024 add-in: $manifestPath"
Write-Host 'Restart Revit 2024 to load the add-in.'
