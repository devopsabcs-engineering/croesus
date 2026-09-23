<#
.SYNOPSIS
    Verifies that a Croesus POC App Service is reachable, challenges correctly, and carries the
    ingress posture its template declared.

.DESCRIPTION
    A reachable endpoint is not a successful deployment and a generic redirect is not a valid
    challenge, so this script separates four failure classes that otherwise present identically:
    a DNS failure, a routing failure, an authorization failure, and a challenge-shape failure.

    When an authorization failure is an HTTP 403 it resolves the cause through the control plane
    rather than guessing, because three unrelated conditions produce the same status:

      1. The App Service plan degraded below the declared SKU. An external process returns this
         subscription's plan to F1 on roughly a 24 hour cycle.
      2. Public ingress is disabled while the template declares Public. An application-layer 403
         carrying a "Web App - Unavailable" title is this signature specifically. It is neither a
         management-plane policy denial nor tier degradation, and the three are reported apart.
      3. The site is stopped.

    The checks run in that order and the first match is reported, because tier degradation can
    cause the others as a side effect.

    This script never sets publicNetworkAccess in either direction. Under ID-01 the template owns
    the ingress posture through its ingressMode parameter; drift is reported here and repaired by
    redeploying the template.

    Headers are inspected in memory. Raw Set-Cookie and authorization header values are never
    written to output, logs, or artifacts; only allowlisted verdicts and presence booleans leave
    this script.

.NOTES
    The modern challenge route is the site root. The legacy challenge route is /signin.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ResourceGroupName,
    [Parameter(Mandatory)]
    [string]$AppName,
    [Parameter(Mandatory)]
    [string]$ExpectedClientId,
    [ValidatePattern('^/')]
    [string]$ChallengePath = '/',
    [ValidatePattern('^/')]
    [string]$ExpectedCallbackPath = '/signin-oidc',
    [string]$ExpectedAuthorityHost = 'login.microsoftonline.com',
    # Supplied from the resolvedIngressMode output of infra/poc/main.bicep.
    [ValidateSet('Public', 'Private')]
    [string]$ExpectedIngressMode = 'Public',
    # Supplied from the resolvedAppServicePlanSkuName output of infra/poc/main.bicep.
    [ValidateSet('F1', 'D1', 'B1', 'B2', 'B3', 'S1', 'S2', 'S3', 'P0v3', 'P1v3', 'P2v3', 'P3v3')]
    [string]$ExpectedAppServicePlanSkuName = 'B1',
    [ValidateSet('All', 'Ingress', 'Posture')]
    [string]$Assertion = 'All',
    # Asserts that the caller has no private route into the VNet hosting any private endpoint.
    # Without it the Private posture check cannot establish its precondition and reports
    # not-executed rather than pass.
    [switch]$UnconnectedClient,
    [ValidateRange(5, 120)]
    [int]$TimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$appHostName = "$AppName.azurewebsites.net"
$scmHostName = "$AppName.scm.azurewebsites.net"
$appBaseUrl = "https://$appHostName"
$expectedRedirectUri = "$appBaseUrl$ExpectedCallbackPath"
$challengeUrl = "$appBaseUrl$ChallengePath"

# Verdicts and details are closed sets. Anything outside them is reduced to a placeholder so no
# response body, header value, or Azure CLI payload can reach output through a message string.
$allowedDetails = @(
    'app-hostname-resolved',
    'scm-hostname-resolved',
    'dns-resolution-failed',
    'tcp-443-open',
    'tcp-443-unreachable',
    'tls-handshake-failed',
    'tls-validated',
    'challenge-shape-valid',
    'challenge-not-a-redirect',
    'challenge-authority-unexpected',
    'challenge-client-id-unexpected',
    'challenge-redirect-uri-unexpected',
    'challenge-pkce-method-not-s256',
    'http-forbidden',
    'http-unexpected-status',
    'http-request-failed',
    'ingress-posture-matches-template',
    'ingress-posture-drift',
    'control-plane-read-denied',
    'control-plane-read-failed',
    'azure-cli-unavailable',
    'azure-cli-not-authenticated',
    'plan-tier-degraded',
    'site-not-running',
    'forbidden-cause-undetermined',
    'public-reachability-is-accepted-poc-posture',
    'public-reachability-lost',
    'unconnected-client-condition-not-established',
    'endpoints-not-usable-from-unconnected-client',
    'private-posture-violated-publicly-usable',
    'unspecified')

