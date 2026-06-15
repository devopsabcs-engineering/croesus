<!-- markdownlint-disable-file -->
# Research: Croesus GPD Central SaaS SSO / Conditional Access / OAuth Evidence

Status: In-Progress (Partial — 2 of 3 binary documents are RMS-encrypted and unreadable)
Date: 2026-06-15
Researcher mode: Researcher Subagent (RESEARCH ONLY — no files modified except this document)

## Research Questions (original scope)

1. Is the observed "second non-interactive sign-in from the Croesus SaaS AWS IP" EXPECTED OAuth/SaaS behaviour, or a MISCONFIGURATION?
2. Decision support: Option A (fix internally — Entra / app-registration / Conditional Access config) vs Option B (escalate to Croesus SaaS vendor).

## Source Inventory

| File | Type | Status | Notes |
| --- | --- | --- | --- |
| assets/app-registration-analysis.md | Markdown | READ | Customer-intent analysis (not raw log evidence) |
| assets/dev-dev.txt | JSON app-reg export | READ | appId 713d6ede-38a2-45f4-8982-89ea4fcf1a7f |
| assets/dev-prod.txt | JSON app-reg export | READ | appId e3e358ea-0aae-4f11-b866-00f76b1cf6c1 |
| assets/prod-prod.txt | JSON app-reg export | READ | appId 92dd40a3-f7c2-42ff-9303-fff5928e195a |
| assets/non_prod_sso_conditional_access_report_20260529_180104.pdf | PDF (13 pp) | READ | Advisory/recommendation report — NOT raw sign-in logs |
| assets/croesus_entra_oauth_integration_report_20260529_185237.pdf | PDF | **BLOCKED** | Azure RMS / MIP encrypted. Label "Highly Confidential \ Internal Only" |
| assets/PROD.docx | DOCX (OLE2/CFB) | **BLOCKED** | Azure RMS / IRM encrypted (DRMEncryptedTransform / EncryptedPackage) |

## Extraction Gaps (Critical)

Two of the three binary documents could not be opened. Both are protected with Microsoft Information Protection (Azure RMS / IRM); decryption requires the RMS service plus the signed-in user's usage rights, which is not possible in this offline research context.

- assets/croesus_entra_oauth_integration_report_20260529_185237.pdf
  - PDF metadata confirms protection:
    - `/MSIP_Label_..._Name`: `Highly Confidential \ Internal Only`
    - `/MSIP_Label_..._SiteId`: `72f988bf-86f1-41af-91ab-2d7cd011db47` (Microsoft corporate tenant)
    - Embedded file name: `MicrosoftIRMServices Protected PDF.pdf`
  - The visible PDF body is only the stub: "This PDF Document has been protected. The reader you are using does not support opening files protected by Microsoft Office".
  - This is the document most likely to contain the authoritative OAuth flow definition (flow type, which token is reused, server-initiated vs browser-initiated). **Its contents are unknown.**
- assets/PROD.docx
  - OLE2/CFB compound file. Streams: `\x06DataSpaces/TransformInfo/DRMEncryptedTransform`, `EncryptedPackage`, `LabelInfo`.
  - `LabelInfo` siteId `{72f988bf-86f1-41af-91ab-2d7cd011db47}` (Microsoft corporate tenant), label id `{9fbde396-1a24-4c79-8edf-9254a0f35055}`.
  - Expected to contain the prod reference / working behaviour. **Its contents are unknown.**

Consequence: I have NO raw sign-in-log evidence (verbatim AADSTS error codes, exact Conditional Access policy names, per-event deviceId / compliance state, per-event source IP, client-app and resource fields). The only narrative description of the two-step behaviour comes from the readable advisory PDF, which is generic and hedged.

## Key Discovery 1 — App registrations CANNOT support a standards-compliant Entra OBO flow

Confirmed identical across all three exports (assets/dev-dev.txt, assets/dev-prod.txt, assets/prod-prod.txt):

| Property | dev-dev | dev-prod | prod-prod | Meaning |
| --- | --- | --- | --- | --- |
| platform | `spa.redirectUris` populated | `spa.redirectUris` populated | `spa.redirectUris` populated | Single-Page App (public client) |
| `web.redirectUris` | `[]` | `[]` | `[]` | No confidential web platform |
| `api.oauth2PermissionScopes` | `[]` | `[]` | `[]` | **No exposed API / no custom scope** |
| `appRoles` | `[]` | `[]` | `[]` | No app roles |
| `passwordCredentials` | `[]` | `[]` | `[]` | **No client secret** |
| `keyCredentials` | `[]` | `[]` | `[]` | **No certificate** |
| `requiredResourceAccess` | Graph `e1fe6dd8` (User.Read) only | same | same | Delegated Graph User.Read only |
| `signInAudience` | `AzureADMyOrg` | `AzureADMyOrg` | `AzureADMyOrg` | Single tenant |
| `api.acceptMappedClaims` | `true` | `true` | `true` | Claims mapping accepted |
| `web.implicitGrantSettings.enableIdTokenIssuance` | `true` | `false` | `false` | Dev-dev still allows implicit ID token |
| `web.implicitGrantSettings.enableAccessTokenIssuance` | `false` | `false` | `false` | No implicit access token |

