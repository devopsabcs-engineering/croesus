[CmdletBinding()]
param(
    # The API registration this script converges. Distinct from the web registration on purpose: the
    # audience boundary under discussion only exists when the resource and the client are two different
    # applications, so collapsing them into one registration would remove the thing being demonstrated.
    [Parameter(Mandatory)]
    [string]$DisplayName,

    # The confidential client permitted to request the scope without an interactive consent prompt. This
    # is the web registration the legacy, modern, and BFF sites already share.
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')]
    [string]$ClientAppId,

    [ValidatePattern('^[A-Za-z][A-Za-z0-9_.]{0,119}$')]
    [string]$ScopeName = 'access_as_user',

    [switch]$MultiTenant
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$graphBaseUri = 'https://graph.microsoft.com/v1.0'
$ownershipTag = 'Croesus.OwnedApiRegistration.v1'

function Write-Status {
    param([Parameter(Mandatory)][string]$Message)

    Write-Information ">>> $Message" -InformationAction Continue
}

function Invoke-Graph {
    param(
        [Parameter(Mandatory)][ValidateSet('GET', 'POST', 'PATCH')][string]$Method,
        [Parameter(Mandatory)][string]$Uri,
        [object]$Body
    )

    $bodyPath = $null
    try {
        $maxAttempts = 4
        $arguments = @('rest', '--method', $Method, '--uri', $Uri, '--output', 'json', '--only-show-errors')
        if ($null -ne $Body) {
            $bodyPath = Join-Path ([System.IO.Path]::GetTempPath()) "croesus-graph-$([guid]::NewGuid().ToString('N')).json"
            [System.IO.File]::WriteAllText(
                $bodyPath,
                ($Body | ConvertTo-Json -Depth 20 -Compress),
                [System.Text.UTF8Encoding]::new($false))
            $arguments += @('--headers', 'Content-Type=application/json', '--body', "@$bodyPath")
        }

        for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
            $result = & az @arguments 2>&1
            $exitCode = $LASTEXITCODE
            $resultText = $result -join [Environment]::NewLine
            if ($exitCode -eq 0) {
                if ([string]::IsNullOrWhiteSpace($resultText)) {
                    return $null
                }

                return $resultText | ConvertFrom-Json
            }

            # A registration that was created moments ago is not immediately readable everywhere in the
            # directory, and two writes racing against the same object surface as a concurrency violation.
            # Both are transient. Anything else is a real failure and is surfaced on the first attempt.
            $isTransient =
                [regex]::IsMatch($resultText, '(?i)Request_ResourceNotFound') -or
                [regex]::IsMatch($resultText, '(?i)Directory_ConcurrencyViolation')

            if (-not $isTransient -or $attempt -eq $maxAttempts) {
                throw "Microsoft Graph request failed: $Method $Uri (Azure CLI exit code $exitCode).`n$resultText"
            }

            Start-Sleep -Seconds ([math]::Pow(2, $attempt))
        }
    }
    finally {
        if ($null -ne $bodyPath -and (Test-Path -LiteralPath $bodyPath -PathType Leaf)) {
            Remove-Item -LiteralPath $bodyPath -Force
        }
    }
}

if ([string]::IsNullOrWhiteSpace($DisplayName)) {
    throw '-DisplayName cannot be empty.'
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI is required. Authenticate for the target home tenant before running this script.'
}

$accountJson = & az account show --output json --only-show-errors
if ($LASTEXITCODE -ne 0) {
    throw 'Azure CLI is not authenticated. Authenticate for the target home tenant.'
}
$tenantId = [string](($accountJson -join [Environment]::NewLine) | ConvertFrom-Json).tenantId

$escapedDisplayName = $DisplayName.Replace("'", "''")
$queryUri = "$graphBaseUri/applications?`$filter=displayName eq '$escapedDisplayName'&`$select=id,appId,displayName,identifierUris,api,tags"
$appMatches = @((Invoke-Graph -Method GET -Uri $queryUri).value)
if ($appMatches.Count -gt 1) {
    throw "More than one app registration has the exact display name '$DisplayName'. Rename or remove duplicates before continuing."
}
if ($appMatches.Count -eq 1 -and $ownershipTag -notin @($appMatches[0].tags)) {
    throw "The existing app registration '$DisplayName' is not tagged as managed by this script. Refusing to claim it."
}

$signInAudience = if ($MultiTenant) { 'AzureADMultipleOrgs' } else { 'AzureADMyOrg' }

if ($appMatches.Count -eq 0) {
    Write-Status "Creating API registration '$DisplayName'"
    $application = Invoke-Graph -Method POST -Uri "$graphBaseUri/applications" -Body @{
        displayName = $DisplayName
        signInAudience = $signInAudience
        tags = @($ownershipTag)
        isFallbackPublicClient = $false
    }
}
else {
    $application = $appMatches[0]
    Write-Status "Reusing API registration '$DisplayName'"
}

$applicationObjectId = [string]$application.id
$applicationId = [string]$application.appId
$identifierUri = "api://$applicationId"

# The identifier URI has to exist before a scope can name it, so it is written on its own.
$existingIdentifierUris = @()
if ($application.PSObject.Properties.Name -contains 'identifierUris' -and $null -ne $application.identifierUris) {
    $existingIdentifierUris = @($application.identifierUris)
}
if ($identifierUri -notin $existingIdentifierUris) {
    Write-Status "Setting the Application ID URI to $identifierUri"
    Invoke-Graph -Method PATCH -Uri "$graphBaseUri/applications/$applicationObjectId" -Body @{
        identifierUris = @($existingIdentifierUris + $identifierUri)
    } | Out-Null
}