# Each 403 cause carries one named remedy. A blind redeploy is correct for exactly one of them,
# so prescribing it for all three would send an operator down the wrong path twice out of three.
$forbiddenRemedies = @{
    'plan-tier-degraded' = 'Redeploy infra/poc/main.bicep with appServicePlanSkuName at the declared SKU. An external process returns this plan to F1 on roughly a 24 hour cycle, so expect to repeat this.'
    'ingress-posture-drift' = 'Redeploy infra/poc/main.bicep with ingressMode Public. The template owns publicNetworkAccess; do not set it imperatively.'
    'site-not-running' = 'Start the site, then re-run this check. Neither a redeploy nor an ingress change addresses a stopped site.'
    'control-plane-read-denied' = 'The read of the site itself was denied at the management plane. That is a different failure from an application-layer 403; resolve the caller access to the resource group before diagnosing the site.'
    'control-plane-read-failed' = 'The control plane could not be read, so the cause is undetermined. Restore Azure CLI authentication for the target subscription and re-run.'
    'forbidden-cause-undetermined' = 'Plan tier, ingress posture, and site state all match the template. Capture the response and escalate rather than redeploying blindly.'
}

$script:passCount = 0
$script:failCount = 0
$script:notExecutedCount = 0

function Write-Verdict {
    param(
        [Parameter(Mandatory)][string]$Check,
        [Parameter(Mandatory)][ValidateSet('pass', 'fail', 'not-executed')][string]$Verdict,
        [Parameter(Mandatory)][string]$Detail
    )

    $safeDetail = if ($Detail -cin $allowedDetails) { $Detail } else { 'unspecified' }
    switch ($Verdict) {
        'pass' { $script:passCount++ }
        'fail' { $script:failCount++ }
        'not-executed' { $script:notExecutedCount++ }
    }

    Write-Output "check=$Check verdict=$Verdict detail=$safeDetail"
}

function Write-Note {
    param([Parameter(Mandatory)][string]$Message)

    Write-Output "note=$Message"
}

function ConvertFrom-QueryString {
    param([Parameter(Mandatory)][string]$Query)

    $parameters = @{}
    foreach ($pair in $Query.TrimStart('?').Split(
            '&',
            [System.StringSplitOptions]::RemoveEmptyEntries)) {
        $parts = $pair.Split('=', 2)
        $name = [uri]::UnescapeDataString($parts[0].Replace('+', ' '))
        $value = if ($parts.Count -eq 2) {
            [uri]::UnescapeDataString($parts[1].Replace('+', ' '))
        }
        else {
            ''
        }
        $parameters[$name] = $value
    }
    return $parameters
}

function Get-PropertyValue {
    param(
        [object]$InputObject,
        [Parameter(Mandatory)][string]$Name
    )

    if ($null -eq $InputObject) {
        return $null
    }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }
    return $property.Value
}

function Invoke-AzureRead {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $raw = & az @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $text = $raw -join [Environment]::NewLine
    if ($exitCode -ne 0) {
        # A management-plane denial is a distinct failure class from an application-layer 403 and
        # is classified here so the two are never reported as the same condition.
        $denied = [regex]::IsMatch(
            $text,
            '(?i)AuthorizationFailed|RequestDisallowedByPolicy|does not have authorization|Forbidden')
        return [pscustomobject]@{
            Succeeded = $false
            Denied = $denied
            Value = $null
        }
    }

    $value = $null
    if (-not [string]::IsNullOrWhiteSpace($text)) {
        $value = $text | ConvertFrom-Json
    }
    return [pscustomobject]@{
        Succeeded = $true
        Denied = $false
        Value = $value
    }
}

