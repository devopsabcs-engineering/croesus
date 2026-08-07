---
title: Classic .NET BFF Comparison PoC
description: Provision and run the .NET Framework 4.5.2 and .NET 10 confidential web client comparison
ms.date: 2026-08-07
ms.topic: how-to
---

## Purpose and recommendation

This proof compares the same confidential OpenID Connect web-client boundary on
two runtimes. The .NET Framework 4.5.2 host establishes protocol feasibility
with Katana. The .NET 10 host demonstrates the supported strategic destination
with Microsoft.Identity.Web and MSAL.NET.

Use the sequence below for Central:

1. Prove the confidential `web` registration in Dev on .NET Framework 4.5.2.
2. Use .NET Framework 4.8 only as an operational bridge when migration timing
   requires one.
3. Move the authentication boundary to .NET 10 as the strategic destination.

> [!WARNING]
> .NET Framework 4.5.2 has been unsupported since April 26, 2022. The legacy
> sample is evidence code, not an Internet-facing or production destination.

Installing the .NET Framework 4.8 runtime does not change an application's
target framework. A 4.8 uplift requires retargeting the project, recompiling it,
and regression testing authentication, cookies, session state, TLS behavior,
and the application. Treat installation and application migration as separate
changes.

The [three-way session findings](../assets/croesus-3way-session-findings.md)
remain the authoritative Central assessment. This PoC implements the R8 and R9
proof path without changing the existing OBO demonstration.

## Architecture

One confidential Entra application registration contains two exact callbacks
under `web.redirectUris`:

* `https://localhost:44352/signin-oidc` for the classic System.Web host
* `https://localhost:7100/signin-oidc` for the ASP.NET Core host

Both hosts own authorization-code redemption and establish a protected
server-side session. Browser JavaScript receives no OAuth token, authorization
code, PKCE verifier, client credential, or raw cookie. The core comparison uses
only OpenID Connect sign-in and requests no downstream API permission.

```mermaid
sequenceDiagram
    participant U as Browser
    participant B as BFF host
    participant E as Microsoft Entra
    U->>B: Request protected page
    B-->>U: Redirect to authorize endpoint with PKCE challenge
    U->>E: Authenticate and consent when required
    E-->>U: Redirect with authorization code
    U->>B: Exact HTTPS callback with code and protected state
    B->>E: Server-side code redemption with client credential and PKCE verifier
    E-->>B: Validated identity tokens
    B-->>U: Secure HTTP-only session cookie
    U->>B: Request session projection
    B-->>U: Authentication state, display name, and tenant ID only
```

The legacy middleware uses native Katana PKCE and code redemption. It does not
contain custom PKCE glue or a custom verifier store. The modern host delegates
the protocol flow to Microsoft.Identity.Web. Neither sample exposes an API
scope or implements On-Behalf-Of.

## Prerequisites

* PowerShell 7 on Windows
* Azure CLI authenticated to the intended home tenant
* Permission to create applications and service principals in that tenant
* .NET SDK 10 with .NET Framework 4.5.2 reference assemblies restored by NuGet
* Visual Studio with IIS Express and a trusted development certificate for the
  legacy host

The provisioning scripts use `az account show` for the authenticated tenant and
`az rest` for Microsoft Graph directory operations. They do not call
`az ad sp create-for-rbac`, create subscription scopes, or assign Azure resource
roles.

## Provision the single-tenant registration

Run these commands from the repository root. The default audience is
`AzureADMyOrg`. The state file contains only tenant, application, service
principal, and credential identifiers plus ownership flags. It contains no
secret value.

```powershell
az login --tenant <home-tenant-guid>
pwsh .\scripts\provision-classic-net-bff-poc.ps1
```

The script looks up an exact display name, creates or reuses one application,
creates or reuses its home-tenant service principal, and converges the two
confidential `web` callbacks. It clears `spa.redirectUris` and disables fallback
public-client behavior. The default permission set contains no Microsoft Graph
delegated permission.

The default state path is `.classic-net-bff-poc-state.json`. It is ignored by
Git. Use a dedicated display name for this PoC because convergence updates the
audience, callbacks, public-client flags, and requested permissions on a reused
registration. Cleanup never deletes a reused object and does not restore its
previous settings.

### Optional Microsoft Graph permission

Authenticated session projection is sufficient for this comparison, so
`User.Read` is not justified by default. Add it only when you intentionally add
a server-side Microsoft Graph `/me` demonstration and its token-cache design:

```powershell
pwsh .\scripts\provision-classic-net-bff-poc.ps1 -IncludeUserRead
```

