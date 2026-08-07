[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [string]$StatePath = '.classic-net-bff-deployment-state.json',
    [string]$DisplayName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$graphBaseUri = 'https://graph.microsoft.com/v1.0'
$ownershipTag = 'Croesus.ClassicNetBffDeployment.v1'
$fullStatePath = [System.IO.Path]::GetFullPath($StatePath)

function Write-Status {
    param([Parameter(Mandatory)][string]$Message)

    Write-Information ">>> $Message" -InformationAction Continue
}

function Invoke-Graph {
    param(
        [Parameter(Mandatory)][ValidateSet('POST', 'DELETE')][string]$Method,
        [Parameter(Mandatory)][string]$Uri,
        [object]$Body
    )

    $arguments = @('rest', '--method', $Method, '--uri', $Uri, '--output', 'none', '--only-show-errors')
    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 10 -Compress
        $arguments += @('--headers', 'Content-Type=application/json', '--body', $json)
    }

    & az @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Microsoft Graph request failed: $Method $Uri"
    }
}

function Find-WorkflowManagedApplication {
    param([Parameter(Mandatory)][string]$ExpectedDisplayName)

    if ([string]::IsNullOrWhiteSpace($ExpectedDisplayName)) {
        throw '-DisplayName cannot be empty when provisioning state is unavailable.'
    }

    $escapedDisplayName = $ExpectedDisplayName.Replace("'", "''")
    $uri = "$graphBaseUri/applications?`$filter=displayName eq '$escapedDisplayName'&`$select=id,appId,displayName,tags,passwordCredentials"
    $response = Get-GraphObject -Uri $uri
    $applicationMatches = @($response.value)
    if ($applicationMatches.Count -ne 1) {
        throw "Expected exactly one app registration named '$ExpectedDisplayName'; found $($applicationMatches.Count)."
    }

    $application = $applicationMatches[0]
    if ($ownershipTag -notin @($application.tags)) {
        throw "The app registration '$ExpectedDisplayName' is not tagged as workflow-managed. Refusing deletion."
    }

    return $application
}

function Get-GraphObject {
    param([Parameter(Mandatory)][string]$Uri)

    $response = & az rest --method GET --uri $Uri --output json --only-show-errors 2>&1
    if ($LASTEXITCODE -eq 0) {
        return ($response -join [Environment]::NewLine) | ConvertFrom-Json
    }

    $message = $response -join [Environment]::NewLine
    if ($message -match '404|Request_ResourceNotFound') {
        return $null
    }

    throw "Microsoft Graph lookup failed: GET $Uri`n$message"
}

function Save-State {
    $script:state | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $script:fullStatePath -Encoding utf8NoBOM
}

function Assert-ApplicationIdentity {
    param([Parameter(Mandatory)][object]$Application)

    if ([string]$Application.id -ne [string]$script:state.application.objectId -or
        [string]$Application.appId -ne [string]$script:state.application.clientId -or
        [string]$Application.displayName -ne [string]$script:state.displayName) {
        throw 'The current app registration does not match the object, client, and display-name identity recorded in state.'
    }
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI is required. Authenticate the workflow with GitHub OIDC before running cleanup.'
}

$stateFileExists = Test-Path -LiteralPath $fullStatePath -PathType Leaf
if ($stateFileExists) {
    $state = Get-Content -LiteralPath $fullStatePath -Raw | ConvertFrom-Json
    if ([int]$state.schemaVersion -ne 1) {
        throw "Unsupported state schema version: $($state.schemaVersion)"
    }
}

$accountJson = & az account show --output json --only-show-errors
if ($LASTEXITCODE -ne 0) {
    throw 'Azure CLI is not authenticated. Authenticate the workflow for the recorded home tenant.'
}
$account = ($accountJson -join [Environment]::NewLine) | ConvertFrom-Json
if ($stateFileExists -and [string]$account.tenantId -ne [string]$state.tenantId) {
    throw "Azure CLI is signed in to tenant $($account.tenantId), but the state belongs to tenant $($state.tenantId)."
}