Why this is decisive: A genuine Microsoft Entra On-Behalf-Of flow (RFC 8693 token exchange) requires the middle-tier (the Croesus backend) to be a **confidential client** with a **client secret or certificate**, AND requires the app to **expose an API** (a custom scope / Application ID URI) so the front-end can obtain an access token whose audience is that backend. These three registrations have **none of those** (no secret, no cert, `oauth2PermissionScopes: []`). Therefore:

- These specific registrations, as configured, **cannot perform a real Entra OBO**.
- Whatever the second non-interactive sign-in is, it is NOT an OBO that these registrations could legitimately support without additional configuration (an exposed API scope + a confidential-client credential on the Croesus backend).

This directly contradicts / qualifies the advisory PDF, which repeatedly labels the second event an "On-Behalf-Of (OBO) flow". The advisory itself hedges ("details depend on Croesus's design", "or even to validate the user's session"), indicating it was written generically and was not reconciled against the actual app-registration export.

## Key Discovery 2 — Readable advisory PDF describes the theory, not the evidence

assets/non_prod_sso_conditional_access_report_20260529_180104.pdf (13 pages) is an **advisory / options report**, not a raw sign-in log export. Verbatim relevant excerpts:

- Root cause framing (p.1): "Croesus's backend performs an OAuth On-Behalf-Of (OBO) token exchange from an external AWS IP, causing a second Azure AD token issuance. This second request lacks the original user device context and originates from outside the corporate network. Conditional Access re-evaluates the sign-in and fails it because the device isn't marked compliant in the test tenant (a device can only be compliant in one tenant) and the new IP is untrusted."
- On the second event being correct-by-design (p.11): "Azure AD sees a request from an untrusted IP (the vendor's AWS servers) and an unrecognized device context. If a CA policy requires a compliant or domain-joined device, the request fails ... This is a known limitation: OBO flows cannot satisfy device-based Conditional Access policies or other 'step-up' requirements without user interaction."
- Log-analysis guidance (p.12): "the user's initial interactive sign-in (Type: interactive user sign-in) followed by a second sign-in event for the OBO token request (Type: non-interactive user sign-in) ... The subsequent OBO-related sign-in will typically log an AWS cloud IP address ... Emphasize that the second sign-in is not a malicious login by the user, but the SaaS performing a server-side token request".
- Hedge on flow detail (p.11): "the SaaS's backend ... presents the user's initial access token (or a special token) to Azure AD's token endpoint, requesting a new access token for a downstream resource ... (details depend on Croesus's design)".

The PDF proposes three remediation options (verbatim names):

- Option 1: "Modify Conditional Access Policies for Non-Prod (Allow Exceptions)" — relax CA / trusted-location for the non-prod app. Security: Low. Usability: High. Complexity: Low. Cost: Minimal.
- Option 2: "Cross-Tenant Device Compliance Trust" — Entra B2B + cross-tenant access settings "Trust compliant devices" so the test tenant honors the prod tenant's device-compliance claim. Security: High. Usability: Medium-High. Complexity: Medium. Cost: Moderate.
- Option 3: "Unify or Redesign the Identity Architecture (Single Tenant or Hybrid Approach)" — one tenant for prod + test, or dedicated test-tenant-joined devices. Security: Very High. Usability: High. Complexity: High. Cost: Moderate.

The PDF contains NO verbatim AADSTS codes, NO exact CA policy names, and NO per-event log fields. (Those are presumably in the two RMS-locked files.)

## Key Discovery 3 — Cross-tenant device-compliance gap is the architectural root cause

From assets/app-registration-analysis.md and the advisory PDF, consistent facts:

- Devices are managed/compliant in the **Prod tenant** (Intune / Entra hybrid join).
- Non-prod Croesus authenticates in the **Dev tenant** (publisherDomain `MVTDEVDesjardins.onmicrosoft.com` on dev-dev; note dev-prod and prod-prod use `mvtdesjardins.onmicrosoft.com`).
- "a device can only be compliant in one tenant" — so a Prod-compliant device is "Not compliant"/"Unknown" in the Dev tenant.
- The Croesus backend's server-side token step originates from an **AWS IP** (untrusted location), with no device context.

Result: device-based Conditional Access in the dev tenant correctly fails the second (AWS-origin, no-device) event. In prod-prod, the same backend step succeeds because device + tenant + (likely) trusted-location conditions align.