This switch adds delegated Microsoft Graph `User.Read` to the application
registration. It does not grant tenant-wide admin consent. Neither sample in
the core PoC calls Graph or retains a downstream access token.

## Create a short-lived Dev credential

Credential creation is opt-in and limited to 30 days. Supply an explicit output
path whose file name matches `.classic-net-bff-poc-secret*.json` so Git ignores
it. The script refuses to overwrite a file, never prints the value, and replaces
inherited permissions with a Windows ACL for the current identity. It creates
and verifies an empty protected output file before requesting the credential
from Microsoft Graph.

```powershell
New-Item -ItemType Directory -Force .\.local | Out-Null
pwsh .\scripts\provision-classic-net-bff-poc.ps1 `
  -CreateDevSecret `
  -SecretLifetimeDays 7 `
  -SecretOutputPath .\.local\.classic-net-bff-poc-secret-dev.json
```

> [!CAUTION]
> Inspect the file ACL before use. Delete the credential file after configuring
> the processes. A Git ignore rule prevents accidental staging but is not an
> access control. Use a non-exportable certificate or an approved
> managed-identity-backed client assertion for the operational destination.
> If the script reports that Graph created the credential but the protected file
> could not be written, run `cleanup-classic-net-bff-poc.ps1` immediately with
> the reported state path, then delete the secret output file without printing
> or inspecting its contents.

Load the same credential into both local process environments without printing
it:

```powershell
$secret = Get-Content .\.local\.classic-net-bff-poc-secret-dev.json -Raw |
  ConvertFrom-Json
$env:CROESUS_LEGACY_CLIENT_SECRET = $secret.clientSecret
$env:AzureAd__TenantId = $secret.tenantId
$env:AzureAd__ClientId = $secret.clientId
$env:AzureAd__ClientSecret = $secret.clientSecret
```

Clear the variables and remove the local file when the session ends:

```powershell
$env:CROESUS_LEGACY_CLIENT_SECRET = $null
$env:AzureAd__TenantId = $null
$env:AzureAd__ClientId = $null
$env:AzureAd__ClientSecret = $null
$secret = $null
Remove-Item .\.local\.classic-net-bff-poc-secret-dev.json
```

## Configure and run the legacy host

Set the non-secret values in
`poc/legacy-net452/web.config` for local execution:

```xml
<add key="ClientId" value="<application-client-guid>" />
<add key="TenantId" value="<home-tenant-guid>" />
<add key="AuthorityMode" value="SingleTenant" />
<add key="AllowedTenantIds" value="" />
```

Keep `CROESUS_LEGACY_CLIENT_SECRET` in the IIS Express process environment. Do
not place the value in `web.config`. Build and test the host:

```powershell
dotnet build .\poc\legacy-net452\LegacyNet452.csproj --configuration Release
dotnet test .\poc\legacy-net452\Tests\LegacyNet452.Tests.csproj --configuration Release
```

Open `poc/legacy-net452/LegacyNet452.csproj` in Visual Studio and run the
`LegacyNet452` IIS Express profile. `dotnet run` is not a System.Web hosting
model. Confirm the browser uses `https://localhost:44352` and the exact callback
registered above.

## Configure and run the modern host

The environment variables loaded from the protected credential file configure
the tenant, client, and Dev secret. Start the HTTPS profile:

```powershell
dotnet run --project .\poc\modern-net10\Croesus.ModernBff.csproj `
  --launch-profile https
```

Open `https://localhost:7100`. The callback is
`https://localhost:7100/signin-oidc`. The `/api/session` response contains only
session metadata and no OAuth material.

## Opt in to organizational multitenancy

Create or converge the registration as `AzureADMultipleOrgs` only with the
explicit switch:

```powershell
pwsh .\scripts\provision-classic-net-bff-poc.ps1 -MultiTenant
```

Use the `organizations` authority so personal Microsoft accounts are excluded.
Configure both hosts with a non-empty allowlist of customer tenant GUIDs:

* Set legacy `AuthorityMode` to `Organizations` and `AllowedTenantIds` to a
  comma-separated list
* Set modern `Authentication__Mode=Organizations`,
  `AzureAd__TenantId=organizations`, and indexed environment variables such as
  `Authentication__AllowedTenantIds__0=<tenant-guid>`

Each host first validates signature, audience, lifetime, nonce, state, and
correlation. It then binds the validated issuer tenant segment to the `tid`
claim and applies the allowlist before establishing the session. An allowlist
does not replace issuer validation.

The publisher owns the application registration in its home tenant. Each
customer tenant receives its own enterprise application (service principal)
when an administrator or authorized user consents. The customer tenant
administrator controls whether consent is permitted and which tenant policies
apply. This repository does not automate customer-tenant consent or create
customer service principals.

