[CmdletBinding()]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSAvoidUsingWriteHost',
    '',
    Justification = 'GitHub Actions masking commands require the workflow command protocol on standard output.')]
param(
    [Parameter(Mandatory)]
    [string]$DisplayName,
    [Parameter(Mandatory)]
    [string]$LegacyCallbackUri,
    [Parameter(Mandatory)]
    [string]$ModernCallbackUri,
    [string]$StatePath = '.classic-net-bff-deployment-state.json',
    [switch]$MultiTenant,
    [ValidateRange(1, 7)]
    [int]$SecretLifetimeDays = 1,
    [ValidatePattern('^[A-Za-z_][A-Za-z0-9_]*$')]
    [string]$SecretEnvironmentVariableName = 'CROESUS_DEPLOYMENT_CLIENT_SECRET'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$graphBaseUri = 'https://graph.microsoft.com/v1.0'
$credentialDisplayName = "$DisplayName GitHub deployment demo"
$ownershipTag = 'Croesus.ClassicNetBffDeployment.v1'

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
            $json = $Body | ConvertTo-Json -Depth 20 -Compress
            [System.IO.File]::WriteAllText(
                $bodyPath,
                $json,
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

            $httpStatusMatch = [regex]::Match(
                $resultText,
                '(?i)(?:HTTP(?:/\d(?:\.\d)?)?\s+|status(?:\s+code)?["'':=\s]+)(?<status>\d{3})\b')
            $hasSemanticNotFound = [regex]::IsMatch(
                $resultText,
                '(?i)(?:^|\s)Not\s+Found(?=\s*\()')
            $graphCodeMatch = [regex]::Match(
                $resultText,
                '(?i)["'']?code["'']?\s*[:=]\s*["''](?<code>[A-Za-z0-9_]+)["'']')
            $httpStatus = if ($httpStatusMatch.Success) {
                $httpStatusMatch.Groups['status'].Value
            }
            elseif ($hasSemanticNotFound) {
                '404'
            }
            else {
                'unknown'
            }
            $graphCode = if ($graphCodeMatch.Success) { $graphCodeMatch.Groups['code'].Value } else { 'unknown' }
            $isTransientResourceNotFound =
                $httpStatus -ceq '404' -and
                $graphCode -ceq 'Request_ResourceNotFound'
            $isTransientDirectoryConcurrencyViolation =
                $graphCode -ceq 'Directory_ConcurrencyViolation'
            $isTransientGraphFailure =
                $isTransientResourceNotFound -or
                $isTransientDirectoryConcurrencyViolation

            if (-not $isTransientGraphFailure -or $attempt -eq $maxAttempts) {
                throw "Microsoft Graph request failed: $Method $Uri (Azure CLI exit code $exitCode; HTTP status $httpStatus; Graph code $graphCode)."
            }

            $delaySeconds = [math]::Pow(2, $attempt)
            Start-Sleep -Seconds $delaySeconds
        }
    }
    finally {
        if ($null -ne $bodyPath -and (Test-Path -LiteralPath $bodyPath -PathType Leaf)) {
            Remove-Item -LiteralPath $bodyPath -Force
        }
    }
}

function Assert-DeploymentCallbackUri {
    param(
        [Parameter(Mandatory)][string]$Value,
        [Parameter(Mandatory)][string]$ParameterName
    )

    $parsedUri = $null
    if (-not [Uri]::IsWellFormedUriString($Value, [UriKind]::Absolute) -or
        -not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$parsedUri) -or
        $parsedUri.Scheme -ne [Uri]::UriSchemeHttps -or
        [string]::IsNullOrWhiteSpace($parsedUri.Host) -or
        -not [string]::IsNullOrEmpty($parsedUri.UserInfo) -or
        -not [string]::IsNullOrEmpty($parsedUri.Query) -or
        -not [string]::IsNullOrEmpty($parsedUri.Fragment) -or
        $parsedUri.AbsolutePath -cne '/signin-oidc') {
        throw "$ParameterName must be an absolute HTTPS URI with no user info, query, or fragment and the exact path /signin-oidc."
    }

    return $parsedUri.AbsoluteUri
}