function Test-HostResolution {
    param([Parameter(Mandatory)][string]$Target)

    try {
        $addresses = [System.Net.Dns]::GetHostAddresses($Target)
        return @($addresses).Count -gt 0
    }
    catch {
        return $false
    }
}

function Test-TlsEndpoint {
    param(
        [Parameter(Mandatory)][string]$Target,
        [Parameter(Mandatory)][int]$Port
    )

    $client = $null
    $stream = $null
    $secureStream = $null
    try {
        $client = [System.Net.Sockets.TcpClient]::new()
        $connectTask = $client.ConnectAsync($Target, $Port)
        if (-not $connectTask.Wait([TimeSpan]::FromSeconds($TimeoutSeconds))) {
            return [pscustomobject]@{ Connected = $false; Outcome = 'tcp-443-unreachable' }
        }

        $stream = $client.GetStream()
        # No certificate validation callback is supplied, so the default chain and name validation
        # applies. TLS validation is never relaxed to make a probe succeed.
        $secureStream = [System.Net.Security.SslStream]::new($stream, $false)
        $secureStream.AuthenticateAsClient($Target)
        return [pscustomobject]@{ Connected = $true; Outcome = 'tls-validated' }
    }
    catch [System.Security.Authentication.AuthenticationException] {
        return [pscustomobject]@{ Connected = $true; Outcome = 'tls-handshake-failed' }
    }
    catch {
        return [pscustomobject]@{ Connected = $false; Outcome = 'tcp-443-unreachable' }
    }
    finally {
        if ($null -ne $secureStream) { $secureStream.Dispose() }
        if ($null -ne $stream) { $stream.Dispose() }
        if ($null -ne $client) { $client.Dispose() }
    }
}

function Invoke-ChallengeProbe {
    param([Parameter(Mandatory)][string]$Url)

    $handler = $null
    $client = $null
    $response = $null
    try {
        $handler = [System.Net.Http.HttpClientHandler]::new()
        # Redirects stay off so the challenge Location header is inspected rather than followed.
        $handler.AllowAutoRedirect = $false
        $client = [System.Net.Http.HttpClient]::new($handler)
        $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)
        $response = $client.GetAsync($Url).GetAwaiter().GetResult()

        $status = [int]$response.StatusCode
        # Presence only. The Set-Cookie value itself is read into no variable that leaves this scope.
        $setCookiePresent = $response.Headers.Contains('Set-Cookie')
        $location = $response.Headers.Location
        $unavailableSignature = $false
        if ($status -eq 403) {
            $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            $unavailableSignature = [regex]::IsMatch(
                $body,
                '(?i)<title>\s*Web App - Unavailable\s*</title>')
            $body = $null
        }

        return [pscustomobject]@{
            Succeeded = $true
            Status = $status
            Location = $location
            SetCookiePresent = $setCookiePresent
            UnavailableSignature = $unavailableSignature
        }
    }
    catch {
        return [pscustomobject]@{
            Succeeded = $false
            Status = 0
            Location = $null
            SetCookiePresent = $false
            UnavailableSignature = $false
        }
    }
    finally {
        if ($null -ne $response) { $response.Dispose() }
        if ($null -ne $client) { $client.Dispose() }
        if ($null -ne $handler) { $handler.Dispose() }
    }
}

function Test-ChallengeShape {
    param([Parameter(Mandatory)][object]$Probe)

    $redirectStatuses = @(301, 302, 303, 307, 308)
    if ($Probe.Status -notin $redirectStatuses) {
        return 'challenge-not-a-redirect'
    }

    $location = $Probe.Location
    if ($null -eq $location -or -not $location.IsAbsoluteUri -or
        $location.Scheme -cne 'https' -or
        $location.Host -cne $ExpectedAuthorityHost) {
        return 'challenge-authority-unexpected'
    }

    $query = ConvertFrom-QueryString -Query $location.Query
    if (-not $query.ContainsKey('client_id') -or $query['client_id'] -cne $ExpectedClientId) {
        return 'challenge-client-id-unexpected'
    }
    if (-not $query.ContainsKey('redirect_uri') -or $query['redirect_uri'] -cne $expectedRedirectUri) {
        return 'challenge-redirect-uri-unexpected'
    }
    if (-not $query.ContainsKey('code_challenge_method') -or $query['code_challenge_method'] -cne 'S256') {
        return 'challenge-pkce-method-not-s256'
    }

    return 'challenge-shape-valid'
}

