<!-- markdownlint-disable-file -->
# Research: .NET Framework 4.8.1 as a BFF for Croesus (Entra ID OIDC)

Date: 2026-09-22
Status: **Complete** (all five research questions answered with citations)
Scope: research only — no workspace files outside `.copilot-tracking/research/` were modified.

## Research questions

1. .NET Framework lifecycle facts (4.5.2 EOL; 4.6.2/4.7.2/4.8/4.8.1 status; Component Lifecycle Policy; OS support for 4.8.1; retarget story 4.5.2 → 4.8.1).
2. Secure OIDC in classic ASP.NET on .NET Framework 4.8.1 (Katana/OWIN status; `Microsoft.Identity.Web` TFMs; MSAL.NET confidential client, OBO, token cache; ADAL retirement; official samples + `Startup.Auth.cs`).
3. Can a .NET Framework 4.8.1 app be a *proper* BFF? (PKCE; server-side token custody; cookie security + SameSite; antiforgery; reverse-proxy options; what is lost; CAE).
4. IdentityServer8 — what is it actually? Compare Duende IdentityServer, IdentityServer4 EOL, `Duende.BFF`.
5. Migration sequencing (Stage 0–3), with deep dive on incremental ASP.NET → ASP.NET Core migration, `System.Web.Adapters` remote authentication, and YARP strangler.

---

## Executive summary — the five things that matter

1. **`Microsoft.Identity.Web.OWIN` exists and targets .NET Framework 4.7.2.** The premise in the session ("Microsoft.Identity.Web is Core-only, so we'd have to hand-roll OWIN") is **wrong**. Microsoft ships a first-party, actively maintained Identity.Web surface for classic ASP.NET OWIN apps. This materially changes the effort estimate for Stage 1.
2. **.NET Framework 4.8.1 is NOT installable on Windows Server 2016 or 2019.** Only Windows Server 2022 (installable) and Windows Server 2025 (in-box). If Croesus's estate is on WS2016/2019, "go to 4.8.1" is an **OS project**, not a retarget project. **4.8** is the correct target for WS2016/2019/2022.
3. **IdentityServer8 is a single-maintainer community fork of a codebase Duende itself describes as containing "multiple known security vulnerabilities and bugs."** It is also ASP.NET Core / .NET 8 only, so it cannot even run in-process inside a .NET Framework 4.8.1 app. Recommendation: do not introduce it.
4. **A .NET Framework 4.8.1 app CAN be a real BFF for the token-replay concern** (auth code + PKCE, tokens server-side, encrypted HttpOnly/Secure cookie to the browser). PKCE has shipped in `Microsoft.Owin.Security.OpenIdConnect` since 4.2.0 (May 2021) and is `true` by default — *but only when `ResponseType = code`*, which is **not** the default.
5. **The highest-value finding: `System.Web.Adapters` + YARP incremental migration lets Croesus put a modern ASP.NET Core BFF *in front of* the 4.5.2 app without rewriting it first.** This inverts the sequencing — they can get BFF benefits (YARP, `Microsoft.Identity.Web`, modern telemetry, CAE) *before* touching the Web Forms estate.

---

## 1. .NET Framework lifecycle facts

### 1.1 Support policy model

.NET Framework 4.5.2 and later is defined as **a component of the Windows operating system**, and therefore follows the **Component Lifecycle Policy** — it receives the same support as its parent product (the Windows OS it is installed on), not a fixed standalone end date.

> "Beginning with version 4.5.2 and later, .NET Framework is defined as a component of the Windows operating system (OS). Components receive the same support as their parent products, therefore, .NET Framework 4.5.2 and later follows the lifecycle policy of the underlying Windows OS on which it is installed."
> — [.NET Framework Support Policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-framework)

> "Microsoft .NET Framework follows the [Component](https://learn.microsoft.com/en-us/lifecycle/faq/fixed-policy#how-is-a-component-supported-under-the-fixed-lifecycle-policy) Lifecycle Policy."
> — [Microsoft .NET Framework — Microsoft Lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-framework)

**Practical meaning for Croesus:** "4.8.1 is supported" is only true *conditionally* — it is supported as long as it sits on a supported Windows build. There is no independent 4.8.1 end-of-support date to cite to an auditor; the audit answer is the Windows Server lifecycle date.

### 1.2 Version → status → end of support

| Version | Release date | Status | End of support | Notes |
| --- | --- | --- | --- | --- |
| 4.8.1 | 2022-08-09 | **Active** | Follows parent Windows OS | Latest version; ships with future Windows releases |
| 4.8 | 2019-04-18 | **Active** | Follows parent Windows OS | |
| 4.7.2 | 2018-04-30 | **Active** | Follows parent Windows OS | Minimum version for supported SameSite behaviour |
| 4.7.1 | 2017-10-17 | **Active** | Follows parent Windows OS | |
| 4.7 | 2017-04-05 | **Active** | Follows parent Windows OS | |
| 4.6.2 | 2016-08-02 | **Active (terminating)** | **2027-01-12** | Only 4.x version with an explicit fixed end date |
| 4.6.1 | 2015-11-30 | **Out of support** | **2022-04-26** | SHA-1 signing retirement |
| 4.6 | 2015-07-20 | **Out of support** | **2022-04-26** | SHA-1 signing retirement |
| **4.5.2** | **2014-05-05** | **Out of support** | **2022-04-26** | **Croesus's current target** |
| 4.5.1 | 2013-10-17 | Out of support | 2016-01-12 | |
| 4.5 | 2012-08-15 | Out of support | 2016-01-12 | |
| 4.0 | 2010-04-12 | Out of support | 2016-01-12 | |
| 3.5 SP1 | 2008-11-18 | Active | **2029-01-09** | Standalone fixed policy from Win10 1809 / WS2019 onward |