function Assert-RegistrationPreflight {
    param(
        [Parameter(Mandatory)][string]$ObjectId,
        [Parameter(Mandatory)][string[]]$ExpectedRedirectUris,
        [Parameter(Mandatory)][string]$ExpectedSignInAudience
    )

    $registration = Invoke-Graph `
        -Method GET `
        -Uri "$graphBaseUri/applications/$($ObjectId)?`$select=id,appId,signInAudience,isFallbackPublicClient,web,spa,optionalClaims"
    $observedRedirectUris = @()
    if ($null -ne $registration.web -and $null -ne $registration.web.redirectUris) {
        $observedRedirectUris = @($registration.web.redirectUris | ForEach-Object { [string]$_ })
    }

    $missingRedirectUris = @($ExpectedRedirectUris | Where-Object { $_ -cnotin $observedRedirectUris })
    if ($missingRedirectUris.Count -gt 0) {
        throw "The registration does not carry every deployment callback URI. Package deployment would produce a challenge that cannot complete. Missing: $($missingRedirectUris -join ', ')"
    }
    if ([string]$registration.signInAudience -cne $ExpectedSignInAudience) {
        throw "The registration sign-in audience is '$([string]$registration.signInAudience)' but the deployment expects '$ExpectedSignInAudience'."
    }
    if ([bool]$registration.isFallbackPublicClient) {
        throw 'The registration is treated as a public client. A confidential web deployment must not proceed against it.'
    }
    $spaRedirectUris = @()
    if ($null -ne $registration.spa -and $null -ne $registration.spa.redirectUris) {
        $spaRedirectUris = @($registration.spa.redirectUris)
    }
    if ($spaRedirectUris.Count -gt 0) {
        throw 'The registration exposes single-page application redirect URIs. The confidential web platform must be the only one configured.'
    }

    # A missing optional claim does not fail a sign-in, so nothing downstream reports it. It only
    # surfaces as an evidence field that stays empty, which is indistinguishable from an
    # authentication that genuinely carried no such value.
    $idTokenClaims = @()
    if ($null -ne $registration.optionalClaims -and $null -ne $registration.optionalClaims.idToken) {
        $idTokenClaims = @($registration.optionalClaims.idToken)
    }
    foreach ($claimName in @('auth_time', 'amr')) {
        if ($claimName -cnotin @($idTokenClaims | ForEach-Object { [string]$_.name })) {
            throw "The registration does not request the '$claimName' ID token claim. The evidence surface would report it as absent."
        }
    }
}

function Assert-DeploymentCredentialValidity {
    param(
        [Parameter(Mandatory)][string]$ObjectId,
        [Parameter(Mandatory)][string]$KeyId,
        [Parameter(Mandatory)][int]$MinimumRemainingMinutes
    )

    # Expiry is read from registration metadata. Inferring it from a failed sign-in cannot separate an
    # expired credential from an unreachable endpoint, which is how the original diagnosis went wrong.
    $registration = Invoke-Graph `
        -Method GET `
        -Uri "$graphBaseUri/applications/$($ObjectId)?`$select=passwordCredentials"
    $credentialMetadata = @($registration.passwordCredentials | Where-Object {
            [string]$_.keyId -ceq $KeyId
        })
    if ($credentialMetadata.Count -ne 1) {
        throw 'The rotated deployment credential is not visible in registration metadata. Package deployment must not start against an unverified credential.'
    }

    $endDateTime = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse(
            [string]$credentialMetadata[0].endDateTime,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::RoundtripKind,
            [ref]$endDateTime)) {
        throw 'The deployment credential metadata does not carry a parseable expiry.'
    }

    $remainingMinutes = [int]($endDateTime - [DateTimeOffset]::UtcNow).TotalMinutes
    if ($remainingMinutes -lt $MinimumRemainingMinutes) {
        throw "The deployment credential has $remainingMinutes minute(s) of validity left, below the $MinimumRemainingMinutes minute preflight margin. Re-run provisioning instead of starting a deployment that expires mid-flight."
    }

    return $endDateTime
}

function Save-State {
    $script:state | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $script:fullStatePath -Encoding utf8NoBOM
}

if ($MyInvocation.InvocationName -ne '.') {
    throw 'Dot-source this script in the workflow PowerShell step so the masked secret remains available only in the current process.'
}

if ([string]::IsNullOrWhiteSpace($DisplayName)) {
    throw '-DisplayName cannot be empty.'
}

$validatedLegacyCallbackUri = Assert-DeploymentCallbackUri `
    -Value $LegacyCallbackUri `
    -ParameterName 'LegacyCallbackUri'
$validatedModernCallbackUri = Assert-DeploymentCallbackUri `
    -Value $ModernCallbackUri `
    -ParameterName 'ModernCallbackUri'