function Resolve-ForbiddenCause {
    param(
        [Parameter(Mandatory)][object]$Site,
        [Parameter(Mandatory)][object]$Plan
    )

    if (-not $Site.Succeeded) {
        $cause = if ($Site.Denied) { 'control-plane-read-denied' } else { 'control-plane-read-failed' }
        return $cause
    }

    $planSku = Get-PropertyValue -InputObject $Plan.Value -Name 'skuName'
    if ($Plan.Succeeded -and $null -ne $planSku -and
        $planSku -cin @('F1', 'D1') -and $planSku -cne $ExpectedAppServicePlanSkuName) {
        return 'plan-tier-degraded'
    }

    $sitePublicAccess = Get-PropertyValue -InputObject $Site.Value -Name 'publicNetworkAccess'
    if ($ExpectedIngressMode -ceq 'Public' -and $sitePublicAccess -ceq 'Disabled') {
        return 'ingress-posture-drift'
    }

    $siteState = Get-PropertyValue -InputObject $Site.Value -Name 'state'
    if ($null -ne $siteState -and $siteState -cne 'Running') {
        return 'site-not-running'
    }

    return 'forbidden-cause-undetermined'
}

Write-Output "app=$AppName resource-group=$ResourceGroupName expected-ingress-mode=$ExpectedIngressMode assertion=$Assertion"

$site = [pscustomobject]@{ Succeeded = $false; Denied = $false; Value = $null }
$plan = [pscustomobject]@{ Succeeded = $false; Denied = $false; Value = $null }
$controlPlaneDetail = 'control-plane-read-failed'

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    $controlPlaneDetail = 'azure-cli-unavailable'
}
else {
    $accountRead = Invoke-AzureRead -Arguments @('account', 'show', '--output', 'json', '--only-show-errors')
    if (-not $accountRead.Succeeded) {
        $controlPlaneDetail = 'azure-cli-not-authenticated'
    }
    else {
        $site = Invoke-AzureRead -Arguments @(
            'webapp', 'show',
            '--resource-group', $ResourceGroupName,
            '--name', $AppName,
            '--output', 'json',
            '--only-show-errors')
        if ($site.Succeeded) {
            $serverFarmId = Get-PropertyValue -InputObject $site.Value -Name 'serverFarmId'
            if (-not [string]::IsNullOrWhiteSpace([string]$serverFarmId)) {
                $plan = Invoke-AzureRead -Arguments @(
                    'appservice', 'plan', 'show',
                    '--ids', [string]$serverFarmId,
                    '--query', '{skuName:sku.name,skuTier:sku.tier}',
                    '--output', 'json',
                    '--only-show-errors')
            }
        }
        elseif ($site.Denied) {
            $controlPlaneDetail = 'control-plane-read-denied'
        }
    }
}

$appResolved = Test-HostResolution -Target $appHostName
Write-Verdict `
    -Check 'dns-app' `
    -Verdict $(if ($appResolved) { 'pass' } else { 'fail' }) `
    -Detail $(if ($appResolved) { 'app-hostname-resolved' } else { 'dns-resolution-failed' })

$scmResolved = Test-HostResolution -Target $scmHostName
Write-Verdict `
    -Check 'dns-scm' `
    -Verdict $(if ($scmResolved) { 'pass' } else { 'fail' }) `
    -Detail $(if ($scmResolved) { 'scm-hostname-resolved' } else { 'dns-resolution-failed' })

if ($ExpectedIngressMode -ceq 'Private') {
    Write-Note 'a-public-dns-answer-is-not-evidence-of-public-application-access'
}

