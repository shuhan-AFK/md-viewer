$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$assetRoot = Join-Path $projectRoot 'assets'
$webRoots = @(
    (Join-Path $projectRoot 'source\windows\wwwroot'),
    (Join-Path $projectRoot 'source\linux-package\opt\md-viewer-plain\wwwroot')
)

function Replace-RequiredText {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Old,
        [Parameter(Mandatory)] [string] $New
    )

    $content = [System.IO.File]::ReadAllText($Path)
    if (-not $content.Contains($Old)) {
        throw "Expected text was not found in $Path : $Old"
    }
    $content = $content.Replace($Old, $New)
    [System.IO.File]::WriteAllText($Path, $content, [System.Text.UTF8Encoding]::new($false))
}

foreach ($webRoot in $webRoots) {
    $jsPath = Join-Path $webRoot 'assets\index-CfLJVyjP.js'
    $cssPath = Join-Path $webRoot 'assets\index-DnhhYRM9.css'
    $htmlPath = Join-Path $webRoot 'index.html'

    Replace-RequiredText -Path $jsPath -Old 'rem-icon-CYNlvrSu.webp' -New 'plain-icon.svg'
    Replace-RequiredText -Path $jsPath -Old 'MD Viewer 雷姆主题' -New 'MD Viewer'
    Replace-RequiredText -Path $cssPath -Old 'url(./rem-background-DD0jVYOE.webp)' -New 'none'
    $plainStylesheets = '<link rel="stylesheet" crossorigin href="./assets/index-DnhhYRM9.css">' + [Environment]::NewLine + '    <link rel="stylesheet" href="./plain-theme.css">'
    Replace-RequiredText -Path $htmlPath -Old '<link rel="stylesheet" crossorigin href="./assets/index-DnhhYRM9.css">' -New $plainStylesheets

    Copy-Item -LiteralPath (Join-Path $assetRoot 'plain-icon.svg') -Destination (Join-Path $webRoot 'assets\plain-icon.svg') -Force
    Copy-Item -LiteralPath (Join-Path $assetRoot 'plain-theme.css') -Destination (Join-Path $webRoot 'plain-theme.css') -Force

    $removedAssets = @(
        (Join-Path $webRoot 'assets\rem-background-DD0jVYOE.webp'),
        (Join-Path $webRoot 'assets\rem-icon-CYNlvrSu.webp')
    )
    foreach ($removedAsset in $removedAssets) {
        if (-not (Test-Path -LiteralPath $removedAsset)) {
            throw "Expected third-party asset was not found: $removedAsset"
        }
        Remove-Item -LiteralPath $removedAsset
    }
}

Write-Output 'Sanitized Windows and Linux web resources.'