if ($validatedLegacyCallbackUri -ceq $validatedModernCallbackUri) {
    throw 'LegacyCallbackUri and ModernCallbackUri must identify two different deployed applications.'
}
$redirectUris = @($validatedLegacyCallbackUri, $validatedModernCallbackUri)

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI is required. Authenticate the workflow with GitHub OIDC before dot-sourcing this script.'
}

$accountJson = & az account show --output json --only-show-errors
if ($LASTEXITCODE -ne 0) {
    throw 'Azure CLI is not authenticated. Authenticate the workflow for the target home tenant.'
}
$account = ($accountJson -join [Environment]::NewLine) | ConvertFrom-Json
$tenantId = [string]$account.tenantId
$parsedTenantId = [guid]::Empty
if (-not [guid]::TryParse($tenantId, [ref]$parsedTenantId)) {
    throw 'Azure CLI did not return a valid home tenant ID.'
}

$fullStatePath = [System.IO.Path]::GetFullPath($StatePath)
$stateDirectory = Split-Path -Parent $fullStatePath
if (-not (Test-Path -LiteralPath $stateDirectory -PathType Container)) {
    throw "The state directory does not exist: $stateDirectory"
}

$priorState = $null
if (Test-Path -LiteralPath $fullStatePath -PathType Leaf) {
    $priorState = Get-Content -LiteralPath $fullStatePath -Raw | ConvertFrom-Json
    if ([int]$priorState.schemaVersion -ne 1) {
        throw "Unsupported state schema version: $($priorState.schemaVersion)"
    }
    if ([string]$priorState.tenantId -ne $tenantId -or [string]$priorState.displayName -ne $DisplayName) {
        throw 'The existing state file belongs to a different tenant or display name. Use another StatePath.'
    }
}

$escapedDisplayName = $DisplayName.Replace("'", "''")
$queryUri = "$graphBaseUri/applications?`$filter=displayName eq '$escapedDisplayName'&`$select=id,appId,displayName,passwordCredentials,tags"
$applicationResponse = Invoke-Graph -Method GET -Uri $queryUri
$appMatches = @($applicationResponse.value)
if ($appMatches.Count -gt 1) {
    throw "More than one app registration has the exact display name '$DisplayName'. Rename or remove duplicates before continuing."
}
if ($appMatches.Count -eq 1 -and $ownershipTag -notin @($appMatches[0].tags)) {
    throw "The existing app registration '$DisplayName' is not tagged as workflow-managed. Refusing to claim it."
}

$signInAudience = if ($MultiTenant) { 'AzureADMultipleOrgs' } else { 'AzureADMyOrg' }
$applicationCreated = $false
if ($appMatches.Count -eq 0) {
    Write-Status "Creating app registration '$DisplayName'"
    $application = Invoke-Graph -Method POST -Uri "$graphBaseUri/applications" -Body @{
        displayName = $DisplayName
        signInAudience = $signInAudience
        tags = @($ownershipTag)
        web = @{ redirectUris = $redirectUris }
        spa = @{ redirectUris = @() }
        isFallbackPublicClient = $false
    }
    $applicationCreated = $true
    $priorSameNameCredentials = @()
}
else {
    $application = $appMatches[0]
    if ($null -ne $priorState -and [string]$priorState.application.objectId -ne [string]$application.id) {
        throw 'The app registration resolved by display name does not match the object ID in the existing state.'
    }
    if ($null -ne $priorState) {
        $applicationCreated = [bool]$priorState.application.created
    }
    $priorSameNameCredentials = @($application.passwordCredentials | Where-Object {
            [string]$_.displayName -ceq $credentialDisplayName
        })
    Write-Status "Reusing app registration '$DisplayName'"
}

$applicationId = [string]$application.appId
$applicationObjectId = [string]$application.id
$state = [ordered]@{
    schemaVersion = 1
    tenantId = $tenantId
    displayName = $DisplayName
    application = [ordered]@{
        objectId = $applicationObjectId
        clientId = $applicationId
        created = $applicationCreated
    }
    servicePrincipal = $null
    passwordCredential = $null
}
Save-State

$patchBody = @{
    signInAudience = $signInAudience
    tags = @($ownershipTag)
    web = @{ redirectUris = $redirectUris }
    spa = @{ redirectUris = @() }
    isFallbackPublicClient = $false
    # Both claims are omitted from a v2.0 ID token unless the registration asks for them, and the
    # evidence surface reports an absent claim as absent rather than inventing a value. auth_time
    # carries the freshness check behind the max_age re-authentication control. include_granular_amr
    # is documented as the way to request AMR, and Microsoft Entra ID emits auth_time from this same
    # collection while withholding amr, which is unresolved.
    optionalClaims = @{
        idToken = @(
            @{ name = 'auth_time'; source = $null; essential = $false; additionalProperties = @() }
            @{ name = 'amr'; source = $null; essential = $false; additionalProperties = @('include_granular_amr') }
        )
        accessToken = @()
        saml2Token = @()
    }
}
Write-Status 'Converging the confidential web callback platform'
Invoke-Graph -Method PATCH -Uri "$graphBaseUri/applications/$applicationObjectId" -Body $patchBody | Out-Null

