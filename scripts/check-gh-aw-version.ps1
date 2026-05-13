$ErrorActionPreference = 'Stop'

function Get-NormalizedVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $trimmed = $Value.Trim()
    if ($trimmed.StartsWith('v')) {
        $trimmed = $trimmed.Substring(1)
    }

    return [Version]$trimmed
}

try {
    $localOutput = gh aw version 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to determine local gh aw version.`n$localOutput"
    }

    $localMatch = [regex]::Match($localOutput, 'v\d+\.\d+\.\d+')
    if (-not $localMatch.Success) {
        throw "Unable to parse local gh aw version from output:`n$localOutput"
    }

    $latestOutput = gh release view --repo github/gh-aw --json tagName -q .tagName 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to determine latest gh-aw release. Ensure your gh auth token can access github/gh-aw releases (SAML/SSO may need authorization).`n$latestOutput"
    }

    $localTag = $localMatch.Value
    $latestTag = $latestOutput.Trim()

    $localVersion = Get-NormalizedVersion -Value $localTag
    $latestVersion = Get-NormalizedVersion -Value $latestTag

    Write-Output ("gh-aw local={0} latest={1}" -f $localTag, $latestTag)

    if ($localVersion -lt $latestVersion) {
        Write-Output "UPDATE NEEDED: upgrade gh-aw before regenerating lockfiles."
        exit 2
    }

    Write-Output "gh-aw is up to date."
    exit 0
}
catch {
    Write-Error $_
    exit 1
}