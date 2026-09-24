<#
.SYNOPSIS
    Fails unless the most recent App Service deployment recorded a Kudu status of Success.

.DESCRIPTION
    The Azure CLI and azure/webapps-deploy report the outcome of the upload, not the outcome of the
    deployment. A package that uploads cleanly and then fails to install still returns exit code 0,
    leaving the previous build serving traffic while every downstream check passes against content
    that was never shipped. That failure mode is silent, so it has to be asserted rather than
    observed.

    The authoritative record is the Kudu deployment status, where 3 is Failed and 4 is Success.
    This script reads it through the Azure Resource Manager deployments collection rather than the
    Kudu endpoint directly, so it needs no publishing credentials and reuses whatever identity
    az is already signed in as.

    Non-terminal statuses are polled rather than treated as a verdict, because the caller usually
    runs immediately after an upload and the install is still in progress.

    Pass -Since to reject a stale record. Without it, a deployment that never started at all
    reports the previous successful one and the check passes for the wrong reason.

.EXAMPLE
    ./scripts/assert-deployment-succeeded.ps1 -AppName croesus-spa, croesus-api

.EXAMPLE
    $started = [datetime]::UtcNow
    az webapp deploy -g rg -n app --src-path app.zip --type zip
    ./scripts/assert-deployment-succeeded.ps1 -AppName app -Since $started
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]]$AppName,

    # Resolved from the first app when omitted, so a caller that knows only the site names needs no
    # further configuration and cannot drift out of step with the deployment.
    [string]$ResourceGroupName,

    [string]$SubscriptionId,

    [string]$Slot,

    # Rejects any deployment that finished before this instant. Supply the time immediately before
    # the upload began.
    [datetime]$Since,

    [int]$TimeoutSeconds = 600,

    [int]$PollIntervalSeconds = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The Kudu deployment status enum. Only 3 and 4 are terminal.
$statusNames = @{
    0 = 'Pending'
    1 = 'Building'
    2 = 'Deploying'
    3 = 'Failed'
    4 = 'Success'
}

function Invoke-Az {
    param([Parameter(Mandatory)][string[]]$Arguments)

    # az writes warnings and deprecation notices to stderr on otherwise successful calls, so the
    # streams are separated rather than merged. Merging them puts non-JSON text in front of the
    # payload and ConvertFrom-Json fails on a command that actually succeeded.
    $stdout = [System.Collections.Generic.List[string]]::new()
    $stderr = [System.Collections.Generic.List[string]]::new()

    & az @Arguments -o json 2>&1 | ForEach-Object {
        if ($_ -is [System.Management.Automation.ErrorRecord]) { $stderr.Add($_.ToString()) }
        else { $stdout.Add([string]$_) }
    }

    if ($LASTEXITCODE -ne 0) {
        $detail = (($stderr + $stdout) -join [Environment]::NewLine)
        throw "az $($Arguments -join ' ') failed with exit code $LASTEXITCODE`n$detail"
    }

    $text = ($stdout -join [Environment]::NewLine).Trim()
    if (-not $text) { return $null }
    return $text | ConvertFrom-Json
}

function Get-LatestDeployment {
    param([Parameter(Mandatory)][string]$Url)

    $response = Invoke-Az @('rest', '--method', 'get', '--url', $Url)
    if (-not $response -or -not $response.value) { return $null }

    # The collection is not guaranteed to be ordered, and a record that has not finished carries no
    # end_time, so received_time is the only field present on every entry.
    return $response.value |
        Sort-Object -Property { [datetime]$_.properties.received_time } -Descending |
        Select-Object -First 1
}

if (-not $SubscriptionId) {
    $SubscriptionId = (Invoke-Az @('account', 'show', '--query', 'id')).ToString()
}

if (-not $ResourceGroupName) {
    $candidates = Invoke-Az @('webapp', 'list', '--query', "[?name=='$($AppName[0])'].resourceGroup")
    if (-not $candidates -or @($candidates).Count -ne 1) {
        throw "Could not resolve a unique resource group for site '$($AppName[0])'. Pass -ResourceGroupName."
    }
    $ResourceGroupName = @($candidates)[0]
    Write-Verbose "Resolved resource group '$ResourceGroupName' from site '$($AppName[0])'."
}

$results = [System.Collections.Generic.List[pscustomobject]]::new()

foreach ($site in $AppName) {
    $resourcePath = "subscriptions/$SubscriptionId/resourceGroups/$ResourceGroupName/providers/Microsoft.Web/sites/$site"
    if ($Slot) { $resourcePath += "/slots/$Slot" }
    $url = "https://management.azure.com/$resourcePath/deployments?api-version=2023-12-01"

    $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSeconds)
    $latest = $null
    $status = $null

    while ($true) {
        $latest = Get-LatestDeployment -Url $url
        $status = if ($latest) { [int]$latest.properties.status } else { $null }

        if ($null -ne $status -and $status -in 3, 4) { break }
        if ([datetime]::UtcNow -ge $deadline) { break }

        $label = if ($null -eq $status) { 'no deployment recorded' } else { $statusNames[$status] }
        Write-Verbose "$site is $label. Waiting $PollIntervalSeconds seconds."
        Start-Sleep -Seconds $PollIntervalSeconds
    }

    if (-not $latest) {
        throw "Site '$site' has no deployment records. The package was never received."
    }

    $endTime = if ($latest.properties.end_time) { [datetime]$latest.properties.end_time } else { $null }

    $results.Add([pscustomobject]@{
            Site    = $site
            Id      = ($latest.name -split '/')[-1]
            Status  = if ($statusNames.ContainsKey($status)) { $statusNames[$status] } else { "Unknown ($status)" }
            Active  = [bool]$latest.properties.active
            EndTime = $endTime
            Message = $latest.properties.message
        })

    if ($status -ne 4) {
        $label = if ($statusNames.ContainsKey($status)) { $statusNames[$status] } else { "Unknown ($status)" }
        throw "Deployment '$($latest.name)' on site '$site' reported status $status ($label). The previously deployed build is still serving traffic."
    }

    if ($PSBoundParameters.ContainsKey('Since')) {
        $sinceUtc = $Since.ToUniversalTime()
        if (-not $endTime) {
            throw "Deployment '$($latest.name)' on site '$site' reports Success with no end time, so it cannot be shown to be newer than $($sinceUtc.ToString('o'))."
        }
        if ($endTime.ToUniversalTime() -lt $sinceUtc) {
            throw "The newest deployment on site '$site' finished at $($endTime.ToUniversalTime().ToString('o')), before the expected start of $($sinceUtc.ToString('o')). No new package was deployed."
        }
    }

    if (-not $latest.properties.active) {
        Write-Warning "Deployment '$($latest.name)' on site '$site' succeeded but is not the active deployment."
    }
}

$results | Format-Table -AutoSize | Out-String | Write-Host

Write-Host "All $($results.Count) deployment(s) reported Kudu status 4 (Success)."
