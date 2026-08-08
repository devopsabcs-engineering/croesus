---
title: Legacy .NET Framework 4.5.2 BFF proof
description: Build and IIS Express guidance for the Katana authorization-code and PKCE comparison host
ms.date: 2026-08-07
ms.topic: how-to
---

## Purpose

This project proves confidential authorization-code redemption with native
Katana PKCE on classic ASP.NET and .NET Framework 4.5.2. It is protocol
feasibility code on an unsupported runtime, not a production recommendation.
Retarget to .NET Framework 4.8 for a supported operational bridge or migrate to
.NET 10 for the strategic destination.

The browser receives an HTTP-only session cookie and a small identity
projection. OAuth tokens, authorization codes, PKCE verifiers, client
credentials, and raw cookies are neither returned nor logged. The sample does
not call Microsoft Graph and contains no token cache.

## Configure the host

Set `ClientId` and `TenantId` in `web.config`. Supply the confidential client
credential through the environment variable named by
`ClientSecretEnvironmentVariable`:

```powershell
$prompt = Get-Credential -UserName "unused" -Message "Enter the Dev client secret"
$env:CROESUS_LEGACY_CLIENT_SECRET = $prompt.GetNetworkCredential().Password
$prompt = $null
```

Start IIS Express from the same process environment, or use your approved IDE
secret facility. Do not commit the value to `web.config`, a launch profile,
scripts, or logs.

The default `SingleTenant` mode builds a tenant-specific authority and allows
only `TenantId`. To test organizational multitenancy, set `AuthorityMode` to
`Organizations` and populate `AllowedTenantIds` with one or more comma-separated
tenant GUIDs. Startup fails when that allowlist is empty or malformed. The
middleware still validates signatures, audience, lifetime, nonce, state, and
correlation. A strict validator also binds the issuer tenant segment to the
validated `tid` claim.

## Build and test

Run these commands from the repository root:

```powershell
dotnet build .\poc\legacy-net452\LegacyNet452.csproj --configuration Release
dotnet test .\poc\legacy-net452\Tests\LegacyNet452.Tests.csproj --configuration Release
```

The SDK build places the host assembly and dependencies in `bin`, which is the
layout expected by System.Web. `dotnet run` is not supported for this host.

## Run with IIS Express

Open the project in Visual Studio and select the `LegacyNet452` IIS Express
profile. The profile reserves HTTPS port `44352`; register the exact callback
`https://localhost:44352/signin-oidc` under the Entra application's `web`
redirect URIs. Trust the local IIS Express development certificate before the
first sign-in.

If Visual Studio or IIS Express is unavailable, the command-line build and tests
remain valid, but they do not prove System.Web hosting. Validate the physical
path, application-pool bitness, TLS certificate, callback, and OWIN startup
discovery on a Windows host before collecting live evidence.

## Operational constraints

Force TLS 1.2 for metadata and token back-channel calls. The startup code makes
that process-wide choice. In a web farm, every instance must use stable shared
ASP.NET machine keys so state, nonce, correlation, PKCE verifier, and session
protection survive cross-instance callbacks. Protect and rotate those keys as
credentials.

The cookie uses `Secure=Always`, HTTP-only access, `SameSite=Lax`, a fixed
30-minute lifetime, and Katana's header-based `CookieManager`. This manager is
selected explicitly to avoid the Katana 4.2.3 `SystemWebCookieManager`
compatibility failure when System.Web applies a cookie after response
commitment. Validate SameSite behavior on the exact supported browser set
because .NET Framework 4.5.2 predates current browser semantics.