$appTls = Test-TlsEndpoint -Target $appHostName -Port 443
$scmTls = Test-TlsEndpoint -Target $scmHostName -Port 443

if ($ExpectedIngressMode -ceq 'Public') {
    # A completed TCP connection is not enough. The handshake must also validate, because a probe
    # that passes only by relaxing TLS would prove nothing about the endpoint an operator uses.
    Write-Verdict `
        -Check 'tcp-443-app' `
        -Verdict $(if ($appTls.Outcome -ceq 'tls-validated') { 'pass' } else { 'fail' }) `
        -Detail $appTls.Outcome
    Write-Verdict `
        -Check 'tcp-443-scm' `
        -Verdict $(if ($scmTls.Outcome -ceq 'tls-validated') { 'pass' } else { 'fail' }) `
        -Detail $scmTls.Outcome
}
elseif (-not $UnconnectedClient) {
    Write-Verdict -Check 'tcp-443-app' -Verdict 'not-executed' -Detail 'unconnected-client-condition-not-established'
    Write-Verdict -Check 'tcp-443-scm' -Verdict 'not-executed' -Detail 'unconnected-client-condition-not-established'
}
else {
    Write-Verdict `
        -Check 'tcp-443-app' `
        -Verdict $(if ($appTls.Connected) { 'fail' } else { 'pass' }) `
        -Detail $(if ($appTls.Connected) { 'private-posture-violated-publicly-usable' } else { 'endpoints-not-usable-from-unconnected-client' })
    Write-Verdict `
        -Check 'tcp-443-scm' `
        -Verdict $(if ($scmTls.Connected) { 'fail' } else { 'pass' }) `
        -Detail $(if ($scmTls.Connected) { 'private-posture-violated-publicly-usable' } else { 'endpoints-not-usable-from-unconnected-client' })
}

