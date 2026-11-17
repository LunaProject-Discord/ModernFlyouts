# Builds the signed WinUI MSIX bundle (x86, x64, ARM64) into AppPackages\WinUI.
#
# The packaging targets sign with the 32-bit SignTool, which fails on the ~280 MB bundle
# ("An unexpected internal error has occurred"). The per-architecture packages sign fine, so
# the bundle is (re)signed here with the 64-bit SignTool afterwards.
param(
    [string]$Configuration = "Release",
    [string]$Thumbprint = "DCEC7DEBC5ACA1AE8AE579538FCE2BE0FB253215",
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root "AppPackages\WinUI"

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -prerelease -products * -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" |
    Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

& $msbuild (Join-Path $root "ModernFlyouts.WinUI\ModernFlyouts.WinUI.csproj") /restore /m /nologo /v:m `
    "/p:PackageMsix=true" "/p:Configuration=$Configuration" "/p:Platform=x64" `
    "/p:AppxBundle=Always" "/p:AppxBundlePlatforms=x86|x64|ARM64" `
    "/p:PackageCertificateThumbprint=$Thumbprint" "/p:AppxPackageDir=$outDir\"
$buildExit = $LASTEXITCODE

$bundle = Get-ChildItem $outDir -Recurse -Filter *.msixbundle | Select-Object -First 1
if (-not $bundle) {
    throw "No bundle was produced (MSBuild exit code $buildExit)."
}

& $signtool sign /fd SHA256 /sha1 $Thumbprint /s My /tr $TimestampUrl /td SHA256 $bundle.FullName
if ($LASTEXITCODE -ne 0) { throw "Signing the bundle failed." }

$signature = Get-AuthenticodeSignature $bundle.FullName
Write-Host ""
Write-Host "Bundle : $($bundle.FullName)"
Write-Host "Signer : $($signature.SignerCertificate.Subject)"
Write-Host "Status : $($signature.Status)"
