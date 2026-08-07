---
title: Croesus Modern .NET BFF Proof of Concept
description: Local configuration and security boundaries for the supported .NET 10 comparison BFF
ms.date: 2026-08-07
ms.topic: how-to
---

## Security boundary

The ASP.NET Core host owns authorization-code redemption and the authenticated
session. The browser receives an opaque secure, HTTP-only cookie. The
`/api/session` endpoint returns only authentication state, display name, and
tenant ID. It never returns OAuth tokens, authorization codes, PKCE material,
client credentials, or raw cookies.

The sample intentionally omits Microsoft Graph. Authenticated session projection
is sufficient for the comparison and avoids adding downstream token custody.

## Local configuration

Configure non-secret identifiers and the development credential with .NET user
secrets. Use the modern callback URI
`https://localhost:7100/signin-oidc` in the confidential web registration.

```powershell
dotnet user-secrets set "AzureAd:TenantId" "<tenant-guid>"
dotnet user-secrets set "AzureAd:ClientId" "<application-client-guid>"
dotnet user-secrets set "AzureAd:ClientSecret" "<short-lived-development-secret>"
dotnet run --launch-profile https
```

You can supply the same keys as environment variables by replacing each colon
with a double underscore. Do not add a secret to `appsettings.json`, launch
settings, source, logs, or command output captured as evidence.

## Multitenant mode

Multitenant mode is opt-in. Set `Authentication:Mode` to `Organizations`, set
`AzureAd:TenantId` to `organizations`, and provide at least one non-empty GUID in
`Authentication:AllowedTenantIds`. Startup fails when this allowlist is absent or
invalid. The OIDC handler validates token cryptography first, then the BFF binds
the tenant-specific issuer to the `tid` claim and rejects disallowed tenants
before creating the application session.

## Destination credential

Development can use a short-lived client secret from user secrets or the
environment. Non-development startup rejects `AzureAd:ClientSecret` and requires
a Microsoft.Identity.Web client credential collection. Supply certificate or
managed-identity-backed assertion configuration through the deployment
environment. This keeps credential rotation outside the application binary.

## Validation

```powershell
dotnet build .\Croesus.ModernBff.csproj --configuration Release
dotnet test .\Tests\Croesus.ModernBff.Tests.csproj --configuration Release
```
