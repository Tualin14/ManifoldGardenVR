param(
    [string]$AssemblyPath = 'Artifacts/Build/MGVR/Release/net6.0/MGVR.dll',
    [string]$Report
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Common.ps1"
[void][Reflection.Assembly]::LoadFrom((Get-MGVRCecilPath))
$assemblyFile = (Resolve-Path -LiteralPath $AssemblyPath).Path
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($assemblyFile)
try {
    [xml]$project = Get-Content -LiteralPath (Join-Path $MGVRRoot 'Mod/MGVR.csproj') -Raw
    $version = [string]$project.Project.PropertyGroup.Version
    $info = @($assembly.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'MelonInfoAttribute' })
    if ($info.Count -ne 1 -or [string]$info[0].ConstructorArguments[1].Value -ne 'Manifold Garden VR' -or
        [string]$info[0].ConstructorArguments[2].Value -ne $version) {
        throw 'The plugin identity does not match the release project.'
    }
    if ($assembly.Name.Version.ToString() -ne "$version.0") { throw 'Incorrect assembly version.' }
    $resources = @(
        foreach ($entry in $project.Project.ItemGroup.EmbeddedResource) {
            if ($null -eq $entry) { continue }
            $resource = @($assembly.MainModule.Resources | Where-Object Name -eq $entry.LogicalName)
            if ($resource.Count -ne 1) { throw "Missing embedded resource: $($entry.LogicalName)" }
            $source = Join-Path $MGVRRoot ('Mod/' + $entry.Include)
            $expected = (Get-FileHash -LiteralPath $source).Hash
            $bytes = $resource[0].GetResourceData()
            $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
            if ($actual -ne $expected) { throw "Embedded resource differs from source: $source" }
            [pscustomobject]@{Name=[string]$entry.LogicalName;Bytes=$bytes.Length;SHA256=$actual}
        }
    )
    if ($resources.Count -ne $assembly.MainModule.Resources.Count) { throw 'Unexpected embedded resources.' }
    $result = [ordered]@{
        Version=$version
        AssemblyVersion=$assembly.Name.Version.ToString()
        SHA256=(Get-FileHash -LiteralPath $assemblyFile).Hash
        EmbeddedResources=$resources
    }
    if ($Report) {
        $reportFile = [IO.Path]::GetFullPath($Report)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reportFile)) | Out-Null
        $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportFile -Encoding utf8
    }
    Write-Output "Verified MGVR $version and $($resources.Count) embedded resources."
} finally {
    $assembly.Dispose()
}