# An existing scope is reused by id rather than recreated. A scope cannot be removed while it is enabled,
# and its id is what preAuthorizedApplications and any issued consent already reference.
$existingScopes = @()
if ($application.PSObject.Properties.Name -contains 'api' -and $null -ne $application.api -and $null -ne $application.api.oauth2PermissionScopes) {
    $existingScopes = @($application.api.oauth2PermissionScopes)
}
$matchingScope = $existingScopes | Where-Object { [string]$_.value -ceq $ScopeName } | Select-Object -First 1
$scopeId = if ($null -ne $matchingScope) { [string]$matchingScope.id } else { [guid]::NewGuid().ToString() }

$desiredScope = [ordered]@{
    id = $scopeId
    value = $ScopeName
    type = 'User'
    isEnabled = $true
    adminConsentDisplayName = "Access $DisplayName as the signed-in user"
    adminConsentDescription = "Allows the calling application to access $DisplayName on behalf of the signed-in user."
    userConsentDisplayName = "Access $DisplayName on your behalf"
    userConsentDescription = "Allows the application to access $DisplayName on your behalf."
}

$otherScopes = @($existingScopes | Where-Object { [string]$_.id -cne $scopeId })

# requestedAccessTokenVersion 2 is required, not cosmetic. Microsoft.Identity.Web builds a v2.0 authority
# from Instance and TenantId, and a v1 access token carries the sts.windows.net issuer, which fails issuer
# validation against that metadata.
#
# The scope is written before the pre-authorization rather than alongside it. Graph resolves
# delegatedPermissionIds against the scopes already stored on the application, so a single combined PATCH
# is rejected with InvalidValue even though the id it cannot find is in the same request body.
Write-Status "Converging the '$ScopeName' delegated scope"
Invoke-Graph -Method PATCH -Uri "$graphBaseUri/applications/$applicationObjectId" -Body @{
    signInAudience = $signInAudience
    tags = @($ownershipTag)
    api = @{
        requestedAccessTokenVersion = 2
        oauth2PermissionScopes = @($otherScopes + $desiredScope)
    }
} | Out-Null

Write-Status "Pre-authorizing $ClientAppId for '$ScopeName'"
Invoke-Graph -Method PATCH -Uri "$graphBaseUri/applications/$applicationObjectId" -Body @{
    api = @{
        preAuthorizedApplications = @(
            @{
                appId = $ClientAppId
                delegatedPermissionIds = @($scopeId)
            }
        )
    }
} | Out-Null

$servicePrincipalQuery = "$graphBaseUri/servicePrincipals?`$filter=appId eq '$applicationId'&`$select=id,appId"
$servicePrincipalMatches = @((Invoke-Graph -Method GET -Uri $servicePrincipalQuery).value)
if ($servicePrincipalMatches.Count -gt 1) {
    throw "More than one home-tenant service principal exists for application ID $applicationId."
}

if ($servicePrincipalMatches.Count -eq 0) {
    Write-Status 'Creating the home-tenant service principal'
    $servicePrincipal = Invoke-Graph -Method POST -Uri "$graphBaseUri/servicePrincipals" -Body @{
        appId = $applicationId
    }
}
else {
    $servicePrincipal = $servicePrincipalMatches[0]
}

# Preflight. A registration that is missing the identifier URI, the scope, or the pre-authorization fails
# at the first proxied call rather than at provisioning, where the symptom no longer names the cause.
Write-Status 'Preflight: asserting the registration shape the reference BFF depends on'
$verifyUri = "$graphBaseUri/applications/$applicationObjectId`?`$select=id,appId,signInAudience,identifierUris,api"
$verified = Invoke-Graph -Method GET -Uri $verifyUri

if ($identifierUri -notin @($verified.identifierUris)) {
    throw "Preflight failed: the registration does not expose the Application ID URI $identifierUri."
}
if ([string]$verified.signInAudience -cne $signInAudience) {
    throw "Preflight failed: signInAudience is '$($verified.signInAudience)' rather than '$signInAudience'."
}
if ([int]$verified.api.requestedAccessTokenVersion -ne 2) {
    throw 'Preflight failed: requestedAccessTokenVersion is not 2, so the API would receive v1 tokens whose issuer fails validation.'
}
$verifiedScope = @($verified.api.oauth2PermissionScopes) | Where-Object { [string]$_.value -ceq $ScopeName } | Select-Object -First 1
if ($null -eq $verifiedScope -or -not [bool]$verifiedScope.isEnabled) {
    throw "Preflight failed: the delegated scope '$ScopeName' is absent or disabled."
}
$verifiedPreAuth = @($verified.api.preAuthorizedApplications) | Where-Object { [string]$_.appId -ceq $ClientAppId } | Select-Object -First 1
if ($null -eq $verifiedPreAuth -or [string]$verifiedScope.id -notin @($verifiedPreAuth.delegatedPermissionIds)) {
    throw "Preflight failed: client $ClientAppId is not pre-authorized for '$ScopeName', so the BFF would face a consent prompt it cannot answer."
}

[ordered]@{
    tenantId = $tenantId
    displayName = $DisplayName
    objectId = $applicationObjectId
    clientId = $applicationId
    servicePrincipalObjectId = [string]$servicePrincipal.id
    identifierUri = $identifierUri
    scopeName = $ScopeName
    scopeId = [string]$verifiedScope.id
    # What the BFF puts in DownstreamApi:Scopes.
    delegatedScope = "$identifierUri/$ScopeName"
    preAuthorizedClientId = $ClientAppId
} | ConvertTo-Json -Depth 5