$challengeProbe = $null
if ($Assertion -ceq 'Posture') {
    Write-Verdict -Check 'challenge-shape' -Verdict 'not-executed' -Detail 'unspecified'
}
elseif ($ExpectedIngressMode -ceq 'Private') {
    # An unconnected client cannot reach the challenge route by design, so asserting its shape here
    # would report a routing condition as an authentication defect.
    Write-Verdict -Check 'challenge-shape' -Verdict 'not-executed' -Detail 'unconnected-client-condition-not-established'
}
else {
    Write-Output "challenge-url=$challengeUrl"
    $challengeProbe = Invoke-ChallengeProbe -Url $challengeUrl
    if (-not $challengeProbe.Succeeded) {
        Write-Verdict -Check 'challenge-shape' -Verdict 'fail' -Detail 'http-request-failed'
    }
    elseif ($challengeProbe.Status -eq 403) {
        Write-Verdict -Check 'challenge-shape' -Verdict 'fail' -Detail 'http-forbidden'
        # Corroboration only. A "Web App - Unavailable" title is the disabled-public-access signature,
        # but the authoritative cause below comes from control-plane state, because the body marker
        # cannot separate drift from a deliberate Private posture or from a management-plane denial.
        if ($challengeProbe.UnavailableSignature) {
            Write-Note 'observed-body-signature=web-app-unavailable'
        }
        else {
            Write-Note 'observed-body-signature=none'
        }
        $cause = Resolve-ForbiddenCause -Site $site -Plan $plan
        Write-Output "diagnosis=$cause"
        Write-Output "remedy=$($forbiddenRemedies[$cause])"
    }
    else {
        $shape = Test-ChallengeShape -Probe $challengeProbe
        Write-Verdict `
            -Check 'challenge-shape' `
            -Verdict $(if ($shape -ceq 'challenge-shape-valid') { 'pass' } else { 'fail' }) `
            -Detail $shape
        if ($shape -cne 'challenge-shape-valid' -and $challengeProbe.Status -ge 400) {
            Write-Verdict -Check 'challenge-status' -Verdict 'fail' -Detail 'http-unexpected-status'
        }
        Write-Note "set-cookie-header-present=$($challengeProbe.SetCookiePresent.ToString().ToLowerInvariant())"
    }
}

$ingressPostureMatches = $null
if (-not $site.Succeeded) {
    Write-Verdict -Check 'ingress-posture-drift' -Verdict 'not-executed' -Detail $controlPlaneDetail
}
else {
    $expectedPublicNetworkAccess = if ($ExpectedIngressMode -ceq 'Public') { 'Enabled' } else { 'Disabled' }
    $sitePublicAccess = [string](Get-PropertyValue -InputObject $site.Value -Name 'publicNetworkAccess')
    $siteConfig = Get-PropertyValue -InputObject $site.Value -Name 'siteConfig'
    $siteConfigPublicAccess = [string](Get-PropertyValue -InputObject $siteConfig -Name 'publicNetworkAccess')
    # Either layer reporting Disabled disables the site, so both are compared against the template.
    $effectivePublicAccess = if ($sitePublicAccess -ceq 'Disabled' -or $siteConfigPublicAccess -ceq 'Disabled') {
        'Disabled'
    }
    elseif ([string]::IsNullOrWhiteSpace($sitePublicAccess)) {
        'Enabled'
    }
    else {
        $sitePublicAccess
    }

    $postureMatches = $effectivePublicAccess -ceq $expectedPublicNetworkAccess
    $ingressPostureMatches = $postureMatches
    Write-Verdict `
        -Check 'ingress-posture-drift' `
        -Verdict $(if ($postureMatches) { 'pass' } else { 'fail' }) `
        -Detail $(if ($postureMatches) { 'ingress-posture-matches-template' } else { 'ingress-posture-drift' })
    if (-not $postureMatches) {
        Write-Output "remedy=$($forbiddenRemedies['ingress-posture-drift'])"
    }
}

if ($Assertion -cne 'Ingress') {
    Write-Output "posture-verified=$ExpectedIngressMode"
    if ($ExpectedIngressMode -ceq 'Public') {
        # Transport reachability alone would report a pass while publicNetworkAccess is Disabled, so
        # the posture in force is part of the verdict rather than a separate line an operator may miss.
        if ($null -eq $ingressPostureMatches) {
            Write-Verdict -Check 'posture-public' -Verdict 'not-executed' -Detail $controlPlaneDetail
        }
        elseif (-not $ingressPostureMatches) {
            Write-Verdict -Check 'posture-public' -Verdict 'fail' -Detail 'ingress-posture-drift'
        }
        else {
            $publiclyReachable = $appResolved -and $appTls.Outcome -ceq 'tls-validated'
            Write-Verdict `
                -Check 'posture-public' `
                -Verdict $(if ($publiclyReachable) { 'pass' } else { 'fail' }) `
                -Detail $(if ($publiclyReachable) { 'public-reachability-is-accepted-poc-posture' } else { 'public-reachability-lost' })
        }
        Write-Note 'public-ingress-is-the-deliberate-poc-posture-recorded-under-ID-01'
        Write-Note 'this-poc-does-not-demonstrate-private-ingress'
    }
    elseif (-not $UnconnectedClient) {
        Write-Verdict -Check 'posture-private' -Verdict 'not-executed' -Detail 'unconnected-client-condition-not-established'
        Write-Note 'private-posture-was-not-verified-because-the-caller-did-not-assert-an-unconnected-client'
    }
    else {
        $privatelyIsolated = -not $appTls.Connected -and -not $scmTls.Connected
        Write-Verdict `
            -Check 'posture-private' `
            -Verdict $(if ($privatelyIsolated) { 'pass' } else { 'fail' }) `
            -Detail $(if ($privatelyIsolated) { 'endpoints-not-usable-from-unconnected-client' } else { 'private-posture-violated-publicly-usable' })
        Write-Note 'a-public-dns-answer-is-not-evidence-of-public-application-access'
    }
}

Write-Output "summary pass=$script:passCount fail=$script:failCount not-executed=$script:notExecutedCount"

if ($script:failCount -gt 0) {
    Write-Output 'Ingress verification FAILED'
    exit 1
}

Write-Output 'Ingress verification checks OK'
exit 0
