<#
.SYNOPSIS
    Loads the CI secrets and protects the default branch.

.DESCRIPTION
    Needs the GitHub CLI, authenticated:

        winget install GitHub.cli
        gh auth login

    What it does:

      1. Uploads the keystore (base64) and its password as repository secrets, so the
         workflow can sign the APK without the key ever living in the repository.
      2. Protects the default branch: no force pushes, no deletion, and the test job has
         to pass before anything merges.

    Direct pushes by administrators stay allowed on purpose. On a repository where you are
    the only person with write access, outsiders already cannot push; forcing yourself
    through a pull request for every change buys friction rather than safety. Pass
    -RequirePullRequest if you want that anyway.
#>
[CmdletBinding()]
param(
    [string] $Keystore = (Join-Path $PSScriptRoot 'continental.keystore'),

    [Parameter(Mandatory)]
    [string] $KeystorePassword,

    [switch] $RequirePullRequest,

    [switch] $SkipSecrets
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'Falta la CLI de GitHub. Instálala con: winget install GitHub.cli   y luego: gh auth login'
}

gh auth status 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'La CLI de GitHub no está autenticada. Ejecuta: gh auth login' }

$repo = (gh repo view --json nameWithOwner --jq .nameWithOwner)
$branch = (gh repo view --json defaultBranchRef --jq .defaultBranchRef.name)

Write-Host "Repositorio: $repo (rama por defecto: $branch)" -ForegroundColor Cyan

if (-not $SkipSecrets) {
    if (-not (Test-Path $Keystore)) { throw "No existe $Keystore." }

    Write-Host "`nSubiendo secretos..." -ForegroundColor Cyan

    $base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($Keystore))
    $base64 | gh secret set ANDROID_KEYSTORE_BASE64 --repo $repo
    $KeystorePassword | gh secret set ANDROID_KEYSTORE_PASSWORD --repo $repo

    Write-Host 'ANDROID_KEYSTORE_BASE64 y ANDROID_KEYSTORE_PASSWORD listos.' -ForegroundColor Green
}

Write-Host "`nProtegiendo la rama $branch..." -ForegroundColor Cyan

$reviews = if ($RequirePullRequest) {
    @{ required_approving_review_count = 0; dismiss_stale_reviews = $true }
}
else {
    $null
}

$protection = @{
    required_status_checks        = @{ strict = $true; contexts = @('test') }
    enforce_admins                = $false
    required_pull_request_reviews = $reviews
    restrictions                  = $null
    allow_force_pushes            = $false
    allow_deletions               = $false
    required_conversation_resolution = $true
}

$json = $protection | ConvertTo-Json -Depth 6
$tmp = New-TemporaryFile
$json | Set-Content -LiteralPath $tmp -Encoding utf8

try {
    gh api --method PUT "repos/$repo/branches/$branch/protection" `
           -H 'Accept: application/vnd.github+json' --input $tmp | Out-Null
    Write-Host "Rama $branch protegida: sin force-push, sin borrado, y el job 'test' debe pasar." -ForegroundColor Green
}
finally {
    Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
}

Write-Host "`nListo." -ForegroundColor Cyan
gh secret list --repo $repo
