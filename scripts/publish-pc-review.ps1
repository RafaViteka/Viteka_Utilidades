[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{32}$')][string]$DeviceId,
    [Parameter(Mandatory)][string]$InstallerDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# This script publishes only an already tested, isolated installer. The workflow
# requires both Windows validation jobs before downloading its own run's artifact.
if ($env:GITHUB_EVENT_NAME -ne 'workflow_dispatch' -or
    $env:GITHUB_REF -ne 'refs/heads/feature/post-017-improvements' -or
    $env:GITHUB_REPOSITORY -ne 'RafaViteka/Viteka_Utilidades' -or
    $env:GITHUB_SHA -cnotmatch '^[a-f0-9]{40}$' -or
    $DeviceId -cnotmatch '^[a-f0-9]{32}$') {
    throw 'Review publication requires the explicit test-branch workflow and an exact PC ID.'
}
$root = Split-Path $PSScriptRoot -Parent
$sourceCommit = if ($env:VITEKA_REVIEW_VALIDATED_SHA) { $env:VITEKA_REVIEW_VALIDATED_SHA } else { $env:GITHUB_SHA }
if ($sourceCommit -cnotmatch '^[a-f0-9]{40}$') { throw 'Invalid validated commit.' }
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if ($version -cnotmatch '^(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})$') { throw 'Invalid review version.' }
$tag = "review-v$version-pc-$DeviceId"
$name = "VitekaUtilidades-Test-Setup-$version-win-x64.exe"
$files = @(Get-ChildItem $InstallerDirectory -File -Recurse)
if ($files.Count -ne 1 -or $files[0].Name -cne $name) { throw 'Expected exactly the isolated installer for this version.' }
$file = $files[0]
$metadata = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($file.FullName)
$productVersion = [Version]::new($metadata.ProductMajorPart, $metadata.ProductMinorPart, $metadata.ProductBuildPart, $metadata.ProductPrivatePart)
if ($metadata.ProductName.TrimEnd([char[]]@(32, 0)) -cne 'Viteka Utilidades - Pruebas' -or
    $productVersion -ne [Version]"$version.0") {
    throw "Unexpected review metadata: product=[$($metadata.ProductName)] numericVersion=[$productVersion] expected=[$version.0]."
}
$hash = (Get-FileHash -Algorithm SHA256 $file.FullName).Hash.ToLowerInvariant()

function Get-ReviewRelease {
    # Drafts do not resolve through /releases/tags/{tag}; the authenticated list
    # includes them. Match the exact tag rather than guessing an untagged URL.
    $json = & gh api "repos/$($env:GITHUB_REPOSITORY)/releases?per_page=100"
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect existing review releases.' }
    $releases = ($json -join "`n") | ConvertFrom-Json
    $matchingReleases = @($releases | Where-Object { $_.tag_name -ceq $tag })
    if ($matchingReleases.Count -gt 1) { throw 'Ambiguous review release.' }
    if ($matchingReleases.Count -eq 1) { return $matchingReleases[0] }
    return $null
}
$release = Get-ReviewRelease
# Published versions and assets are immutable. An interrupted draft can resume
# only after its target, sole asset, size and digest all match this tested file.
if ($null -ne $release -and -not $release.draft) { throw 'This PC review is already published; use a new version.' }
$notes = @"
Revisión técnica $version de Viteka Utilidades - Pruebas, dirigida a un único identificador de PC.

Completa la ficha del artículo con stock mínimo, FUC, FUV y último proveedor desde APADMIN_AC_EXISTENCIAS; resuelve el nombre en APADMIN_AD_PROVEEDORES. Obtiene el laboratorio cruzando ART_CLASE y ART_LAB_CODIGO con DW_AB_LABORATORIOS. Añade clasificación ABC, PVP, agrupación homogénea, código de barras y estado de baja cuando las tablas están disponibles. Conserva compatibilidad con AC1_EXISTENCIAS.

Datos de la farmacia permite elegir el plazo de revisión de caducidad, tres meses por defecto, y si el stock caducado cuenta como efectivo. Mientras no exista una relación de lotes, todas las unidades toman la caducidad de la ficha. La pantalla incorpora el filtro Revisar caducidad, abrevia C.N. y muestra Descripción. Los grupos con faltas muestran el número de unidades pendientes.

Incluye las correcciones del actualizador incorporadas en 0.18.2. Desde 0.18.2 esta revisión puede instalarse desde Configuración. Las versiones 0.18.0 y 0.18.1 requieren ejecutar manualmente una revisión corregida una vez sobre la instalación existente, sin desinstalar. Conserva el ID, las cuentas y los datos propios de pruebas; la edición habitual utiliza otro servicio y otras carpetas.

La publicación exige las pruebas Windows de ambas ediciones y de convivencia del mismo commit. No acredita conexión a Oracle en la farmacia. Las ediciones normales y los demás identificadores de pruebas omiten esta etiqueta.

SHA256: $hash
"@
$notesPath = Join-Path $InstallerDirectory 'release-notes.txt'
Set-Content -LiteralPath $notesPath -Value $notes -Encoding utf8
if ($null -eq $release) {
    & gh api "repos/$($env:GITHUB_REPOSITORY)/git/ref/tags/$tag" *> $null
    if ($LASTEXITCODE -eq 0) { throw 'This PC review tag already exists; use a new version.' }
    & gh release create $tag --repo $env:GITHUB_REPOSITORY --target $sourceCommit --draft --prerelease --latest=false --title "Viteka Utilidades - Pruebas $version" --notes-file $notesPath
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the isolated draft release.' }
    & gh release upload $tag $file.FullName --repo $env:GITHUB_REPOSITORY
    if ($LASTEXITCODE -ne 0) { throw 'Could not upload the isolated installer; release remains a draft.' }
    $release = Get-ReviewRelease
    if ($null -eq $release) { throw 'Could not find the draft release.' }
}
$assets = @($release.assets)
$url = "https://github.com/$($env:GITHUB_REPOSITORY)/releases/download/$tag/$name"
$draftUrl = $url
# Until publication GitHub gives draft assets an untagged-* URL. It must still
# belong to this exact repository and the draft's own temporary tag.
if ($release.html_url -cmatch '^https://github\.com/RafaViteka/Viteka_Utilidades/releases/tag/(untagged-[a-f0-9]+)$') {
    $draftUrl = "https://github.com/$($env:GITHUB_REPOSITORY)/releases/download/$($Matches[1])/$name"
}
if (-not $release.draft -or -not $release.prerelease -or $release.tag_name -cne $tag -or
    $release.target_commitish -cne $sourceCommit -or $assets.Count -ne 1 -or
    $assets[0].name -cne $name -or $assets[0].browser_download_url -cnotin @($url, $draftUrl) -or
    $assets[0].size -ne $file.Length -or $assets[0].digest -cne "sha256:$hash") {
    throw 'Draft/installer verification failed; release remains a draft.'
}
# Some GitHub release flows defer creating the tag until publication. Create it
# explicitly if still absent, then verify it points at the tested commit.
$json = & gh api "repos/$($env:GITHUB_REPOSITORY)/git/ref/tags/$tag" 2>$null
if ($LASTEXITCODE -ne 0) {
    & gh api --method POST "repos/$($env:GITHUB_REPOSITORY)/git/refs" -f "ref=refs/tags/$tag" -f "sha=$sourceCommit" *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the isolated review tag.' }
    $json = & gh api "repos/$($env:GITHUB_REPOSITORY)/git/ref/tags/$tag"
    if ($LASTEXITCODE -ne 0) { throw 'Could not verify the review tag.' }
}
$ref = ($json -join "`n") | ConvertFrom-Json
if ($ref.object.type -cne 'commit' -or $ref.object.sha -cne $sourceCommit) { throw 'Review tag does not point to the tested commit.' }
& gh release edit $tag --repo $env:GITHUB_REPOSITORY --draft=false --prerelease --latest=false
if ($LASTEXITCODE -ne 0) { throw 'Could not publish the verified PC review.' }
$json = & gh api "repos/$($env:GITHUB_REPOSITORY)/releases/$($release.id)"
if ($LASTEXITCODE -ne 0) { throw 'Could not verify the published review.' }
$published = ($json -join "`n") | ConvertFrom-Json
$publishedAssets = @($published.assets)
if ($published.draft -or -not $published.prerelease -or $published.tag_name -cne $tag -or
    $publishedAssets.Count -ne 1 -or $publishedAssets[0].browser_download_url -cne $url -or
    $publishedAssets[0].size -ne $file.Length -or $publishedAssets[0].digest -cne "sha256:$hash") {
    throw 'Published review verification failed.'
}
Write-Output "Published only the isolated review: $url"
