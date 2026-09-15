<#
.SYNOPSIS
    Builds the shippable Continental packages into dist/.

.DESCRIPTION
    Android  -> a signed APK for sideloading. By default arm64-v8a only, which is the
                fastest build and enough for the developer's own phone. Pass -AllAbis for
                the package that also installs on 32-bit phones; that is what CI ships.
    Windows  -> a self-contained folder plus a zip. Self-contained means the machine needs
                neither the .NET runtime nor the Windows App SDK installed.

    The APK is signed with build/continental.keystore. Keep that file: Android refuses to
    install an update signed with a different key, so losing it means uninstalling the app
    on every phone before the next version will go on.

.EXAMPLE
    pwsh build/publish.ps1
    pwsh build/publish.ps1 -Target android -AllAbis
#>
[CmdletBinding()]
param(
    [ValidateSet('all', 'android', 'windows')]
    [string] $Target = 'all',

    # Also include 32-bit ARM (armeabi-v7a) and x86_64, so the APK installs on any phone.
    # Budget devices such as the Redmi 9A run a 32-bit Android image and reject an arm64-only
    # package with nothing but "the app was not installed". It grows the APK from 38 to 50 MB
    # and forces the Mono runtime, because CoreCLR ships no 32-bit ARM runtime pack, so it is
    # off by default for quick local builds and on in CI.
    [switch] $AllAbis,

    # Nunca con valor por defecto en el repositorio. Se toma de la variable de entorno
    # CONTINENTAL_KEYSTORE_PASSWORD, que es tambien lo que inyecta el CI desde sus secretos.
    [string] $KeystorePassword = $env:CONTINENTAL_KEYSTORE_PASSWORD,

    # Becomes both the Android versionCode and the display version 1.0.<n>. Android refuses
    # to install a lower versionCode than the one already on the phone, so CI offsets its run
    # number by 100: its releases then always sit above anything built by hand here, and the
    # YAML cannot say so itself because that file carries no comments.
    [int] $Version = 0
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Continental'
$dist = Join-Path $root 'dist'
$keystore = Join-Path $PSScriptRoot 'continental.keystore'

New-Item -ItemType Directory -Force -Path $dist | Out-Null

function Step($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }

if ($Target -in 'all', 'android') {
    Step 'Android'

    if (-not (Test-Path $keystore)) {
        throw "Falta $keystore. No se versiona: recupéralo de tu copia de seguridad."
    }

    if ([string]::IsNullOrWhiteSpace($KeystorePassword)) {
        throw 'Falta la contraseña. Define $env:CONTINENTAL_KEYSTORE_PASSWORD o pásala con -KeystorePassword.'
    }

    $rid = 'android-arm64'

    $buildArgs = @(
        'publish', $project,
        '-f', 'net10.0-android',
        '-c', 'Release',
        '--nologo',
        '-p:AndroidPackageFormat=apk',
        '-p:AndroidKeyStore=true',
        "-p:AndroidSigningKeyStore=$keystore",
        '-p:AndroidSigningKeyAlias=continental',
        "-p:AndroidSigningKeyPass=$KeystorePassword",
        "-p:AndroidSigningStorePass=$KeystorePassword"
    )

    # UniversalApk is this repository's own switch, not an SDK one: the project file turns it
    # into the Mono runtime plus the three runtime identifiers. The ABI list has to travel as
    # RuntimeIdentifiers from inside the project, so nothing about it is passed here.
    if ($AllAbis) {
        $buildArgs += '-p:UniversalApk=true'
    }
    else {
        $buildArgs += "-p:RuntimeIdentifier=$rid"
    }

    if ($Version -gt 0) {
        $buildArgs += "-p:ApplicationVersion=$Version"
        $buildArgs += "-p:ApplicationDisplayVersion=1.0.$Version"
    }

    # Signing is an incremental target: if a previously signed APK is still sitting there,
    # MSBuild considers the output up to date and silently keeps the old signature.
    $stale = Join-Path $project 'bin/Release/net10.0-android'
    if (Test-Path $stale) { Remove-Item $stale -Recurse -Force }

    dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación de Android.' }

    $apk = Get-ChildItem -Path (Join-Path $project 'bin/Release/net10.0-android') `
                         -Recurse -Filter '*-Signed.apk' |
           Sort-Object LastWriteTime -Descending | Select-Object -First 1

    if (-not $apk) { throw 'No se generó ningún APK firmado.' }

    $out = Join-Path $dist 'Continental.apk'
    Copy-Item $apk.FullName $out -Force

    Write-Host "APK: $out  ($([math]::Round($apk.Length / 1MB, 1)) MB)" -ForegroundColor Green
}

if ($Target -in 'all', 'windows') {
    Step 'Windows'

    $winOut = Join-Path $dist 'Continental-Windows'
    if (Test-Path $winOut) { Remove-Item $winOut -Recurse -Force }

    $winArgs = @(
        'publish', $project,
        '-f', 'net10.0-windows10.0.19041.0',
        '-c', 'Release',
        '--nologo',
        '-p:RuntimeIdentifier=win-x64',
        '-p:SelfContained=true',
        '-p:WindowsAppSDKSelfContained=true',
        '-p:WindowsPackageType=None',
        '-p:UseMonoRuntime=false',
        '-o', $winOut
    )

    if ($Version -gt 0) { $winArgs += "-p:ApplicationDisplayVersion=1.0.$Version" }

    dotnet @winArgs

    if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación de Windows.' }

    $zip = Join-Path $dist 'Continental-Windows.zip'
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $winOut '*') -DestinationPath $zip

    Write-Host "Carpeta: $winOut" -ForegroundColor Green
    Write-Host "Zip:     $zip  ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)" -ForegroundColor Green
}

Step 'Listo'
Get-ChildItem $dist | Select-Object Name, @{ N = 'MB'; E = { [math]::Round($_.Length / 1MB, 1) } }