Sources: [.NET Framework Support Policy — Release history](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-framework); [Microsoft .NET Framework lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-framework); [Install .NET Framework on Windows](https://learn.microsoft.com/en-us/dotnet/framework/install/on-windows-and-server).

**The 4.5.2 EOL reason matters for the security conversation.** It was not a routine expiry — it was a cryptographic retirement:

> "Support for .NET Framework versions 4.5.2, 4.6, and 4.6.1 ended on April 26, 2022, so security fixes, updates, and technical support for these versions will no longer be provided. .NET Framework content previously digitally signed using certificates that use the SHA-1 algorithm was retired in order to support evolving industry standards."
> — [.NET Framework Support Policy — SHA-1 content retirement](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-framework)

Announcement: [.NET Framework 4.5.2, 4.6, 4.6.1 will reach end of support on April 26, 2022](https://devblogs.microsoft.com/dotnet/net-framework-4-5-2-4-6-4-6-1-will-reach-end-of-support-on-april-26-2022).

Croesus has therefore been running **~4 years past end-of-security-fixes** on a runtime whose EOL was driven by a signing-algorithm weakness. That is the single strongest audit talking point available.

### 1.3 OS support for 4.8.1 — the blocker nobody mentioned in the session

This is the most consequential lifecycle fact and it is easy to miss.

| Windows Server | .NET Framework in-box | Latest .NET Framework supported |
| --- | --- | --- |
| Windows Server 2025 | **4.8.1** | **4.8.1** |
| Windows Server 2022 | 4.8 | **4.8.1** (installable) |
| Windows Server 2019 | 4.7.2 | **4.8** — *4.8.1 NOT supported* |
| Windows Server, version 1809 | 4.7.2 | 4.8 |
| Windows Server, version 1803 | 4.7.2 | 4.8 |
| Windows Server, version 1709 | 4.7.1 | 4.7.2 |
| Windows Server 2016 | 4.6.2 | **4.8** — *4.8.1 NOT supported* |
| Windows Server 2012 R2 | 4.5.1 | 4.8 |
| Windows Server 2012 | 4.5 | 4.8 |
| Windows Server 2008 R2 SP1 | 3.5 | 4.8 |

— [Install .NET Framework on Windows — Windows Server](https://learn.microsoft.com/en-us/dotnet/framework/install/on-windows-and-server)

Client side:

| Windows client | Latest .NET Framework supported |
| --- | --- |
| Windows 11 22H2 and later | 4.8.1 (in-box) |
| Windows 11 21H2 | 4.8.1 (installable) |
| Windows 10 22H2 / 21H2 / 21H1 / 20H2 | 4.8.1 (installable) |
| Windows 10 2004 and earlier | 4.8 max |

The release announcement confirms the narrow support matrix:

> "Windows Client versions: Windows 11, Windows 10 version 21H2, Windows 10 version 21H1, Windows 10 version 20H2
> Windows Server versions: **Windows Server 2022**"
> — [Announcing .NET Framework 4.8.1](https://devblogs.microsoft.com/dotnet/announcing-dotnet-framework-481/)

Also corroborated by the versions-and-dependencies matrix, which lists 4.8.1 Windows Server support as only ✔️ Windows Server 2025 and ➕ Windows Server 2022: [.NET Framework & Windows OS versions](https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/versions-and-dependencies).

**ARM64 angle.** Native Arm64 support is the headline runtime feature of 4.8.1 — and it is Windows 11+ only:

> ".NET Framework 4.8.1 adds native Arm64 support to the .NET Framework family. So, your investments in the vast ecosystem of .NET Framework apps and libraries can now leverage the benefits of running workloads natively on Arm64 — namely better performance when compared to running x64 code emulated on Arm64."
> — [What's new in .NET Framework](https://learn.microsoft.com/en-us/dotnet/framework/whats-new/) / [Announcing .NET Framework 4.8.1](https://devblogs.microsoft.com/dotnet/announcing-dotnet-framework-481/)

For a server-side Web Forms/MVC estate, Arm64 is **not a driver**. Beyond Arm64, 4.8.1's changes are WCAG 2.1 accessible tooltips and Windows Forms accessibility fixes — i.e. **nothing in 4.8.1 benefits Croesus's web workload over 4.8.**

> **Recommendation R1 — target 4.8, not 4.8.1, unless the OS is already WS2022/2025.**
> Retargeting 4.5.2 → 4.8 buys the *entire* security and lifecycle benefit with **zero OS dependency** (4.8 installs on WS2008 R2 SP1 through WS2022). Retargeting to 4.8.1 additionally forces a Windows Server 2022+ migration for no web-relevant feature gain. Verify Croesus's actual OS inventory before committing to "4.8.1" as the stated target — this is a question to put to them directly.

### 1.4 In-place upgrade story: 4.5.2 → 4.8 / 4.8.1

**Runtime-level: this is an in-place update, not a side-by-side install.**

> ".NET Framework 4.5 is an in-place update that replaces .NET Framework 4 on your computer, and similarly, .NET Framework 4.5.1, 4.5.2, 4.6, 4.6.1, 4.6.2, 4.7, 4.7.1, 4.7.2, and 4.8 are in-place updates to .NET Framework 4.5. In-place update means that they use the same runtime version, but the assembly versions are updated and include new types and members. After you install one of these updates, your .NET Framework 4, .NET Framework 4.5, .NET Framework 4.6, or .NET Framework 4.7 apps should continue to run without requiring recompilation."
> — [.NET Framework & Windows OS versions — Remarks for version 4.5 and later](https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/versions-and-dependencies)

> "All .NET Framework 4.x versions are in-place updates. Only a single 4.x version can be present on Windows."
> — [Install .NET Framework on Windows](https://learn.microsoft.com/en-us/dotnet/framework/install/on-windows-and-server)

Implications:

* Binary compatibility is **high** — existing 4.5.2 assemblies keep running on a 4.8 runtime without recompilation. Croesus's risk is not "will it run" but "will retargeting change behaviour."
* Only one 4.x runtime exists per machine. There is no side-by-side hedge; the runtime upgrade is a machine-wide event and must be validated across the whole estate on that box.
* Behavioural changes are gated on the **`targetFramework` attribute**, not the installed runtime. Retargeting is the act that opts you into new (and breaking) behaviour.

**What actually changes in config.** Two independent things must both be set:

```xml
<!-- web.config -->
<configuration>
  <system.web>
    <!-- 1. compilation targetFramework: which reference assemblies the compiler binds against -->
    <compilation targetFramework="4.8" debug="false" />

    <!-- 2. httpRuntime targetFramework: which ASP.NET quirks-mode behaviours are enabled.
            This is the one that flips SameSite, request validation, and ASP.NET-level
            behavioural switches. Omitting it silently keeps 4.0 quirks mode. -->
    <httpRuntime targetFramework="4.8" />
  </system.web>
</configuration>
```

And in the `.csproj` / `.vbproj`:

```xml
<PropertyGroup>
  <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
</PropertyGroup>
```

The SameSite guidance shows exactly this triple (`compilation`, `httpRuntime`, `TargetFrameworkVersion`) and adds a fourth item that is routinely missed — the `targetFramework` attribute on **every entry in `packages.config`**:

```xml
<?xml version="1.0" encoding="utf-8"?>
<packages>
  <package id="Microsoft.AspNet.Mvc" version="5.2.7" targetFramework="net48" />
  <package id="Microsoft.Owin" version="4.2.3" targetFramework="net48" />
</packages>
```

> "Verify NuGet packages in the project are targeted at the correct framework version… `Microsoft.ApplicationInsights` … should have its `targetFramework` attribute updated to `net472` if an updated package targeting your framework target exists."
> — [Work with SameSite cookies in ASP.NET — Retarget .NET apps](https://learn.microsoft.com/en-us/aspnet/samesite/system-web-samesite)

**Tooling constraint.** Visual Studio 2022 cannot build apps targeting .NET Framework 4.0–4.5.1. Croesus at 4.5.2 is *just* inside the VS2022-buildable range, but this confirms the direction of travel:

> "Starting with Visual Studio 2022, Visual Studio no longer includes .NET Framework components for .NET Framework 4.0 - 4.5.1 because these versions are no longer supported."
> — [.NET Framework & Windows OS versions](https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/versions-and-dependencies)

Targeting 4.8.1 additionally requires **Visual Studio 2022 17.3+** and the [.NET Framework 4.8.1 Developer Pack](https://go.microsoft.com/fwlink/?LinkId=2203306) ([Announcing .NET Framework 4.8.1](https://devblogs.microsoft.com/dotnet/announcing-dotnet-framework-481/)).

**Known retargeting breaking-change lists on Learn** (the authoritative per-version diff Croesus's dev team must walk):

* [Application compatibility in .NET Framework](https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/application-compatibility) — the index for retargeting vs runtime changes.
* [Retargeting changes in .NET Framework 4.8.x](https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/retargeting/4.8.x)
* Specific 4.8 retargeting change relevant to a regulated financial workload — **managed crypto classes stop throwing in FIPS mode**:
  > "By default in applications that target .NET Framework 4.8, the following managed cryptography classes no longer throw a `CryptographicException` [in FIPS mode]… Instead, these classes redirect cryptographic operations to a system cryptography library." Opt out with `Switch.System.Security.Cryptography.UseLegacyFipsThrow = true`.
  > — [What's new in .NET Framework 4.8 — Base classes](https://learn.microsoft.com/en-us/dotnet/framework/whats-new/)
* 4.7.2 retargeting change — **`DeflateStream` decompression switches to native Windows APIs** (`Switch.System.IO.Compression.DoNotUseNativeZipLibraryForDecompression`).
* 4.7 retargeting change — **`DataContractJsonSerializer` control-character serialization changes to ECMAScript 6 conformance**.
* 4.7.2 addition Croesus will need anyway — **`HttpCookie.SameSite`** and `<httpCookies sameSite="..." />` (see §3.3).

**Realistic Stage 0 effort.** Retarget itself is hours. The work is: (a) confirm/upgrade the OS if targeting 4.8.1; (b) refresh every NuGet package to a `net48`-targeted version; (c) regression-test the crypto/FIPS, compression, and JSON-serialization retargeting changes; (d) regression-test SameSite behaviour changes on auth/session cookies, which **change defaults from unspecified to `Lax`** (see §3.3). For a large ASPX estate, budget weeks of regression, not days.

---

## 2. Secure OIDC in classic ASP.NET (.NET Framework 4.8 / 4.8.1)

### 2.1 Katana / OWIN — supported, maintained, not deprecated

`Microsoft.Owin.Security.OpenIdConnect` is **currently maintained by Microsoft**, not deprecated.

* Latest version **4.2.3**, published **2025-07-08**, owned by `Microsoft` / `aspnet`, targets .NET Framework 4.5+. — [NuGet: Microsoft.Owin.Security.OpenIdConnect](https://www.nuget.org/packages/Microsoft.Owin.Security.OpenIdConnect)
* Repo `aspnet/AspNetKatana` is active: latest commit "Update IdentityModel packages to 5.7.1 (#577)" **last month** (as of retrieval), Dependabot cadence moved monthly → weekly, CodeQL suppressions being triaged. — [aspnet/AspNetKatana](https://github.com/aspnet/AspNetKatana)
* Release history shows ongoing **security servicing**: 4.2.3 (Jul 2025, cookie-parsing hardening), 4.2.2 (May 2022, [CVE-2022-29117](https://github.com/dotnet/announcements/issues/220)), 4.1.1 (Sep 2020, [CVE-2020-1045](https://github.com/aspnet/Announcements/issues/437)). — [AspNetKatana releases](https://github.com/aspnet/AspNetKatana/releases)

**Assessment:** Katana is in *maintenance* mode — security fixes and dependency updates, community-driven feature work only (Microsoft's own 4.2.0 notes say "These improvements have been completely community driven"). No Microsoft deprecation notice exists. It is a defensible platform for a 3–5 year bridge, **not** a platform to build a decade on.

The relevant packages for a Croesus BFF:

| Package | Purpose | Latest | TFM |
| --- | --- | --- | --- |
| [`Microsoft.Owin.Host.SystemWeb`](https://www.nuget.org/packages/Microsoft.Owin.Host.SystemWeb) | Hosts the OWIN pipeline inside IIS/System.Web | 4.2.3 | net45+ |
| [`Microsoft.Owin.Security.Cookies`](https://www.nuget.org/packages/Microsoft.Owin.Security.Cookies) | Session cookie — the BFF's browser-facing credential | 4.2.3 | net45+ |
| [`Microsoft.Owin.Security.OpenIdConnect`](https://www.nuget.org/packages/Microsoft.Owin.Security.OpenIdConnect) | OIDC to Entra ID | 4.2.3 | net45+ |
| [`Microsoft.Identity.Client`](https://www.nuget.org/packages/Microsoft.Identity.Client) (MSAL.NET) | Confidential client, OBO, token cache | current | netfx supported |
| [`Microsoft.Identity.Web.OWIN`](https://www.nuget.org/packages/Microsoft.Identity.Web.OWIN) | **First-party Identity.Web for OWIN** | 4.15.0 | **net472** |
| [`Microsoft.Identity.Web.TokenCache`](https://www.nuget.org/packages/Microsoft.Identity.Web.TokenCache) | Distributed/SQL/Redis token cache serializers | 4.15.0 | netstandard2.0 |

### 2.2 `Microsoft.Identity.Web` DOES support .NET Framework — correcting the session assumption

This is the single most important correction to the session record.

* **`Microsoft.Identity.Web` 4.15.0** declares supported frameworks: **.NET 8.0, .NET Standard 2.0, and .NET Framework 4.6.2**. — [NuGet: Microsoft.Identity.Web](https://www.nuget.org/packages/Microsoft.Identity.Web)
* **`Microsoft.Identity.Web.OWIN` 4.15.0** targets **.NET Framework 4.7.2** and is owned by `Microsoft` / `AzureAD`, last updated the day before retrieval, ~771K total downloads. — [NuGet: Microsoft.Identity.Web.OWIN](https://www.nuget.org/packages/Microsoft.Identity.Web.OWIN)
* The Identity.Web wiki home confirms the OWIN surface is first-class: *"Microsoft.Identity.Web is available as a set of NuGet packages … for .NET 6+ **and OWIN**."* — [microsoft-identity-web wiki](https://github.com/AzureAD/microsoft-identity-web/wiki)
* Release history confirms the intent, not an accident:
  * **1.9.0 (Apr 2021)** — "support for NET Framework 4.6.2"
  * **1.17 (Sep 2021)** — split out `Microsoft.Identity.Web.TokenCache` and `Microsoft.Identity.Web.Certificate` "for ASP.NET Framework and .NET Core apps"
  * **2.5.0 (Feb 2023)** — "v2 brings a variety of new higher-level APIs, including **support for .NET Framework (Owin)**, Daemon scenarios, and the new DownstreamApi"
  * **2.18.2 (May 2024)** — "token acquisition in ASP.NET Core 2.x on **net472 & net48**"
  — [microsoft-identity-web wiki — Roadmap](https://github.com/AzureAD/microsoft-identity-web/wiki)

**Support SLA to quote to Croesus:** major versions supported 12 months after the next major ships; minor versions older than N-1 unsupported. — [NuGet: Microsoft.Identity.Web](https://www.nuget.org/packages/Microsoft.Identity.Web)

> **Recommendation R2 — Croesus's Stage 1 auth work should use `Microsoft.Identity.Web.OWIN` on top of Katana, not hand-rolled `Microsoft.Owin.Security.OpenIdConnect` + raw MSAL.** They get Microsoft-maintained token acquisition, cache serialization, certificate/FIC credential loading, and `DownstreamApi` — the same conceptual API surface they'd later use in ASP.NET Core. This makes Stage 3 a *smaller* delta, not a rewrite.
> Constraint: `Microsoft.Identity.Web.OWIN` requires **net472 minimum**. This is another reason 4.8 (not 4.5.2) is mandatory, and another reason 4.8 suffices (4.8.1 not required).

### 2.3 ADAL.NET — retired, with a hard date

> "All Microsoft support and development for ADAL, including security fixes, **ended on June 30, 2023**. There were no ADAL feature releases or new platform version releases planned before the deprecation date. **No new features have been added to ADAL since June 30, 2020.**"
> — [Migrate to the Microsoft Authentication Library (MSAL)](https://learn.microsoft.com/en-us/entra/identity-platform/msal-migration)

> "Azure Active Directory Authentication Library (ADAL) has been deprecated. While existing apps that use ADAL will continue to work, Microsoft will no longer release security fixes on ADAL. Use the Microsoft Authentication Library (MSAL) to avoid putting your app's security at risk."
> — ibid.

Migration guide for .NET specifically: [ADAL.NET to MSAL.NET migration](https://learn.microsoft.com/en-us/entra/msal/dotnet/how-to/msal-net-migration).

If Croesus has *any* ADAL.NET in the estate (common in 2014-era Azure AD integrations), it is a second unpatched-security-library finding alongside the 4.5.2 runtime. Worth an explicit inventory question. Tenant-wide discovery tooling: [Get a complete list of apps using ADAL in your tenant](https://learn.microsoft.com/en-us/entra/identity-platform/howto-get-list-of-all-auth-library-apps).

**AD FS caveat** (relevant if SiteMinder fronts an AD FS): MSAL supports AD FS **2019 or later only**; AD FS 2016 and earlier are unsupported by MSAL. — [MSAL migration — AD FS support](https://learn.microsoft.com/en-us/entra/identity-platform/msal-migration)

### 2.4 MSAL.NET on .NET Framework — confidential client, OBO, token cache

MSAL.NET supports .NET Framework and is the supported path for confidential-client flows there. The Identity.Web wiki documents the classic-ASP.NET pattern explicitly ("Support for ASP.NET classic, .NET 4.7.2, and .NET Standard 2.0"):

```csharp
// Microsoft.Identity.Web token-cache serialization on classic .NET Framework
using Microsoft.Identity.Web;

private static IConfidentialClientApplication app;

public static async Task<IConfidentialClientApplication> BuildConfidentialClientApplication()
{
    if (app == null)
    {
        app = ConfidentialClientApplicationBuilder.Create(clientId)
            // Prefer a certificate over a client secret for a regulated workload.
            // .WithClientSecret(clientSecret) is the alternative.
            .WithCertificate(certDescription.Certificate)
            .WithTenantId(tenant)
            .Build();

        app.AddInMemoryTokenCache();   // single-instance only
    }
    return app;
}
```

— [microsoft-identity-web wiki — asp-net](https://github.com/AzureAD/microsoft-identity-web/wiki/asp-net)

**Token cache serialization on .NET Framework — this is the operational crux of a BFF.** A BFF holds tokens server-side, so the cache *is* the session store. Identity.Web supplies the serializers and they work on net462/net472:

```csharp
// Distributed in-memory (dev / single node)
app.UseDistributedTokenCaches(services =>
{
    // In net462/net472, requires a reference to Microsoft.Extensions.Caching.Memory
    services.AddDistributedMemoryCache();
});

// SQL Server — the right choice for a load-balanced Croesus web farm
app.UseDistributedTokenCaches(services =>
{
    // Requires Microsoft.Extensions.Caching.SqlServer
    services.AddDistributedSqlServerCache(options =>
    {
        options.ConnectionString = "<connection string>";
        options.SchemaName = "dbo";
        options.TableName  = "TokenCache";

        // Must exceed access-token lifetime (default sliding expiration is 20 min,
        // access tokens are typically 60 min — and up to ~28h with CAE).
        options.DefaultSlidingExpiration = TimeSpan.FromMinutes(90);
    });
});

// Redis
app.UseDistributedTokenCaches(services =>
{
    // Requires Microsoft.Extensions.Caching.StackExchangeRedis
    services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = "<redis endpoint>";
        options.InstanceName  = "Croesus";
    });
});
```

— [microsoft-identity-web wiki — asp-net](https://github.com/AzureAD/microsoft-identity-web/wiki/asp-net)

Answering the specific question asked: **`MsalDistributedTokenCacheAdapter` / the `Microsoft.Identity.Web.TokenCache` serializers ARE available on .NET Framework.** `Microsoft.Identity.Web.TokenCache` was split out in 1.17 precisely so that "ASP.NET Framework and .NET Core apps" could consume it with fewer dependencies and netstandard2.0 support. — [microsoft-identity-web wiki — Roadmap](https://github.com/AzureAD/microsoft-identity-web/wiki) and [asp-net](https://github.com/AzureAD/microsoft-identity-web/wiki/asp-net)

**On-Behalf-Of flow** is supported by MSAL.NET on .NET Framework (`AcquireTokenOnBehalfOf`) and Identity.Web 2.x added "OBO support for composite tokens" and long-running-OBO support. — [microsoft-identity-web wiki — Roadmap](https://github.com/AzureAD/microsoft-identity-web/wiki). Note for Croesus: a BFF that *delegates* to downstream APIs typically does **auth-code-then-OBO** (or auth-code with multiple resource scopes), and OBO is exactly the mechanism that keeps the downstream token bound to the user without the browser ever seeing it.

**Certificate / federated-credential loading** on .NET Framework is also covered (`DefaultCertificateLoader`, Key Vault-backed `CertificateDescription`), which matters because a BFF holding refresh tokens should use certificate credentials rather than a client secret. — [microsoft-identity-web wiki — asp-net](https://github.com/AzureAD/microsoft-identity-web/wiki/asp-net), [Certificates](https://github.com/AzureAD/microsoft-identity-web/wiki/Certificates)

### 2.5 Official samples — status check (important finding)

| Repo | Status |
| --- | --- |
| [`Azure-Samples/ms-identity-aspnet-webapp-openidconnect`](https://github.com/Azure-Samples/ms-identity-aspnet-webapp-openidconnect) | **Archived 2024-06-12. Read-only.** Code moved to the `archive` branch. README: *"The sample in this repository is no longer maintained and is kept for historical reasons. The sample in the main branch is not guaranteed to work with the latest versions of the libraries it depends on."* Points to [`ms-identity-docs-code-dotnet/web-app-aspnet`](https://github.com/Azure-Samples/ms-identity-docs-code-dotnet/tree/main/web-app-aspnet) (ASP.NET **Core**) as the current sample. |
| `AzureAD/ms-identity-aspnet-webapp-openidconnect` | Same repo under the old org name; redirects to the archived Azure-Samples repo. |
| `ms-identity-aspnet-daemon-webapp` | Daemon/confidential-client variant; same archival trajectory — treat as historical. |
| [`Azure-Samples/active-directory-aspnetcore-webapp-openidconnect-v2`](https://github.com/Azure-Samples/active-directory-aspnetcore-webapp-openidconnect-v2) | **Active.** The maintained incremental tutorial — ASP.NET **Core** only. |
| [`Azure-Samples/active-directory-dotnet-v1-to-v2` → `ConfidentialClientTokenCache`](https://github.com/Azure-Samples/active-directory-dotnet-v1-to-v2/tree/master/ConfidentialClientTokenCache) | Referenced by the current Identity.Web wiki as the .NET Framework token-cache reference. |

The Identity.Web wiki still cites `ms-identity-aspnet-webapp-openidconnect` (net472) as *the* ASP.NET MVC token-cache example — specifically [`WebApp/Utils/MsalAppBuilder.cs`](https://github.com/Azure-Samples/ms-identity-aspnet-webapp-openidconnect/blob/master/WebApp/Utils/MsalAppBuilder.cs) — even though the repo is archived. — [microsoft-identity-web wiki — asp-net](https://github.com/AzureAD/microsoft-identity-web/wiki/asp-net)

> **Finding F1 — Microsoft has archived its classic-ASP.NET Entra ID OIDC samples and now only maintains ASP.NET Core ones.** The *libraries* are supported; the *samples and guidance* have moved on. Croesus should expect to work from library docs and the archive branch rather than a living reference app. This is a real (if soft) signal about where the platform investment is, and it belongs in the recommendation.

### 2.6 `Startup.Auth.cs` — OWIN OIDC configuration for a BFF

The following is the correct shape for a **BFF** on .NET Framework 4.8 — note the deliberate departures from the historical sample defaults, each annotated. Constructed from the Katana source and release notes cited in §3.1.

```csharp
// App_Start/Startup.Auth.cs
using System;
using System.Configuration;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Owin;
using Microsoft.Owin.Extensions;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Cookies;
using Microsoft.Owin.Security.Notifications;
using Microsoft.Owin.Security.OpenIdConnect;
using Owin;

[assembly: OwinStartup(typeof(Croesus.Web.Startup))]

namespace Croesus.Web
{
    public partial class Startup
    {
        private static readonly string ClientId     = ConfigurationManager.AppSettings["ida:ClientId"];
        private static readonly string TenantId     = ConfigurationManager.AppSettings["ida:TenantId"];
        private static readonly string Authority    = $"https://login.microsoftonline.com/{TenantId}/v2.0";
        private static readonly string RedirectUri  = ConfigurationManager.AppSettings["ida:RedirectUri"];
        private static readonly string PostLogoutRedirectUri =
            ConfigurationManager.AppSettings["ida:PostLogoutRedirectUri"];

        public void ConfigureAuth(IAppBuilder app)
        {
            app.SetDefaultSignInAsAuthenticationType(CookieAuthenticationDefaults.AuthenticationType);

            // ---- The browser-facing credential: an encrypted, HttpOnly, Secure session cookie.
            //      This is the ONLY thing the browser ever holds. No access token, no id_token,
            //      no refresh token reaches JavaScript. This is what kills the unbound-token /
            //      token-replay exposure.
            app.UseCookieAuthentication(new CookieAuthenticationOptions
            {
                AuthenticationType = CookieAuthenticationDefaults.AuthenticationType,
                CookieName         = "__Host-Croesus.Session",  // __Host- prefix: Secure + Path=/ + no Domain
                CookieHttpOnly     = true,                      // not readable from JS
                CookieSecure       = CookieSecureOption.Always, // never sent over plaintext
                CookieSameSite     = SameSiteMode.Lax,          // Lax survives the OIDC redirect-back
                ExpireTimeSpan     = TimeSpan.FromHours(8),
                SlidingExpiration  = true,
                // Optional hardening: server-side session store so the cookie is an opaque
                // reference rather than a self-contained ticket.
                // SessionStore = new MyDistributedAuthenticationSessionStore(),
            });

            app.UseOpenIdConnectAuthentication(new OpenIdConnectAuthenticationOptions
            {
                ClientId     = ClientId,
                Authority    = Authority,
                RedirectUri  = RedirectUri,
                PostLogoutRedirectUri = PostLogoutRedirectUri,

                // ---- CRITICAL for a BFF, and NOT the library default. ----
                // Default ResponseType is CodeIdToken (hybrid). PKCE is only applied when
                // ResponseType == Code, so leaving the default silently disables PKCE.
                ResponseType = OpenIdConnectResponseType.Code,
                ResponseMode = OpenIdConnectResponseMode.FormPost,

                // UsePkce defaults to true in Microsoft.Owin.Security.OpenIdConnect >= 4.2.0,
                // but set it explicitly so the intent survives a package downgrade or review.
                UsePkce = true,

                // Redeem the code server-side for tokens. Default is false.
                RedeemCode = true,
                ClientSecret = ConfigurationManager.AppSettings["ida:ClientSecret"],
                // Prefer a certificate credential in production; see §2.4.

                // Persist tokens in the auth ticket. For a BFF, prefer NOT doing this and
                // instead writing tokens to the MSAL distributed token cache keyed by the
                // user's home account id — it keeps the cookie small and enables OBO/refresh.
                SaveTokens = false,

                Scope = "openid profile offline_access " +
                        ConfigurationManager.AppSettings["ida:DownstreamApiScope"],

                RequireHttpsMetadata = true,
                UseTokenLifetime     = false, // decouple cookie lifetime from id_token lifetime

                TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidAudience  = ClientId,
                    NameClaimType  = "name",
                    RoleClaimType  = "roles",
                },

                Notifications = new OpenIdConnectAuthenticationNotifications
                {
                    AuthorizationCodeReceived = OnAuthorizationCodeReceivedAsync,
                    AuthenticationFailed      = OnAuthenticationFailed,
                    RedirectToIdentityProvider = context =>
                    {
                        // Advertise CAE readiness (see §3.6). Entra ID then issues long-lived,
                        // revocable tokens and sends claims challenges on critical events.
                        context.ProtocolMessage.SetParameter("client_capabilities", "cp1");
                        return Task.CompletedTask;
                    },
                },
            });

            // Required when hosting OWIN under System.Web/IIS so the auth stage runs correctly.
            app.UseStageMarker(PipelineStage.Authenticate);
        }

        private static async Task OnAuthorizationCodeReceivedAsync(AuthorizationCodeReceivedNotification ctx)
        {
            // Redeem the code into the server-side MSAL token cache.
            // Tokens live here — in SQL Server / Redis — never in the browser.
            // See §2.4 for the cache wiring.
            var app = await MsalAppBuilder.BuildConfidentialClientApplicationAsync(ctx.OwinContext);
            var scopes = ConfigurationManager.AppSettings["ida:DownstreamApiScope"].Split(' ');

            await app.AcquireTokenByAuthorizationCode(scopes, ctx.Code)
                     .ExecuteAsync()
                     .ConfigureAwait(false);

            // Tell the middleware we handled the redemption so it does not redeem again.
            ctx.HandleCodeRedemption();
        }

        private static Task OnAuthenticationFailed(
            AuthenticationFailedNotification<OpenIdConnectMessage, OpenIdConnectAuthenticationOptions> ctx)
        {
            ctx.HandleResponse();
            ctx.Response.Redirect("/Error?message=" + Uri.EscapeDataString(ctx.Exception.Message));
            return Task.CompletedTask;
        }
    }
}
```

**The two landmines in that file, restated because they are easy to get wrong:**

1. `ResponseType` defaults to `OpenIdConnectResponseType.CodeIdToken`, and PKCE is gated on `ResponseType == Code`. Leaving the default gives you no PKCE while `UsePkce = true` sits in your config looking reassuring. Verified against Katana source and tests (§3.1).
2. `RedeemCode` defaults to `false`. Without it (or an `AuthorizationCodeReceived` handler that redeems), the middleware never obtains an access/refresh token and there is nothing for the BFF to hold. Confirmed by the constructor defaults in [`OpenIdConnectAuthenticationOptions.cs`](https://github.com/aspnet/AspNetKatana/blob/main/src/Microsoft.Owin.Security.OpenIdConnect/OpenIdConnectAuthenticationOptions.cs): `RedeemCode = false; UsePkce = true;`.

---

## 3. Can .NET Framework 4.8.1 be a *proper* BFF?

**Verdict: yes for the token-custody problem; no for the full modern BFF pattern.** The security objective Croesus cares about — no tokens in the browser, no replayable unbound bearer token — is fully achievable. The architectural conveniences (YARP, in-proc routing/DI/telemetry, first-class API forwarding) are not.

### 3.1 PKCE — supported, on by default since 4.2.0, but conditionally applied

**Shipped in `Microsoft.Owin.Security.OpenIdConnect` 4.2.0, released 2021-05-10**, via community PR [#389](https://github.com/aspnet/AspNetKatana/pull/389):

> "[#389](https://github.com/aspnet/AspNetKatana/pull/389) adds PKCE support for OpenIdConnect authentication when using the `code` flow"
> — [AspNetKatana 4.2.0 Release](https://github.com/aspnet/AspNetKatana/releases)

The API surface, from source:

```csharp
/// Enables or disables the use of the Proof Key for Code Exchange (PKCE) standard.
/// This only applies when the <see cref="ResponseType"/> is set to
/// <see cref="OpenIdConnectResponseType.Code"/>.
/// See https://tools.ietf.org/html/rfc7636.
/// The default value is `true`.
public bool UsePkce { get; set; }
```

— [`OpenIdConnectAuthenticationOptions.cs`](https://github.com/aspnet/AspNetKatana/blob/main/src/Microsoft.Owin.Security.OpenIdConnect/OpenIdConnectAuthenticationOptions.cs)

Constructor defaults confirm `UsePkce = true` and `RedeemCode = false`:

```csharp
public OpenIdConnectAuthenticationOptions(string authenticationType) : base(authenticationType)
{
    // ...
    ResponseMode = OpenIdConnectResponseMode.FormPost;
    ResponseType = OpenIdConnectResponseType.CodeIdToken;   // <-- NOT Code
    Scope        = OpenIdConnectScope.OpenIdProfile;
    RequireHttpsMetadata = true;
    UseTokenLifetime = true;
    RedeemCode = false;
    UsePkce    = true;
}
```

Challenge-side implementation — S256, cryptographically random 32-byte verifier, base64url-encoded, stashed in the protected `AuthenticationProperties`:

```csharp
// https://tools.ietf.org/html/rfc7636
if (Options.UsePkce && Options.ResponseType == OpenIdConnectResponseType.Code)
{
    using (RandomNumberGenerator randomNumberGenerator = RandomNumberGenerator.Create())
    using (HashAlgorithm hash = SHA256.Create())
    {
        byte[] bytes = new byte[32];
        randomNumberGenerator.GetBytes(bytes);
        string codeVerifier = TextEncodings.Base64Url.Encode(bytes);

        properties.Dictionary.Add(OAuthConstants.CodeVerifierKey, codeVerifier);
        byte[] challengeBytes = hash.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        string codeChallenge = TextEncodings.Base64Url.Encode(challengeBytes);

        openIdConnectMessage.Parameters.Add(OAuthConstants.CodeChallengeKey, codeChallenge);
        openIdConnectMessage.Parameters.Add(OAuthConstants.CodeChallengeMethodKey,
                                            OAuthConstants.CodeChallengeMethodS256);
    }
}
```

Redemption-side — the verifier is replayed on the token request and removed:

```csharp
// PKCE https://tools.ietf.org/html/rfc7636#section-4.5
string codeVerifier;
if (properties.Dictionary.TryGetValue(OAuthConstants.CodeVerifierKey, out codeVerifier))
{
    tokenEndpointRequest.Parameters.Add(OAuthConstants.CodeVerifierKey, codeVerifier);
    properties.Dictionary.Remove(OAuthConstants.CodeVerifierKey);
}
```

— [`OpenidConnectAuthenticationHandler.cs`](https://github.com/aspnet/AspNetKatana/blob/main/src/Microsoft.Owin.Security.OpenIdConnect/OpenidConnectAuthenticationHandler.cs)

The library's own tests prove the conditional gating — `ChallengeDoesNotIncludePkceForOtherResponseTypes` asserts that with `UsePkce = true` and `ResponseType` of `Token`, `IdToken`, or **`CodeIdToken`**, the challenge contains **no** `code_challenge` at all. — [`OpenIdConnectMiddlewareTests.cs`](https://github.com/aspnet/AspNetKatana/blob/main/tests/Microsoft.Owin.Security.Tests/OpenIdConnect/OpenIdConnectMiddlewareTests.cs)

**Conclusion Q3a:** Authorization Code + PKCE (S256) is fully supported on .NET Framework, provided Croesus is on `Microsoft.Owin.Security.OpenIdConnect >= 4.2.0` (recommend 4.2.3) **and** explicitly sets `ResponseType = OpenIdConnectResponseType.Code`. This is a config review item, not a development item.

### 3.2 Server-side token custody + session cookie

Yes — this is exactly the Katana cookie-middleware + MSAL-cache pattern shown in §2.4 and §2.6. The mechanics:

* OWIN cookie middleware issues an encrypted `AuthenticationTicket`. On IIS/System.Web the default protection is **ASP.NET machine key data protection** (stated in the Katana source docs for `AuthorizationCodeFormat`/`AccessTokenFormat`). For a web farm this means the `<machineKey>` must be synchronized across nodes.
* Tokens go into the MSAL confidential-client cache, serialized to SQL Server / Redis (§2.4). The browser holds only the session cookie.
* Refresh happens server-side via `AcquireTokenSilent`; the browser is never involved.

**Cookie-security settings required** (all present in the §2.6 snippet):

| Setting | Value | Why |
| --- | --- | --- |
| `CookieSecure` | `CookieSecureOption.Always` | Never transmit the session over plaintext |
| `CookieHttpOnly` | `true` | XSS cannot read the session |
| `CookieSameSite` | `SameSiteMode.Lax` | Blocks cross-site CSRF delivery while surviving the OIDC redirect-back |
| Cookie name prefix | `__Host-` | Browser-enforced: Secure, `Path=/`, no `Domain` — prevents subdomain cookie injection |
| `ExpireTimeSpan` / `SlidingExpiration` | policy-driven | Session lifetime decoupled from token lifetime |
| `SessionStore` | optional | Turns the cookie into an opaque server-side reference (strongest option) |

**`SameSite=Lax` is the right default, not `Strict`.** `Strict` breaks the top-level POST that Entra ID uses for `response_mode=form_post`. Microsoft's own OIDC middleware sets the *correlation/nonce* cookies to `SameSite=None` for exactly this reason — see `RememberNonce`/`RetrieveNonce` in [`OpenidConnectAuthenticationHandler.cs`](https://github.com/aspnet/AspNetKatana/blob/main/src/Microsoft.Owin.Security.OpenIdConnect/OpenidConnectAuthenticationHandler.cs), which use `SameSite = SameSiteMode.None, HttpOnly = true, Secure = Request.IsSecure`. Note the consequence: **the OIDC nonce cookie is `SameSite=None`, therefore it must be `Secure`, therefore the whole flow must be HTTPS end-to-end.** Any HTTP hop (e.g. TLS terminating at a load balancer with plaintext to IIS) silently breaks sign-in.

### 3.3 SameSite on .NET Framework — the patch story

This is a real trap for a 4.5.2 → 4.8 retarget because the patch **changed defaults**.

> "Microsoft does not support .NET versions lower than 4.7.2 for writing the same-site cookie attribute."
> — [Work with SameSite cookies in ASP.NET](https://learn.microsoft.com/en-us/aspnet/samesite/system-web-samesite)

Croesus at 4.5.2 therefore **cannot** correctly emit SameSite at all today. That alone justifies the retarget.

The December 2019 / November 2019 Windows updates moved .NET 4.7.2+ from the [2016 draft](https://tools.ietf.org/html/draft-west-first-party-cookies-07) to the [2019 draft](https://tools.ietf.org/html/draft-west-cookie-incrementalism-00) and changed semantics:

> "Before the patch a value of `None` meant: Do not emit the attribute at all. After the patch: A value of `None` means 'Emit the attribute with a value of `None`'. A `SameSite` value of `(SameSiteMode)(-1)` causes the attribute not to be emitted. **The default SameSite value for forms authentication and session state cookies was changed from `None` to `Lax`.**"
> — ibid.

KB list: [KB articles that support SameSite in .NET Framework](https://learn.microsoft.com/en-us/aspnet/samesite/kbs-samesite).

Config surface introduced in 4.7.2:

```xml
<configuration>
  <system.web>
    <httpCookies sameSite="Strict" requireSSL="true" />
    <authentication mode="Forms">
      <forms cookieSameSite="Lax" requireSSL="true" />
    </authentication>
    <sessionState cookieSameSite="Lax" />
  </system.web>
</configuration>
```

— [What's new in .NET Framework 4.7.2 — ASP.NET](https://learn.microsoft.com/en-us/dotnet/framework/whats-new/) and [Work with SameSite cookies in ASP.NET](https://learn.microsoft.com/en-us/aspnet/samesite/system-web-samesite)

Escape hatches to know about (and to *not* ship): `aspnet:SuppressSameSiteNone=true` reverts to 2016 behaviour; `(SameSiteMode)(-1)` / `sameSite="Unspecified"` suppresses emission. Microsoft explicitly frames the revert as "an *extremely temporary fix*."

**Regression risk for Croesus:** if any ASPX page or partner integration does a cross-site POST or renders inside an `<iframe>`, the forms-auth/session cookies flipping to `Lax` will break it. Microsoft calls this out directly. This must be on the Stage 0 test plan.

### 3.4 Antiforgery / CSRF — Web Forms vs MVC

| Stack | Mechanism | Notes for a BFF |
| --- | --- | --- |
| ASP.NET MVC 5 | `@Html.AntiForgeryToken()` + `[ValidateAntiForgeryToken]` (or `[AutoValidateAntiforgeryToken]` patterns via a global filter) | Mature, well understood. Apply globally via a base controller or global filter rather than per-action — per-action is where gaps appear. |
| ASP.NET Web Forms | **No built-in antiforgery-token feature.** Relies on `ViewStateUserKey` + MAC-protected ViewState, plus `EnableEventValidation`. `AntiForgery.GetHtml()` from `System.Web.Helpers` can be wired manually. | Weaker and easier to get wrong. `Page.ViewStateUserKey` must be set (typically to the session ID or authenticated user ID) in `Page_Init` — if it isn't set, ViewState provides **no** CSRF protection. |

For a Web Forms estate, the practical CSRF posture is: `SameSite=Lax` on the session cookie (which blocks the cross-site form POST) **plus** `ViewStateUserKey` set correctly, **plus** MAC-enabled ViewState. `SameSite=Lax` is doing a lot of the work here, which is another reason the 4.7.2+ retarget is load-bearing.

### 3.5 Reverse proxy / API forwarding — no YARP on .NET Framework

**Confirmed: YARP cannot run on .NET Framework.** YARP is built as ASP.NET Core middleware and requires modern .NET. Supporting evidence within this research: the Learn incremental-migration guidance always places YARP in the **ASP.NET Core** project that fronts the Framework app, never inside the Framework app ([Get started with incremental migration](https://learn.microsoft.com/en-us/aspnet/core/migration/fx-to-core/start): *"Configure it to proxy requests to your original application using YARP (Yet Another Reverse Proxy)"*), and `Duende.BFF` 4.3.0 — which is built on YARP — targets **.NET 10.0 only** ([NuGet: Duende.BFF](https://www.nuget.org/packages/Duende.BFF)).

Alternatives available to a .NET Framework 4.8 BFF, in rough order of preference:

| Option | How | Pros | Cons |
| --- | --- | --- | --- |
| **In-app passthrough controller** (`HttpClient`) | An MVC/Web API controller that takes the incoming request, attaches a server-held access token from the MSAL cache, forwards to the downstream API, and streams the response back | Full control; tokens never leave the server; works today; no new infrastructure | You are writing (and maintaining) a proxy: header allow/deny-listing, streaming, chunked transfer, SSE/WebSockets, timeouts, retries, `HttpClient` lifetime/socket-exhaustion, error mapping. Non-trivial and easy to get subtly wrong. |
| **IIS ARR + URL Rewrite** | Infrastructure-level reverse proxy in front of / alongside the app | No app code; mature; ops-owned | Cannot inject a per-user access token from the MSAL cache — it has no identity context. Useful for routing, **not** for token attachment. Does not solve the BFF problem by itself. |
| **Azure API Management** | APIM in front of the APIs; BFF calls APIM; APIM validates JWT, rate-limits, transforms | Offloads policy, throttling, observability; audit-friendly; a real control point | New managed service, cost, latency hop, and a new thing to operate. Still needs the BFF to obtain and attach the token. |
| **Azure Application Gateway / Front Door** | L7 routing + WAF | WAF value; TLS offload | Same limitation as ARR — no identity-aware token attachment. |
| **ASP.NET Core BFF fronting the 4.x app (YARP)** | See §5 Stage 3 / the incremental-migration path | Gets you real YARP, `Microsoft.Identity.Web`, modern DI/telemetry | Introduces a second runtime and deployment unit |

**Note on the WebSocket/streaming question:** a hand-rolled `HttpClient` passthrough on .NET Framework does not transparently proxy WebSockets. If any Croesus feature uses SignalR or long-polling, that must be routed around the passthrough or handled by ARR.

### 3.6 What is LOST versus a modern .NET BFF

| Capability | Modern .NET (ASP.NET Core) BFF | .NET Framework 4.8/4.8.1 BFF | Severity for Croesus |
| --- | --- | --- | --- |
| Reverse proxy | **YARP** — battle-tested, config-driven, streaming, WebSockets, load balancing, health checks | Hand-rolled `HttpClient` controller, or ARR/APIM out of process | **High** — this is the biggest single gap |
| Identity library | `Microsoft.Identity.Web` (full ASP.NET Core surface: `AddMicrosoftIdentityWebApp`, `DownstreamApi`, `[AuthorizeForScopes]`, automatic incremental consent/CA challenge handling) | `Microsoft.Identity.Web.OWIN` — **available**, but a smaller surface; no `[AuthorizeForScopes]` MVC-Core filter, no built-in consent-challenge UX | **Medium** — much smaller gap than assumed |
| BFF framework | `Duende.BFF` (session mgmt, back-channel logout, token mgmt, anti-forgery header enforcement) — **.NET 10 only** | None. Build it yourself. | Medium |
| DI | Built-in `IServiceCollection`, scoped lifetimes everywhere | Web Forms DI only since 4.7.2 (setter/interface/ctor injection for handlers, modules, Pages, user controls) — usable but retrofit-grade | Medium |
| Telemetry | OpenTelemetry-native; `Activity`/`W3C traceparent` propagation; OTLP export | Classic Application Insights SDK; no first-class OTel; correlation across the BFF boundary must be hand-wired | **High** for a regulated workload where audit trail matters |
| Config | `IConfiguration`, layered providers, Key Vault provider, hot reload | `web.config` + [configuration builders (4.7.1+)](https://learn.microsoft.com/en-us/dotnet/framework/whats-new/) | Low/Medium |
| Health checks / readiness | Built-in `/health` endpoints | Roll your own | Low |
| Cookie/session store | `ITicketStore`, distributed cache-backed by default | `SessionStore` on `CookieAuthenticationOptions` — exists, but you write the store | Low |
| **CAE (Continuous Access Evaluation)** | Supported via MSAL + `cp1` capability | **Also supported** — MSAL.NET runs on .NET Framework; see below | **Not a gap** |
| Modern crypto / TLS | Follows OS + modern .NET defaults | Follows OS; TLS 1.3 support depends on Windows version and is not exposed by .NET Framework's `SslProtocols` the way modern .NET does | Medium |

**CAE — answering the specific question.** CAE is *not* lost on .NET Framework. It is an MSAL feature driven by declaring the `cp1` client capability and handling the `WWW-Authenticate` claims challenge. MSAL.NET supports both on .NET Framework:

> "Because risk and policy are evaluated in real time, some resource APIs token lifetime can increase by up to 28 hours. These long-lived tokens are proactively refreshed by the Microsoft Authentication Library (MSAL)."
> — [How to use Continuous Access Evaluation enabled APIs](https://learn.microsoft.com/en-us/entra/identity-platform/app-resilience-continuous-access-evaluation)

```csharp
// Declare CAE readiness on the confidential client
var app = ConfidentialClientApplicationBuilder.Create(clientId)
    .WithCertificate(cert)
    .WithTenantId(tenantId)
    .WithClientCapabilities(new[] { "cp1" })
    .Build();

// Handle the claims challenge from a CAE-enabled downstream API
if (apiResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized
    && apiResponse.Headers.WwwAuthenticate.Any())
{
    string claimChallenge =
        WwwAuthenticateParameters.GetClaimChallengeFromResponseHeaders(apiResponse.Headers);

    authResult = await app.AcquireTokenSilent(scopes, account)
                          .WithClaims(claimChallenge)
                          .ExecuteAsync()
                          .ConfigureAwait(false);
}
```

— adapted from [App resilience — CAE (.NET tab)](https://learn.microsoft.com/en-us/entra/identity-platform/app-resilience-continuous-access-evaluation); Identity.Web exposes the same via [client capabilities](https://github.com/AzureAD/microsoft-identity-web/wiki/client-capabilities).

Corollary: **ADAL cannot do CAE** — the MSAL migration comparison table marks "Proactively refresh and revoke tokens based on policy or critical events … (CAE)" as MSAL-only. If Croesus has ADAL anywhere, CAE is off the table until it's gone.

> **Answer to Q3 (the headline):** **Yes, .NET Framework 4.8 can be a proper BFF for the token-replay/unbound-token concern** — auth code + PKCE, server-side token custody, encrypted `__Host-` HttpOnly/Secure/Lax session cookie, MSAL distributed token cache, CAE-aware. **The key limitation is API forwarding: there is no YARP, so the BFF's reverse-proxy layer must be hand-built in `HttpClient` or pushed out to APIM/ARR — and that hand-built proxy is where the security bugs will be.**

---

## 4. IdentityServer8 — what it actually is

### 4.1 Primary source

From the project's own documentation:

> "IdentityServer8 is an OpenID Connect and OAuth 2.0 framework for ASP.NET **DotNet 8**.
> Browse the latest [IdentityServer8 source code on GitHub](https://github.com/alexhiggins732/IdentityServer8) or download the [latest IdentityServer8 packages](https://www.nuget.org/packages/HigginsSoft.IdentityServer8/) on NuGet.
>
> **Warning** — This is a revival of the archived IdentityServer4 project which started a new company as of Oct, 1st 2020. The new Duende IdentityServer is no longer free open source, but now has various commercial licenses and paid upgrade package. **IdentityServer8 and dependencies have been upgraded to DotNet 8 and will be maintained by HigginsSoft, Alexander Higgins and the community as an Open Source project.**"
> — [IdentityServer8 documentation](https://identityserver8.readthedocs.io/en/latest/)

### 4.2 What this means, factually

| Question | Answer |
| --- | --- |
| Official Microsoft project? | **No.** |
| Official Duende project? | **No.** Explicitly positioned as an alternative *to* Duende's commercial licensing. |
| A fork? | **Yes** — a revival fork of the archived IdentityServer4. |
| Who maintains it? | **HigginsSoft / Alexander Higgins** and "the community." A single named individual/microbusiness. |
| NuGet ID | `HigginsSoft.IdentityServer8` — a **vendor-prefixed, non-reserved** package ID, not `IdentityServer8`. |
| Runtime | **ASP.NET Core / .NET 8.** |
| .NET Foundation? | The docs page claims *"It is also part of the .NET Foundation which provides governance and legal backing."* — **this is copied boilerplate from the original IdentityServer4 documentation and does not apply to this fork.** Treat as a red flag, not a credential. |

### 4.3 The upstream codebase's own security status

This is the decisive fact. Duende's archived IdentityServer4 repository states:

> "This project is not maintained anymore and is now archived.
> IdentityServer4 … **went out of support when .NET Core 3.1 end of support was reached (13th Dec 2022).**
> …
> **IdentityServer4 contains multiple known security vulnerabilities and bugs, and has outdated documentation.** As explained in more detail on the Duende blog, the decision was made to archive the IdentityServer4 repository and code in the current DuendeArchive GitHub organization."
> — [DuendeArchive/IdentityServer4 README](https://github.com/IdentityServer/IdentityServer4) (repo archived 2025-03-06; redirects from `IdentityServer/IdentityServer4`)

So: **IdentityServer8 is a community fork of a codebase whose original owners publicly state it contains multiple known security vulnerabilities.** Whether the fork has remediated them is unaudited and unwarranted.

### 4.4 Duende IdentityServer (the supported successor)

* Commercial, source-available, ASP.NET Core. — [Duende IdentityServer](https://duendesoftware.com/products/identityserver)
* Free **Community Edition** for qualifying small companies and non-profits — Croesus, as a commercial fintech, would almost certainly need a paid licence. — [Duende Community Edition](https://duendesoftware.com/products/communityedition)
* Duende publishes an explicit [IdentityServer4 upgrade path](https://duendesoftware.com/upgrade-identityserver4).
* Duende publishes a direct [Entra ID vs Duende IdentityServer comparison](https://duendesoftware.com/compare/entra-id-vs-duende-identityserver) — worth reading before any "do we need an STS?" conversation.

### 4.5 `Duende.BFF` — relevant, but not reachable from .NET Framework

> "Duende.BFF is a framework for building services that solve security and identity problems in browser based applications such as SPAs and Blazor WASM applications… This backend is called the Backend For Frontend (BFF) host, and is responsible for all of the OAuth and OIDC protocol interactions. **Moving the protocol handling out of JavaScript provides important security benefits** and works around changes in browser privacy rules that increasingly disrupt OAuth and OIDC protocol flows in browser based applications."
> — [NuGet: Duende.BFF](https://www.nuget.org/packages/Duende.BFF)

* **Duende.BFF 4.3.0 targets .NET 10.0 only.** It **cannot** be used from a .NET Framework 4.8.1 app. — ibid.
* Licensing: *"Duende.BFF is source-available, but requires a paid license for production use."* — ibid.
* Feature set worth borrowing conceptually even if not adopting: session and token management, API endpoint protection, back-channel logout notification, `X-CSRF` header enforcement on BFF endpoints, server-side session storage, mTLS/DPoP/JAR/JWT client auth.

### 4.6 Recommendation on introducing a self-hosted STS

> **Recommendation R3 — Croesus should NOT introduce IdentityServer8, and should not introduce a self-hosted STS at all, given they already have Entra ID.**
>
> Reasoning:
> 1. **It cannot solve the stated problem.** IdentityServer8 is .NET 8 / ASP.NET Core only. It cannot run in-process inside a .NET Framework 4.8.1 Web Forms app. Adopting it means standing up a *separate* ASP.NET Core service — at which point, if you're deploying ASP.NET Core anyway, you should deploy the **incremental-migration BFF** (§5 Stage 3) and get YARP + `Microsoft.Identity.Web`, not a second IdP.
> 2. **Supply-chain risk.** Single-maintainer fork; non-reserved NuGet ID; a `.NET Foundation` claim that does not hold; derived from a codebase its original authors describe as carrying multiple known security vulnerabilities. For a financial-services workload with an audit trail, this is not defensible.
> 3. **It adds an IdP where one already exists.** Entra ID is the authority. A self-hosted STS in front of Entra ID adds a second token-issuing trust boundary, a second key-rotation obligation, a second thing to patch, a second thing to monitor, and a second thing to explain to an auditor — while *removing* the CA/CAE/risk-signal capabilities that come from talking to Entra ID directly.
> 4. **If a federation gateway is genuinely required** (e.g. Croesus must issue tokens to *their* customers' apps, or must bridge SiteMinder/SAML during transition), the supported options are **Duende IdentityServer (commercial)** or **Entra External ID / Entra ID B2B-B2C**, not a community fork.
>
> Suggested framing for the "pas certain de identity server — à confirmer" note: *IdentityServer8 is not an official or vendor-supported product; it is a one-maintainer community revival of a retired codebase, runs only on modern .NET, and would not be used inside the 4.8.1 app anyway. Recommend closing this thread.*

---

## 5. Migration sequencing recommendation

### 5.0 The reframe — the incremental migration story changes the order

Microsoft's official position on large ASP.NET Framework estates:

> "Updating an app from ASP.NET Framework to ASP.NET Core is non-trivial for the majority of production apps… **Incremental migration is an implementation of the Strangler Fig pattern. It's best for larger projects or projects that need to continue to stay in production throughout a migration.**"
> — [Migrate from ASP.NET Framework to ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/migration/fx-to-core/) (the `/migration/inc/overview` URL now canonicalises here)

Their decision guide maps Croesus almost perfectly onto incremental:

> - Need to stay in production during migration → **Incremental migration**
> - Large production apps → **Incremental migration is safer**
> - Unknown or out-of-date dependencies → **Incremental migration**
> - Heavy use of System.Web → **Incremental migration**
> — ibid.

The mechanism:

> "For a large migration, we recommend setting up an ASP.NET Core app that **proxies to the original .NET Framework app**… Create a new ASP.NET Core project alongside your existing ASP.NET Framework app; **configure it to proxy requests to your original application using YARP (Yet Another Reverse Proxy)**; set up the basic infrastructure for incremental migration."
> — [Get started with incremental ASP.NET to ASP.NET Core migration](https://learn.microsoft.com/en-us/aspnet/core/migration/fx-to-core/start)

**The key insight for Croesus: the ASP.NET Core front app IS the BFF.** It is where YARP lives, where `Microsoft.Identity.Web` lives, where OIDC to Entra ID lives, where tokens are held, and where the session cookie is issued. The legacy ASPX app sits behind it and, initially, doesn't change at all.

That means the BFF does **not** have to wait for a rewrite, and arguably should not wait for the 4.5.2 retarget either (though the retarget remains independently necessary for the unpatched-runtime finding).

#### 5.0.1 Remote authentication — how the two apps share identity

`System.Web.Adapters` provides **remote authentication**: the ASP.NET Core app defers authentication to the ASP.NET Framework app over an API-key-protected internal endpoint.

> "The System.Web adapters' remote authentication feature allows an ASP.NET Core app to determine a user's identity by deferring to an ASP.NET app."
> 1. "if remote app authentication is the default scheme or specified by the request's endpoint, the `RemoteAuthenticationAuthHandler` will attempt to authenticate the user."
> 2. "The handler makes an HTTP request to the ASP.NET app's authenticate endpoint, forwarding configured headers from the current request (by default, `Authorization` and `Cookie` headers)."
> 3. "The ASP.NET app processes the authentication and returns either a serialized `ClaimsPrincipal` or an HTTP status code indicating failure."
> 4. "The ASP.NET Core app uses the result to establish the user's identity or handle authentication challenges."
> — [ASP.NET Framework to Core Authentication Migration](https://learn.microsoft.com/en-us/aspnet/core/migration/fx-to-core/areas/authentication)

**Framework-side wiring:**

```csharp
// Global.asax.cs (ASP.NET Framework)
HttpApplicationHost.RegisterHost(builder =>
{
    builder.AddSystemWebAdapters()
        .AddProxySupport(options => options.UseForwardedHeaders = true)
        .AddRemoteAppServer(options =>
        {
            options.ApiKey = ConfigurationManager.AppSettings["RemoteAppApiKey"];
        })
        .AddAuthenticationServer();
});
```

**Core-side wiring:**

```csharp
// Program.cs (ASP.NET Core BFF)
builder.Services.AddSystemWebAdapters()
    .AddRemoteAppClient(options =>
    {
        options.RemoteAppUrl = new Uri(builder.Configuration
            ["ReverseProxy:Clusters:fallbackCluster:Destinations:fallbackApp:Address"]);
        options.ApiKey = builder.Configuration["RemoteAppApiKey"];
    })
    .AddAuthenticationClient(true);   // true => remote auth is the DEFAULT scheme

// ...
app.UseAuthentication();   // after routing, before authorization
```

— ibid.

Direction matters. There are **three** supported patterns and Croesus wants the *third*:

| Pattern | Who authenticates | Fits Croesus? |
| --- | --- | --- |
| Remote authentication | Legacy 4.x app authenticates; Core BFF defers to it | Transitional only — keeps SiteMinder/legacy auth as the authority. Useful in week 1, wrong as an end state. |
| Shared cookie authentication | Both apps share an OWIN cookie + synchronized data-protection keys | Good if the 4.x app already uses `Microsoft.Owin` cookie auth. Best performance, no extra HTTP hop. |
| **Core BFF authenticates with Entra ID; legacy app trusts the forwarded principal** | **ASP.NET Core BFF** owns OIDC/PKCE/tokens; legacy app receives an already-authenticated request | **Yes — this is the target architecture.** |

Microsoft's own decision guide:

> "Do both your ASP.NET Framework and ASP.NET Core apps need to access the same authentication state? Yes → Can you configure matching data protection settings between both apps? **Yes → Shared cookie authentication (best performance). No or unsure → Remote authentication.**"
> — ibid.

Shared-cookie references: [Cookie sharing between ASP.NET and ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/cookie-sharing), [ASP.NET machineKey to ASP.NET Core migration](https://learn.microsoft.com/en-us/aspnet/core/migration/fx-to-core/areas/machine-key), working samples: [AuthRemoteIdentityFramework](https://github.com/dotnet/systemweb-adapters/tree/main/samples/AuthRemoteIdentity/AuthRemoteIdentityFramework) / [AuthRemoteIdentityCore](https://github.com/dotnet/systemweb-adapters/tree/main/samples/AuthRemoteIdentity/AuthRemoteIdentityCore).

**Documented limitations Croesus must know up front:**

> 1. **Windows Authentication**: "Because Windows authentication depends on a handle to a Windows identity, Windows authentication is not supported by this feature." ([dotnet/systemweb-adapters#246](https://github.com/dotnet/systemweb-adapters/issues/246))
> 2. **User Management Actions**: "all actions related to users (logging on, logging off, etc.) still need to be routed through the ASP.NET app."
> 3. **Performance**: "Each authentication request requires an HTTP call to the ASP.NET app."
> — ibid.

Plus a YARP-specific gotcha worth flagging in the design:

> "When using remote authentication with YARP-based fallback to the ASP.NET Framework app, make sure that fallback requests don't invoke remote authentication… use routing short-circuit metadata on the YARP fallback route… or don't use remote authentication as the default authentication scheme."
> — ibid.

#### 5.0.2 Illustrative YARP + adapters BFF skeleton

```csharp
// Program.cs — ASP.NET Core BFF fronting the legacy Croesus 4.x app
var builder = WebApplication.CreateBuilder(args);

// 1. OIDC to Entra ID + server-side token custody (Microsoft.Identity.Web)
builder.Services
    .AddMicrosoftIdentityWebAppAuthentication(builder.Configuration, "AzureAd")
    .EnableTokenAcquisitionToCallDownstreamApi(
        builder.Configuration.GetSection("DownstreamApi:Scopes").Get<string[]>())
    .AddDistributedTokenCaches();          // SQL Server / Redis-backed token cache

builder.Services.AddStackExchangeRedisCache(o => o.Configuration = builder.Configuration["Redis"]);

// 2. Session cookie hardening — the only credential the browser holds
builder.Services.Configure<CookieAuthenticationOptions>(
    CookieAuthenticationDefaults.AuthenticationScheme, o =>
{
    o.Cookie.Name        = "__Host-Croesus.Session";
    o.Cookie.HttpOnly    = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite    = SameSiteMode.Lax;
    o.SlidingExpiration  = true;
});

// 3. System.Web adapters — session/identity bridge to the legacy app
builder.Services.AddSystemWebAdapters()
    .AddRemoteAppClient(o =>
    {
        o.RemoteAppUrl = new Uri(builder.Configuration["LegacyApp:Url"]!);
        o.ApiKey       = builder.Configuration["RemoteAppApiKey"]!;
    })
    .AddAuthenticationClient(false);   // false: BFF owns Entra auth; use remote auth only where needed

// 4. YARP — the reverse proxy that .NET Framework cannot give you
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(ctx =>
    {
        // Attach a server-held access token to downstream API calls.
        // The browser never sees it.
        ctx.AddRequestTransform(async transformContext =>
        {
            var tokenAcquisition = transformContext.HttpContext.RequestServices
                .GetRequiredService<ITokenAcquisition>();
            var token = await tokenAcquisition.GetAccessTokenForUserAsync(
                new[] { "api://croesus-api/.default" });
            transformContext.ProxyRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        });
    });

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseSystemWebAdapters();

// Migrated routes land here first...
app.MapControllers().RequireAuthorization();

// ...everything else falls through to the legacy 4.x app.
// ShortCircuit() prevents the fallback from re-running remote authentication.
app.MapReverseProxy().ShortCircuit();

app.Run();
```

```jsonc
// appsettings.json — YARP fallback cluster
{
  "ReverseProxy": {
    "Routes": {
      "fallbackRoute": {
        "ClusterId": "fallbackCluster",
        "Match": { "Path": "{**catch-all}" }
      }
    },
    "Clusters": {
      "fallbackCluster": {
        "Destinations": {
          "fallbackApp": { "Address": "https://legacy-croesus.internal/" }
        }
      }
    }
  }
}
```

*(The `Program.cs` above is an illustrative composition of the patterns from the cited Learn articles and the Identity.Web wiki; the `AddSystemWebAdapters`/`AddRemoteAppClient`/`ShortCircuit` fragments and the YARP fallback-cluster config key are taken directly from those sources. The `AddTransforms` token-attachment block is idiomatic YARP + Identity.Web and should be validated against the current package versions before use.)*

**Tooling accelerators worth naming to Croesus:**

* [GitHub Copilot app modernization for .NET](https://learn.microsoft.com/en-us/dotnet/core/porting/github-copilot-app-modernization/overview) — "can assess your solution, generate an upgrade plan, and automate many of the migration steps."
* [Learn to upgrade from ASP.NET MVC, Web API, and Web Forms to ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/migration/fx-to-core/tooling) — Visual Studio tooling for setting up the incremental projects.
* [.NET Generic Host in ASP.NET Framework](https://learn.microsoft.com/en-us/aspnet/core/migration/fx-to-core/areas/hosting) — brings modern DI/config/logging into the *Framework* app, reducing the Stage-3 delta.
* [System.Web adapters](https://learn.microsoft.com/en-us/aspnet/core/migration/fx-to-core/inc/systemweb-adapters) — enables `HttpContext` usage in shared class libraries multi-targeted to `netstandard2.0`/`net8.0`.

### 5.1 Staged plan

| Stage | What | Effort | Risk | What it buys |
| --- | --- | --- | --- | --- |
| **0 — Retarget 4.5.2 → 4.8** | `compilation`/`httpRuntime targetFramework`, `TargetFrameworkVersion`, refresh every `packages.config` `targetFramework`, upgrade all NuGet packages to net48-compatible versions | **M** (days to retarget, weeks to regression-test) | **M** | Closes the "running an EOL runtime with SHA-1-era signing" finding. Unlocks `HttpCookie.SameSite` (4.7.2+), `Microsoft.Identity.Web.OWIN` (net472+), modern MSAL. **Prerequisite for everything else.** |
| **0b — (only if 4.8.1 is genuinely required)** OS upgrade to Windows Server 2022/2025 | Server build, IIS config, app-pool, certs, load balancer | **L** | **M-H** | Arm64 + WCAG tooltips — **no web-relevant benefit.** Recommend deferring unless OS lifecycle drives it independently. |
| **1 — Replace SiteMinder/custom auth with OWIN OIDC to Entra ID** | `Microsoft.Owin.Host.SystemWeb` + `.Security.Cookies` + `.Security.OpenIdConnect` 4.2.3, layered with `Microsoft.Identity.Web.OWIN`; `ResponseType = Code`, `UsePkce = true`, `RedeemCode = true`; MSAL distributed token cache (SQL/Redis); `__Host-` HttpOnly/Secure/Lax session cookie | **M-L** | **M** | **This is where the token-replay concern is actually solved.** Tokens server-side, browser holds only an encrypted session cookie. Entra ID CA + CAE become available. Retires SiteMinder dependency. |
| **2 — Introduce a BFF boundary** | Option 2a: in-app `HttpClient` passthrough controller attaching server-held tokens. Option 2b: front the APIs with **Azure API Management** (JWT validation, throttling, WAF-adjacent policy). | 2a: **M**, 2b: **M** | 2a: **M-H** (hand-rolled proxy = bug surface), 2b: **M** | Clean separation of browser session from API tokens; central policy/observability point. |
| **3 — Strangler-fig to modern .NET** | Stand up an **ASP.NET Core BFF** in front, wired with **YARP** + `System.Web.Adapters`; migrate routes incrementally; `Microsoft.Identity.Web` owns OIDC; legacy 4.x app behind the proxy | **L** (continuous) | **L-M** (incremental, always in production) | Real YARP, real `Microsoft.Identity.Web`, OpenTelemetry, modern DI, `Duende.BFF` becomes an option. Legacy retired route-by-route. |

### 5.2 The sequencing recommendation, stated plainly

> **Recommendation R4 — Run Stage 0 and Stage 3 in parallel; treat Stage 3 as the BFF, not as a future rewrite.**
>
> * **Stage 0 (retarget to 4.8) is non-negotiable and urgent** — it's the unpatched-runtime finding, and it's the prerequisite for `Microsoft.Identity.Web.OWIN` and correct SameSite. Do it regardless of everything else. Target **4.8**, not 4.8.1, unless the OS is already WS2022+.
> * **Do NOT sequence Stage 3 last.** Microsoft's incremental-migration guidance means the ASP.NET Core BFF can be stood up **in front of the existing app** without rewriting a single ASPX page. That BFF is where YARP, `Microsoft.Identity.Web`, PKCE, server-side tokens, CAE, and OpenTelemetry all live — i.e. it is the *complete* answer to the BFF requirement, available now.
> * **Stage 1 (in-app OWIN OIDC) is then a choice, not a requirement.** If the Core BFF owns authentication, the legacy app can simply trust the forwarded principal (shared cookie or remote auth). Stage 1 is worth doing only if Croesus wants the 4.x app to remain independently deployable and independently authenticating — which is a legitimate position for a regulated workload with a long tail.
> * **Stage 2 (in-app passthrough) is the weakest option** and should be skipped if Stage 3 is on the table. Hand-rolled `HttpClient` proxies are where the CVEs come from.
>
> The compressed pitch: *"You don't need to rewrite to get a BFF. Retarget to 4.8 for the security finding, then put an ASP.NET Core + YARP BFF in front using Microsoft's supported System.Web.Adapters incremental-migration path. The legacy app doesn't change. You get PKCE, server-side tokens, CAE, and a migration runway in the same move."*

---

## 6. Decision table — Croesus options A / B / C

| | **Option A — Stay 4.x, retarget + in-app OWIN OIDC BFF** | **Option B — Retarget + ASP.NET Core / YARP BFF in front (incremental migration)** | **Option C — Introduce a self-hosted STS (IdentityServer8 / Duende)** |
| --- | --- | --- | --- |
| **Shape** | Single 4.8 app. Katana OIDC + `Microsoft.Identity.Web.OWIN` + MSAL distributed cache. API calls via hand-rolled `HttpClient` passthrough or APIM. | Two apps. ASP.NET Core BFF (YARP + `Microsoft.Identity.Web`) fronts the 4.8 legacy app via `System.Web.Adapters`. Routes migrate incrementally. | Additional ASP.NET Core STS between Croesus apps and Entra ID. |
| **Solves token-replay / unbound-token?** | **Yes.** Auth code + PKCE (S256), tokens in server-side MSAL cache, browser gets only an encrypted `__Host-` HttpOnly/Secure/Lax cookie. | **Yes, and better.** Same custody model, plus YARP-level token attachment, plus `Duende.BFF` available later for anti-forgery header enforcement and back-channel logout. | **No — it makes it worse.** Adds a second token issuer and a second set of tokens to protect, without changing browser-side custody unless a BFF is *also* built. |
| **PKCE** | Yes — `Microsoft.Owin.Security.OpenIdConnect >= 4.2.0`, `UsePkce=true` **and** `ResponseType=Code` (both required) | Yes — `Microsoft.Identity.Web` / ASP.NET Core OIDC, PKCE on by default | N/A — doesn't address it |
| **Reverse proxy** | **No YARP.** Hand-rolled `HttpClient`, or ARR/APIM out of process | **YARP** — config-driven, streaming, WebSockets, health checks | N/A |
| **Identity library** | `Microsoft.Identity.Web.OWIN` (net472+) — supported, reduced surface | `Microsoft.Identity.Web` — full surface | Either, plus a whole STS to operate |
| **CAE support** | **Yes** — MSAL + `cp1` + claims-challenge handling | **Yes** | Degraded — an intermediary STS typically breaks the CAE signal chain to Entra ID |
| **Telemetry** | Classic App Insights; no first-class OpenTelemetry | OpenTelemetry-native in the BFF; legacy app instrumented separately | Adds a third thing to instrument |
| **OS dependency** | 4.8: none (WS2008R2–WS2022). 4.8.1: **forces WS2022+** | Same for the 4.x half; BFF runs anywhere .NET 8+ runs (incl. Linux containers) | ASP.NET Core host required regardless |
| **Effort** | **Medium** | **Medium-High up front, then continuous and incremental** | **High** |
| **Risk** | Medium — hand-rolled proxy is the weak point; Web Forms CSRF posture depends on `ViewStateUserKey` discipline | **Low-Medium** — Microsoft-supported pattern, app stays in production throughout, rollback = route the YARP fallback back | **High** — IdentityServer8 is a single-maintainer fork of a codebase with known vulnerabilities; Duende is licensed and still doesn't remove Entra ID |
| **Migration runway** | **Dead end.** Every line written is thrown away at rewrite time. | **This IS the migration.** The BFF becomes the app; legacy retires route-by-route. | Sideways — adds scope without advancing the runway |
| **Vendor support posture** | Libraries supported; **samples archived** (June 2024) | Fully supported, actively documented, tooling-assisted (Copilot app modernization) | IdentityServer8: none. Duende: commercial. |
| **Audit story** | "We retargeted to a supported runtime and moved tokens server-side." Good. | "We retargeted to a supported runtime, moved tokens server-side, and are executing Microsoft's supported incremental migration." **Best.** | "We introduced a community-maintained fork of a retired identity product into a financial-services auth path." **Very hard to defend.** |
| **Verdict** | Acceptable if a second runtime is politically or operationally impossible | **Recommended** | **Reject** |

---

## 7. Open items / clarifying questions for Croesus

1. **What Windows Server version(s) is the estate actually on?** This single answer decides 4.8 vs 4.8.1 and whether Stage 0 is a config change or an OS programme. (WS2016/2019 ⇒ 4.8.1 is off the table without an OS migration.)
2. **Is there any ADAL.NET in the codebase?** ADAL support and security fixes ended 2023-06-30. If present it is a second finding and it blocks CAE.
3. **Web Forms vs MVC split.** What proportion of routes are `.aspx` versus MVC controllers? Drives both the CSRF posture (§3.4) and the realistic Stage 3 route-migration order.
4. **What is SiteMinder actually doing today** — header-based SSO injection, an ISAPI filter, a WAM agent, a SAML IdP in front of Entra ID? The replacement design (and whether remote authentication is even needed) hinges on this.
5. **Is `<machineKey>` synchronized across the web farm?** Required for OWIN cookie protection on multiple nodes, and required again for the shared-cookie option in Stage 3.
6. **Any cross-site POST, `<iframe>` embedding, or partner form-post integrations?** These will break when SameSite defaults flip to `Lax` during the retarget.
7. **Any SignalR / WebSocket / long-polling features?** These do not survive a naive `HttpClient` passthrough (Option A, Stage 2).
8. **Is a second runtime (ASP.NET Core) politically/operationally acceptable?** This is the fork in the road between Option A and Option B, and it is an organisational question, not a technical one.
9. **Does Croesus need to *issue* tokens to third parties**, or only *consume* Entra ID? If only consume, the STS conversation closes immediately.
10. **Which .NET version would the Stage 3 BFF target?** .NET 8 (LTS) vs .NET 10 (LTS) matters for `Duende.BFF` (4.3.0 is .NET 10 only) and for the support window.

## 8. Recommended next research (not completed in this session)

- [ ] **Verify `Microsoft.Identity.Web.OWIN` API surface in practice** — pull the actual `Microsoft.Identity.Web.OWIN` sample (`tests/DevApps` or equivalent in `AzureAD/microsoft-identity-web`) and confirm the exact `OwinTokenAcquirerFactory` / `AddMicrosoftIdentityWebApp` extension names and the real `Startup.Auth.cs`. The wiki page for OWIN specifically (as opposed to the general `asp-net` page) was not retrievable; the code shape in §2.6 is constructed from Katana source + Identity.Web wiki and should be validated against a live sample before being quoted to the customer.
- [ ] **Retrieve the archived `ms-identity-aspnet-webapp-openidconnect` `archive` branch** `WebApp/App_Start/Startup.Auth.cs` and `WebApp/Utils/MsalAppBuilder.cs` verbatim — useful as a "here is the historical reference implementation, note it is archived" artefact.
- [ ] **Confirm Windows Server 2019 lifecycle dates.** The Learn install table marks WS2019 ❌, which appears to reflect *mainstream* end (Jan 2024) rather than extended end (Jan 2029). Worth pinning precisely before telling Croesus their OS is "unsupported."
- [ ] **Enumerate the full 4.5.2 → 4.8 retargeting breaking-change list** from [Retargeting changes 4.8.x](https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/retargeting/4.8.x) plus the 4.6.x / 4.7.x retargeting pages, and produce a Croesus-specific triage (FIPS crypto, DeflateStream, DataContractJsonSerializer, ASP.NET request validation, `HttpRuntime` quirks).
- [ ] **TLS 1.3 posture on .NET Framework 4.8 / Windows Server 2022** — relevant to a financial-services security review; not covered here.
- [ ] **Token-binding / DPoP feasibility.** The session's "unbound token" language hints at a sender-constraint requirement. Entra ID's proof-of-possession story (MSAL `MsAuth10ATPop`, token protection) on .NET Framework was not researched and may be material.
- [ ] **Cost and latency modelling for APIM** as the Stage 2b option, if Option A is selected.
- [ ] **Data-protection key management design** for the shared-cookie variant of Stage 3 (`<machineKey>` ↔ ASP.NET Core Data Protection interop) — see [machine-key migration](https://learn.microsoft.com/en-us/aspnet/core/migration/fx-to-core/areas/machine-key).
- [ ] **Confirm whether `Duende.BFF` ships a .NET 8-targeting version**; 4.3.0 is .NET 10 only, but an older 3.x line may target .NET 8 and would matter if Croesus standardises on .NET 8 LTS.

## 9. Source index

**Lifecycle**

- [.NET Framework Support Policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-framework)
- [Microsoft .NET Framework — Microsoft Lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-framework)
- [Install .NET Framework on Windows (incl. Windows Server table)](https://learn.microsoft.com/en-us/dotnet/framework/install/on-windows-and-server)
- [.NET Framework & Windows OS versions and dependencies](https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/versions-and-dependencies)
- [What's new in .NET Framework](https://learn.microsoft.com/en-us/dotnet/framework/whats-new/)
- [Announcing .NET Framework 4.8.1](https://devblogs.microsoft.com/dotnet/announcing-dotnet-framework-481/)
- [.NET Framework 4.5.2, 4.6, 4.6.1 end of support](https://devblogs.microsoft.com/dotnet/net-framework-4-5-2-4-6-4-6-1-will-reach-end-of-support-on-april-26-2022)
- [Application compatibility in .NET Framework](https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/application-compatibility)

**OWIN / Katana**

- [aspnet/AspNetKatana](https://github.com/aspnet/AspNetKatana)
- [AspNetKatana releases (4.2.0 PKCE, 4.2.2 CVE-2022-29117, 4.2.3)](https://github.com/aspnet/AspNetKatana/releases)
- [`OpenIdConnectAuthenticationOptions.cs`](https://github.com/aspnet/AspNetKatana/blob/main/src/Microsoft.Owin.Security.OpenIdConnect/OpenIdConnectAuthenticationOptions.cs)
- [`OpenidConnectAuthenticationHandler.cs`](https://github.com/aspnet/AspNetKatana/blob/main/src/Microsoft.Owin.Security.OpenIdConnect/OpenidConnectAuthenticationHandler.cs)
- [`OpenIdConnectMiddlewareTests.cs`](https://github.com/aspnet/AspNetKatana/blob/main/tests/Microsoft.Owin.Security.Tests/OpenIdConnect/OpenIdConnectMiddlewareTests.cs)
- [NuGet: Microsoft.Owin.Security.OpenIdConnect](https://www.nuget.org/packages/Microsoft.Owin.Security.OpenIdConnect)
- [OWIN and Katana overview](https://learn.microsoft.com/en-us/aspnet/aspnet/overview/owin-and-katana/)

**Identity libraries**

- [NuGet: Microsoft.Identity.Web](https://www.nuget.org/packages/Microsoft.Identity.Web)
- [NuGet: Microsoft.Identity.Web.OWIN](https://www.nuget.org/packages/Microsoft.Identity.Web.OWIN)
- [microsoft-identity-web wiki — Home / Roadmap](https://github.com/AzureAD/microsoft-identity-web/wiki)
- [microsoft-identity-web wiki — Support for ASP.NET classic, .NET 4.7.2, .NET Standard 2.0](https://github.com/AzureAD/microsoft-identity-web/wiki/asp-net)
- [microsoft-identity-web wiki — Token cache serialization](https://github.com/AzureAD/microsoft-identity-web/wiki/token-cache-serialization)
- [microsoft-identity-web wiki — Client capabilities](https://github.com/AzureAD/microsoft-identity-web/wiki/client-capabilities)
- [Migrate to MSAL (ADAL EOL 2023-06-30)](https://learn.microsoft.com/en-us/entra/identity-platform/msal-migration)
- [ADAL.NET → MSAL.NET migration](https://learn.microsoft.com/en-us/entra/msal/dotnet/how-to/msal-net-migration)
- [Continuous Access Evaluation in apps](https://learn.microsoft.com/en-us/entra/identity-platform/app-resilience-continuous-access-evaluation)
- [Claims challenges, claims requests, client capabilities](https://learn.microsoft.com/en-us/entra/identity-platform/claims-challenge)

**Samples**

- [Azure-Samples/ms-identity-aspnet-webapp-openidconnect — ARCHIVED 2024-06-12](https://github.com/Azure-Samples/ms-identity-aspnet-webapp-openidconnect)
- [Azure-Samples/ms-identity-docs-code-dotnet/web-app-aspnet (current, ASP.NET Core)](https://github.com/Azure-Samples/ms-identity-docs-code-dotnet/tree/main/web-app-aspnet)
- [Azure-Samples/active-directory-aspnetcore-webapp-openidconnect-v2](https://github.com/Azure-Samples/active-directory-aspnetcore-webapp-openidconnect-v2)
- [ConfidentialClientTokenCache (.NET Framework token cache)](https://github.com/Azure-Samples/active-directory-dotnet-v1-to-v2/tree/master/ConfidentialClientTokenCache)

**Cookies / SameSite / CSRF**

- [Work with SameSite cookies in ASP.NET](https://learn.microsoft.com/en-us/aspnet/samesite/system-web-samesite)
- [KB articles that support SameSite in .NET Framework](https://learn.microsoft.com/en-us/aspnet/samesite/kbs-samesite)

**Incremental migration / BFF**

- [Migrate from ASP.NET Framework to ASP.NET Core (overview)](https://learn.microsoft.com/en-us/aspnet/core/migration/fx-to-core/)
- [Get started with incremental migration](https://learn.microsoft.com/en-us/aspnet/core/migration/fx-to-core/start)
- [ASP.NET Framework to Core Authentication Migration (remote auth + shared cookies)](https://learn.microsoft.com/en-us/aspnet/core/migration/fx-to-core/areas/authentication)
- [System.Web adapters](https://learn.microsoft.com/en-us/aspnet/core/migration/fx-to-core/inc/systemweb-adapters)
- [Remote app setup](https://learn.microsoft.com/en-us/aspnet/core/migration/fx-to-core/inc/remote-app-setup)
- [.NET Generic Host in ASP.NET Framework](https://learn.microsoft.com/en-us/aspnet/core/migration/fx-to-core/areas/hosting)
- [machineKey → ASP.NET Core Data Protection](https://learn.microsoft.com/en-us/aspnet/core/migration/fx-to-core/areas/machine-key)
- [Cookie sharing between ASP.NET and ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/cookie-sharing)
- [dotnet/systemweb-adapters samples — AuthRemoteIdentity](https://github.com/dotnet/systemweb-adapters/tree/main/samples/AuthRemoteIdentity)
- [GitHub Copilot app modernization for .NET](https://learn.microsoft.com/en-us/dotnet/core/porting/github-copilot-app-modernization/overview)

**IdentityServer**

- [IdentityServer8 documentation](https://identityserver8.readthedocs.io/en/latest/)
- [alexhiggins732/IdentityServer8](https://github.com/alexhiggins732/IdentityServer8)
- [NuGet: HigginsSoft.IdentityServer8](https://www.nuget.org/packages/HigginsSoft.IdentityServer8/)
- [DuendeArchive/IdentityServer4 — archived, "multiple known security vulnerabilities"](https://github.com/IdentityServer/IdentityServer4)
- [Duende IdentityServer](https://duendesoftware.com/products/identityserver)
- [Duende Community Edition](https://duendesoftware.com/products/communityedition)
- [Duende — Upgrade IdentityServer4](https://duendesoftware.com/upgrade-identityserver4)
- [Duende — Entra ID vs Duende IdentityServer](https://duendesoftware.com/compare/entra-id-vs-duende-identityserver)
- [NuGet: Duende.BFF (net10.0 only)](https://www.nuget.org/packages/Duende.BFF)
