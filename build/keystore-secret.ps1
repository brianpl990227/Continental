<#
.SYNOPSIS
    Prints the keystore as base64 so it can be pasted into a GitHub secret.

.DESCRIPTION
    GitHub secrets hold text, not files, so the keystore travels base64-encoded. The
    workflow decodes it back into a file on the runner, which is destroyed with the job.

    Create two secrets in Settings -> Secrets and variables -> Actions:

        ANDROID_KEYSTORE_BASE64     the output of this script
        ANDROID_KEYSTORE_PASSWORD   the keystore password

    Never commit the keystore itself. Anyone holding it can sign updates that your phones
    will accept as genuine.
#>
[CmdletBinding()]
param(
    [string] $Keystore = (Join-Path $PSScriptRoot 'continental.keystore'),
    [switch] $ToClipboard
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Keystore)) {
    throw "No existe $Keystore."
}

$base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($Keystore))

if ($ToClipboard) {
    $base64 | Set-Clipboard
    Write-Host "Copiado al portapapeles ($($base64.Length) caracteres)." -ForegroundColor Green
    Write-Host 'Pégalo en el secreto ANDROID_KEYSTORE_BASE64.'
}
else {
    $base64
}