# Preflight. A package deployment against a registration that is missing a callback, exposes the
# wrong platform, or targets the wrong audience fails at the first sign-in rather than at publish,
# where the symptom no longer names the cause.
Write-Status 'Preflight: asserting the registration shape the deployment depends on'
Assert-RegistrationPreflight `
    -ObjectId $applicationObjectId `
    -ExpectedRedirectUris $redirectUris `
    -ExpectedSignInAudience $signInAudience

$servicePrincipalQuery = "$graphBaseUri/servicePrincipals?`$filter=appId eq '$applicationId'&`$select=id,appId"
$servicePrincipalResponse = Invoke-Graph -Method GET -Uri $servicePrincipalQuery
$servicePrincipalMatches = @($servicePrincipalResponse.value)
if ($servicePrincipalMatches.Count -gt 1) {
    throw "More than one home-tenant service principal exists for application ID $applicationId."
}

$servicePrincipalCreated = $false
if ($servicePrincipalMatches.Count -eq 0) {
    Write-Status 'Creating the home-tenant service principal'
    $servicePrincipal = Invoke-Graph -Method POST -Uri "$graphBaseUri/servicePrincipals" -Body @{
        appId = $applicationId
    }
    $servicePrincipalCreated = $true
}
else {
    $servicePrincipal = $servicePrincipalMatches[0]
    if ($null -ne $priorState -and
        $null -ne $priorState.servicePrincipal -and
        [string]$priorState.servicePrincipal.objectId -ne [string]$servicePrincipal.id) {
        throw 'The home service principal does not match the object ID in the existing state.'
    }
    if ($null -ne $priorState -and $null -ne $priorState.servicePrincipal) {
        $servicePrincipalCreated = [bool]$priorState.servicePrincipal.created
    }
    Write-Status 'Reusing the home-tenant service principal'
}

$state.servicePrincipal = [ordered]@{
    objectId = [string]$servicePrincipal.id
    created = $servicePrincipalCreated
}
Save-State

$expiresOn = [DateTimeOffset]::UtcNow.AddDays($SecretLifetimeDays)
Write-Status "Creating the replacement deployment credential with a $SecretLifetimeDays day lifetime"
$credential = Invoke-Graph -Method POST -Uri "$graphBaseUri/applications/$applicationObjectId/addPassword" -Body @{
    passwordCredential = @{
        displayName = $credentialDisplayName
        endDateTime = $expiresOn.ToString('o')
    }
}

$secretText = [string]$credential.secretText
if ([string]::IsNullOrEmpty($secretText)) {
    throw 'Microsoft Graph created the credential without returning secret material.'
}
if ([string]$env:GITHUB_ACTIONS -eq 'true') {
    [Console]::WriteLine("::add-mask::$secretText")
}
[Environment]::SetEnvironmentVariable(
    $SecretEnvironmentVariableName,
    $secretText,
    [EnvironmentVariableTarget]::Process)

$newCredentialKeyId = [string]$credential.keyId
$state.passwordCredential = [ordered]@{
    keyId = $newCredentialKeyId
    created = $true
}
Save-State

foreach ($priorCredential in $priorSameNameCredentials) {
    $priorKeyId = [string]$priorCredential.keyId
    if ($priorKeyId -eq $newCredentialKeyId) {
        continue
    }
    Write-Status 'Removing a superseded deployment credential'
    Invoke-Graph -Method POST -Uri "$graphBaseUri/applications/$applicationObjectId/removePassword" -Body @{
        keyId = $priorKeyId
    } | Out-Null
}

$credential.secretText = $null
$secretText = $null

$credentialExpiresOn = Assert-DeploymentCredentialValidity `
    -ObjectId $applicationObjectId `
    -KeyId $newCredentialKeyId `
    -MinimumRemainingMinutes 30
Write-Status "Preflight: registration metadata reports the deployment credential valid until $($credentialExpiresOn.ToUniversalTime().ToString('u'))."
Write-Status "Provisioning complete. The masked secret is available only in process environment variable $SecretEnvironmentVariableName."