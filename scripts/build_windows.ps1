param(
    [string] $OutputDirectory = '',
    [string] $IconPath = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$nativeProject = Join-Path $projectRoot 'source\windows-native\MD Viewer.csproj'
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $projectRoot 'build\windows-native'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

$buildArguments = @('build', $nativeProject, '-c', 'Release', '-o', $OutputDirectory)
if ($IconPath) {
    $resolvedIcon = (Resolve-Path -LiteralPath $IconPath).Path
    $buildArguments += "-p:ApplicationIcon=$resolvedIcon"
}
& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) {
    throw "Windows build failed with exit code $LASTEXITCODE"
}
Write-Output (Join-Path $OutputDirectory 'MD Viewer.exe')
