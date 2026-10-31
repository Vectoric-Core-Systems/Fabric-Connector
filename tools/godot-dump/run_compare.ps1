# Convert a Godot project with the Fabric Connector and check the result against Godot itself (tools/godot-dump/README.md).
#   run_compare.ps1 -Godot <godot exe> -Project <godot project dir> -AssetC <AverAssetC> -Compare <FabricGodotCompareTest> [-Work <dir>]
# Godot only ever runs --headless, on a copy of the project in the work dir (default: a folder under the temp folder).
param(
    [Parameter(Mandatory = $true)][string]$Godot,
    [Parameter(Mandatory = $true)][string]$Project,
    [Parameter(Mandatory = $true)][string]$AssetC,
    [Parameter(Mandatory = $true)][string]$Compare,
    [string]$Work = (Join-Path ([System.IO.Path]::GetTempPath()) 'fabric-godot-compare')
)
$ErrorActionPreference = 'Stop'
if (Test-Path $Work) { Remove-Item -Recurse -Force $Work }
New-Item -ItemType Directory -Force -Path $Work | Out-Null
& $AssetC fabric $Project (Join-Path $Work 'out') --overwrite
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$converted = (Get-ChildItem (Join-Path $Work 'out') -Directory | Select-Object -First 1).FullName
& $Compare compare --converted $converted --godot $Godot --godot-project $Project --work (Join-Path $Work 'godot-project')
exit $LASTEXITCODE
