param(
    # The installed bridge. Defaults to where install-revit-2024.ps1 puts it.
    [string]$BridgePath = (Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2024\BuildingRegulationReview\McpBridge\BuildingRegulationReview.McpBridge.exe')
)

# Registers the stdio bridge with Claude Code for every project (user scope), replacing any older
# entry of the same name - including the HTTP one with a pasted token that the first MCP release
# told people to add, which stops working every time Revit restarts (docs/mcp-server.md section 3).

$ErrorActionPreference = 'Stop'
$serverName = 'building-regulation-review'
$projectRoot = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path -LiteralPath $BridgePath)) {
    throw "Bridge not found: $BridgePath. Run scripts\install-revit-2024.ps1 first."
}

$claude = Get-Command claude -CommandType Application -ErrorAction SilentlyContinue
if (-not $claude) {
    Write-Host 'Claude Code (claude) is not on PATH; register the bridge yourself with:'
    Write-Host "  claude mcp add --scope user $serverName -- `"$BridgePath`""
    return
}

# Old entries in any scope. The local scope belongs to a folder, so remove it from the repo root,
# where the first release's instructions had people add it. A missing entry is not an error.
foreach ($scope in 'local', 'project', 'user') {
    Push-Location $projectRoot
    try {
        & $claude.Source mcp remove $serverName --scope $scope 2>&1 | Out-Null
    }
    catch {
    }
    finally {
        Pop-Location
    }
}

& $claude.Source mcp add --scope user $serverName -- $BridgePath
if ($LASTEXITCODE -ne 0) {
    throw "claude mcp add failed (exit code $LASTEXITCODE)."
}
Write-Host "Registered $serverName for Claude Code (user scope): $BridgePath"
Write-Host 'Already-running Claude Code sessions pick it up after /mcp reconnect or a restart; new sessions connect by themselves.'
