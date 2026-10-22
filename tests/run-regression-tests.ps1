[CmdletBinding()]
param(
    [string]$GameDir = 'C:/Program Files (x86)/Steam/steamapps/common/RimWorld',
    [string]$JecsDir = 'C:/Program Files (x86)/Steam/steamapps/workshop/content/294100/3312959701/1.7/Assemblies',
    [string]$HarmonyDir = 'C:/Program Files (x86)/Steam/steamapps/workshop/content/294100/2009463077/1.7/Assemblies',
    [string]$ModAssembly,
    [string]$ArtifactsDir = (Join-Path ([IO.Path]::GetTempPath()) ('rwom-regression-' + [Guid]::NewGuid().ToString('N'))),
    [string]$Filter = ''
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$refs = [IO.Path]::GetFullPath((Join-Path $repo '../refs/out'))
$ArtifactsDir = [IO.Path]::GetFullPath($ArtifactsDir)
if ($ArtifactsDir.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'ArtifactsDir must be outside the repository so builds cannot change tracked assemblies or source inputs.'
}
foreach ($path in @((Join-Path $GameDir 'Assembly-CSharp.dll'), (Join-Path $GameDir 'UnityStubs.dll'), (Join-Path $JecsDir 'AbilityUser.dll'))) {
    if (!(Test-Path $path -PathType Leaf)) { throw "Required runtime dependency missing: $path" }
}
New-Item -ItemType Directory -Force $ArtifactsDir | Out-Null

# No downloads: production restore reads only packages already in the local NuGet cache.
# Both output and intermediate directories are outside the source tree; never build net48.
if (!$ModAssembly) {
    $project = Join-Path $repo 'RimWorldOfMagic/RimWorldOfMagic/RimWorldOfMagic.csproj'
    $productionArgs = @("-p:OutputPath=$ArtifactsDir/production/out/", "-p:BaseIntermediateOutputPath=$ArtifactsDir/production/obj/", '-p:NuGetAudit=false')
    $packageCache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget/packages' }
    & dotnet restore $project --source $packageCache @productionArgs 2>&1 | Tee-Object -FilePath (Join-Path $ArtifactsDir 'production-restore.log')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & dotnet build $project --no-restore -c Release @productionArgs 2>&1 | Tee-Object -FilePath (Join-Path $ArtifactsDir 'production-build.log')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $ModAssembly = Join-Path $ArtifactsDir 'production/out/TorannMagic.dll'
}
$ModAssembly = [IO.Path]::GetFullPath($ModAssembly)
if (!(Test-Path $ModAssembly -PathType Leaf)) { throw "Production DLL missing: $ModAssembly" }

$harness = Join-Path $PSScriptRoot 'RWoM.RegressionTests/RWoM.RegressionTests.csproj'
& dotnet build $harness -c Release "-p:OutputPath=$ArtifactsDir/harness/out/" "-p:BaseIntermediateOutputPath=$ArtifactsDir/harness/obj/" "-p:ModAssemblyPath=$ModAssembly" "-p:GameDir=$GameDir" "-p:JecsDir=$JecsDir" 2>&1 | Tee-Object -FilePath (Join-Path $ArtifactsDir 'harness-build.log')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$testArgs = @('--repo', $repo, '--game', $GameDir, '--jecs', $JecsDir, '--mod', $ModAssembly, '--work', (Join-Path $ArtifactsDir 'cases'), '--refs', $refs, '--harmony', $HarmonyDir)
if ($Filter) { $testArgs += @('--filter', $Filter) }
& dotnet (Join-Path $ArtifactsDir 'harness/out/RWoM.RegressionTests.dll') @testArgs 2>&1 | Tee-Object -FilePath (Join-Path $ArtifactsDir 'test-results.log')
$testExit = $LASTEXITCODE
Write-Host "Regression exit code: $testExit; logs and Scribe XML: $ArtifactsDir"
exit $testExit
