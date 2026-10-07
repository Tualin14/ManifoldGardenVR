param([ValidateSet('Debug','Release')][string]$Configuration='Release')
. "$PSScriptRoot/Common.ps1"
$game = Get-MGVRGameRoot
if(-not(Test-Path -LiteralPath (Join-Path $game 'MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll'))){throw 'Missing generated MelonLoader game references.'}
$env:DOTNET_CLI_HOME=Join-Path $MGVRRoot 'Artifacts/dotnet-cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
foreach($project in @('Mod/MGVR.csproj')) {
    & dotnet build (Join-Path $MGVRRoot $project) -c $Configuration --ignore-failed-sources -p:NuGetAudit=false -p:UseSharedCompilation=false
    if($LASTEXITCODE -ne 0){throw "Build failed: $project"}
}
$msbuild=Get-MGVRMSBuild
& $msbuild (Join-Path $MGVRRoot 'Native/MGVR.Native.vcxproj') /m /nologo /verbosity:minimal "/p:Configuration=$Configuration" /p:Platform=x64
if($LASTEXITCODE -ne 0){throw 'Native build failed.'}
Write-Output 'Build complete. Nothing has been deployed to the game.'