if ($stateFileExists) {
    $applicationObjectId = [string]$state.application.objectId
    $application = Get-GraphObject -Uri "$graphBaseUri/applications/$applicationObjectId?`$select=id,appId,displayName,tags,passwordCredentials"
    if ($null -ne $application) {
        Assert-ApplicationIdentity -Application $application
    }
}
else {
    $application = Find-WorkflowManagedApplication -ExpectedDisplayName $DisplayName
    $applicationObjectId = [string]$application.id
    $servicePrincipalResponse = Get-GraphObject -Uri "$graphBaseUri/servicePrincipals?`$filter=appId eq '$($application.appId)'&`$select=id,appId"
    $servicePrincipalMatches = @($servicePrincipalResponse.value)
    if ($servicePrincipalMatches.Count -gt 1) {
        throw "More than one home-tenant service principal exists for application ID $($application.appId)."
    }
    $state = [ordered]@{
        schemaVersion = 1
        tenantId = [string]$account.tenantId
        displayName = [string]$application.displayName
        application = [ordered]@{
            objectId = $applicationObjectId
            clientId = [string]$application.appId
            created = $true
        }
        servicePrincipal = if ($servicePrincipalMatches.Count -eq 1) {
            [ordered]@{
                objectId = [string]$servicePrincipalMatches[0].id
                created = $true
            }
        }
        else {
            $null
        }
        passwordCredential = $null
    }
}

if ($stateFileExists -and $null -ne $state.passwordCredential -and [bool]$state.passwordCredential.created) {
    $keyId = [string]$state.passwordCredential.keyId
    if ($PSCmdlet.ShouldProcess("password credential $keyId", 'Remove recorded deployment credential')) {
        if ($null -ne $application) {
            Write-Status 'Removing the recorded deployment credential'
            Invoke-Graph -Method POST -Uri "$graphBaseUri/applications/$applicationObjectId/removePassword" -Body @{
                keyId = $keyId
            }
        }
        else {
            Write-Status 'The owning app registration is already absent; the recorded deployment credential is also gone'
        }
        $state.passwordCredential.created = $false
        Save-State
    }
}
else {
    Write-Status 'No script-created deployment credential is recorded; skipping credential removal'
}

if ($null -ne $state.servicePrincipal -and [bool]$state.servicePrincipal.created) {
    $servicePrincipalObjectId = [string]$state.servicePrincipal.objectId
    if ($PSCmdlet.ShouldProcess("service principal $servicePrincipalObjectId", 'Delete script-created service principal')) {
        $servicePrincipal = Get-GraphObject -Uri "$graphBaseUri/servicePrincipals/$servicePrincipalObjectId?`$select=id,appId"
        if ($null -ne $servicePrincipal) {
            if ([string]$servicePrincipal.id -ne $servicePrincipalObjectId -or
                [string]$servicePrincipal.appId -ne [string]$state.application.clientId) {
                throw 'The current service principal does not match the object and application identity recorded in state.'
            }
            Write-Status 'Deleting the script-created home-tenant service principal'
            Invoke-Graph -Method DELETE -Uri "$graphBaseUri/servicePrincipals/$servicePrincipalObjectId"
        }
        else {
            Write-Status 'The script-created home-tenant service principal is already absent'
        }
        $state.servicePrincipal.created = $false
        if ($stateFileExists) {
            Save-State
        }
    }
}
else {
    Write-Status 'The home-tenant service principal was reused; leaving it intact'
}

if ([bool]$state.application.created) {
    if ($PSCmdlet.ShouldProcess("app registration $applicationObjectId", 'Delete script-created app registration')) {
        if ($null -ne $application) {
            Write-Status 'Deleting the script-created app registration'
            Invoke-Graph -Method DELETE -Uri "$graphBaseUri/applications/$applicationObjectId"
        }
        else {
            Write-Status 'The script-created app registration is already absent'
        }
        $state.application.created = $false
        if ($stateFileExists) {
            Save-State
        }
    }
}
else {
    Write-Status 'The app registration was reused; leaving it intact'
}

if ($stateFileExists) {
    Write-Status "Cleanup complete. Review and remove the non-secret state file when it is no longer needed: $fullStatePath"
}
else {
    Write-Status "Cleanup complete for workflow-managed app registration '$DisplayName'."
}