Multitenancy expands the blast radius of a registration, credential, consent,
or issuer-validation mistake from one directory to every admitted customer
directory. Keep the allowlist narrow, rotate credentials, review customer
consent separately, and monitor sign-ins by tenant.

## Capture privacy-safe evidence

Collect only the evidence needed to compare the two hosts:

* UTC timestamp and host name (`legacy-net452` or `modern-net10`)
* Callback URI and `web` platform classification
* Result category, such as success, issuer rejection, or allowlist rejection
* Locally generated correlation ID
* Redacted tenant ID, for example the first and last four characters
* Browser response field names proving that only session metadata is returned

Never capture or share an access token, ID token, refresh token, authorization
code, PKCE verifier or challenge, client secret, raw cookie, full claims set, or
browser HAR. Keep IdentityModel personally identifiable information logging
disabled. Treat Entra sign-in exports as restricted evidence and redact user,
IP, and tenant identifiers before distribution.

## Caveat matrix

<!-- markdownlint-disable MD060 -->

| Concern                 | .NET Framework 4.5.2                              | .NET Framework 4.8 bridge                         | .NET 10 destination                              |
|-------------------------|---------------------------------------------------|---------------------------------------------------|--------------------------------------------------|
| Support position        | Unsupported since April 2022                      | Supported with the underlying Windows lifecycle   | Supported strategic application target           |
| Identity integration    | Katana 4.2.3; no current MSAL.NET                  | Katana can remain; current MSAL.NET is compatible  | Microsoft.Identity.Web and current MSAL.NET       |
| PKCE                    | Native Katana only with explicit code-flow options | Retest Katana behavior after retargeting           | Handler-managed authorization code with PKCE      |
| Upgrade meaning         | Dev protocol proof only                            | Install, retarget, recompile, and regression test  | Migrate hosting and authentication boundary       |
| TLS                     | Force TLS 1.2 process-wide                         | Verify OS defaults and outbound policy             | Use current platform defaults and policy          |
| Cookie and SameSite     | Test exact browsers; old runtime semantics         | Retest after framework and patch changes           | Use secure ASP.NET Core cookie policy              |
| Farm state protection   | Synchronize and protect ASP.NET machine keys       | Preserve stable shared keys during the bridge      | Use shared ASP.NET Core Data Protection keys      |
| Token cache             | None in core; avoid custom process-local cache      | Design a distributed cache if downstream calls exist | Use supported MSAL cache integration when needed |
| Operational credential  | Dev secret for proof only                          | Prefer a non-exportable certificate                | Certificate or managed-identity-backed assertion  |
| Production recommendation | Do not deploy                                    | Transitional only when business timing requires it | Preferred destination                             |

<!-- markdownlint-enable MD060 -->

## Threats and limitations

* The legacy runtime and IdentityModel dependency chain are old even though the
  selected Katana packages compile for .NET Framework 4.5.2.
* Introducing custom PKCE glue, manual verifier storage, or manual token
  redemption would enlarge the attack surface and invalidate the comparison.
* A process-local token cache loses state on recycle and fails across instances.
  The core PoC avoids downstream token acquisition entirely.
* Dev secrets require expiration, rotation, protected storage, and prompt
  deletion. They are not the operational credential recommendation.
* Legacy SameSite behavior varies with Windows and .NET patches. Test every
  supported browser and callback path.
* The legacy process must force TLS 1.2 before metadata and token back-channel
  traffic.
* Every legacy farm instance must share stable protected machine keys. Key drift
  breaks state, nonce, correlation, PKCE verifier, and session recovery.
* Multitenant consent and issuer isolation are security boundaries. Do not
  automate customer consent or disable issuer validation to make sign-in work.
* Successful compilation does not prove IIS hosting, trusted HTTPS, callback
  routing, or production readiness.

## Clean up directory objects

Review the recorded ownership flags before cleanup. Preview the operation first:

```powershell
Get-Content .\.classic-net-bff-poc-state.json
pwsh .\scripts\cleanup-classic-net-bff-poc.ps1 -WhatIf
```

Run cleanup after confirming the target tenant and recorded IDs:

```powershell
pwsh .\scripts\cleanup-classic-net-bff-poc.ps1
```

The cleanup script removes only the password credential and directory objects
whose `created` flags were recorded as `true` by provisioning. It never deletes
a reused application or service principal. It updates state after each removal
so a repeated cleanup does not broaden its scope. Remove the non-secret state
file manually after reviewing the completed result.