## Analysis — Answer to the core question

### Is the second non-interactive sign-in EXPECTED or a MISCONFIGURATION?

Three distinct things must be separated:

1. The existence of a second, server-initiated token event from the Croesus AWS backend
   - This is a **property of how the Croesus SaaS is architected** (a server-side token step), not a toggle in the Desjardins app registration. In that sense it is "expected" of this vendor's design. It is NOT something the customer accidentally configured.
2. The Conditional Access block of that second event in non-prod
   - This is **CA working correctly / by design** (Zero Trust). A token presented from an untrusted AWS IP with no compliant-device context in the dev tenant is correctly denied. **This is NOT a misconfiguration.**
3. Whether the flow is a legitimate Entra OBO
   - **It cannot be**, given the app-registration export: no client secret, no certificate, no exposed API scope. So either (a) Croesus is doing a non-standard server-side **token reuse/replay** that Entra logs as a non-interactive sign-in (not a sanctioned OBO), or (b) Croesus genuinely intends an OBO, in which case there is ALSO a **configuration gap** — the backend would need to be registered as a confidential client (secret/cert) and these apps would need to expose an API scope. Which of these is true cannot be determined from the readable evidence; it is most likely documented in the RMS-locked OAuth report.

Bottom line: The Conditional Access denial is expected and correct. The second sign-in event is driven by the vendor's server-side design, not by a Desjardins app-registration mistake. However, the registrations as-is are inconsistent with a real OBO, so "is it expected OAuth behaviour" cannot be fully affirmed without Croesus's authoritative flow definition (locked in the encrypted documents).

### Option A vs Option B

This is **not a clean either/or** — the evidence points to **Option B first, then Option A**:

- Option B (escalate to Croesus) — REQUIRED FIRST, because:
  - The two documents containing the actual flow/log evidence are RMS-locked and unreadable here.
  - The app-registration export proves these registrations cannot do a standards OBO unaided; you cannot correctly choose an internal fix until Croesus states exactly which flow they use (true OBO needing an exposed API + secret, vs. server-side token replay, vs. something else) and from which IP ranges.
  - You should ask Croesus to confirm: the exact OAuth/OIDC flow, whether the backend is a confidential client and against which app/scope, the audience of the token it reuses, and the published AWS egress IP ranges.
- Option A (internal Entra / CA / architecture fix) — the remediation lever ONCE the flow is confirmed:
  - Most proportionate: PDF Option 2 (cross-tenant device-compliance trust via B2B) or PDF Option 1 (scoped CA exception / add Croesus AWS IP ranges as trusted location for the non-prod app only, with MFA as compensating control). PDF Option 3 (single tenant) is a large re-architecture and is a last resort.
  - The CA policy itself should NOT be "fixed" in the sense of being called wrong — it is behaving correctly. Any Option A change is a deliberate, scoped accommodation, not a bug fix.

## Follow-on Questions (in scope, currently unanswerable from readable evidence)

- [ ] What is the verbatim AADSTS error code on the blocked second event? (Locked in encrypted docs.)
- [ ] What is the exact Conditional Access policy name and grant control that blocks it? (Locked.)
- [ ] In the second event, what are the verbatim "Client App", "Resource/audience", "Source IP", and "Device compliance state" log fields? (Locked.)
- [ ] Does Croesus's backend present an Entra-issued access token, an ID token, or a refresh token in the second step? (Locked / vendor.)
- [ ] Which app registration (dev vs prod) is actually used by the AWS backend step? (Locked / vendor.)

## Clarifying Questions for the User

1. Can you provide an **unprotected / rights-removed** copy of `croesus_entra_oauth_integration_report_...pdf` and `PROD.docx`, or paste the relevant sign-in-log rows? Both current files are Azure RMS-encrypted (label "Highly Confidential \ Internal Only", Microsoft tenant) and cannot be opened in this research context — they hold the only raw flow/log evidence.
2. Has Croesus provided any written statement of the OAuth flow they use and their AWS egress IP ranges? That determines whether an exposed-API/secret config change is even needed.

## Evidence References

- assets/app-registration-analysis.md
- assets/dev-dev.txt
- assets/dev-prod.txt
- assets/prod-prod.txt
- assets/non_prod_sso_conditional_access_report_20260529_180104.pdf
- assets/croesus_entra_oauth_integration_report_20260529_185237.pdf (RMS-encrypted; metadata only)
- assets/PROD.docx (RMS-encrypted; stream structure + label metadata only)
- Microsoft Entra On-Behalf-Of flow requirements (confidential client + exposed API scope): https://learn.microsoft.com/entra/identity-platform/v2-oauth2-on-behalf-of-flow
- Cross-tenant access "Trust compliant devices": https://learn.microsoft.com/entra/external-id/cross-tenant-access-settings-b2b-collaboration
