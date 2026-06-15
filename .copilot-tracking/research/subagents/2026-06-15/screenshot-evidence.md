<!-- markdownlint-disable-file -->
# Subagent Research: Screenshot Evidence (readable docx)

Source artifact: assets/Screenshots of app registrations.docx (6.7 MB, NOT RMS-encrypted — readable OOXML; sensitivity label id {f42aa342-8706-4288-bd11-ebb85995028c}, siteId 72f988bf-86f1-41af-91ab-2d7cd011db47). Contains 16 sequential PNG screenshots (word/media/image1.png .. image16.png) captured from a recorded Teams meeting (Desjardins | Suite de la discussion (General); participants Marie-Eve Léger, Mathieu Santerre, Emmanuel Knafo; 5/29/2026). Several screenshots show a side "Meeting chat" / "JSON" pane with operator notes.

This document complements (does not replace) the three .txt manifests. It supplies live Azure Portal confirmation of the registrations AND the previously-missing raw Entra sign-in log for the second (AWS) event.

## Status

Complete — all 16 images catalogued.

## Per-screenshot catalogue

* image1 — App registration > Authentication, sp-CentralGPD-UAT-dev-fed, appId 713d6ede-38a2-45f4-8982-89ea4fcf1a7f (DEV tenant). SPA redirect URIs visible incl. SiteMinder oidc-tool.html and certif/pat LogonSso.aspx. Chat pane note: DEV (MvtDevDesjardins.com), "Corporate Sign In serv / Le service de connec...", and an Amazon IP "3.99.119.124 (Amazon Da...)". Confirms dev-dev.txt.
* image2 — Same app > Authentication settings. Implicit grant: ID tokens CHECKED, Access tokens unchecked; Allow public client flows: No. Confirms dev-dev implicit ID-token = TRUE.
* image3 — Enterprise application context for the dev-prod app (objectId/SP 323e25ff-4afb-47c1-aa3e-0d1142515473, appId e3e358ea-0aae-4f11-b866-00f76b1cf6c1). JSON pane shows app object id a2a7a9ad-8624-43.. appId e3e358ea-0aae. Permission/consent flags (GrantPermission True, etc.).
* image4 — App registration > Authentication, appId e3e358ea-0aae-4f11-b866-00f76b1cf6c1 (dev-prod, PROD tenant). Implicit grant: BOTH Access and ID tokens UNCHECKED. Confirms dev-prod implicit fully off.
* image5 — Enterprise Application > Properties for dev-prod app (SP 323e25ff..., appId e3e358ea...). Assignment required? No; Visible to users? No.
* image6 — Enterprise Application > Single sign-on (OIDC-based) for the dev-prod app. "This application uses OpenID Connect and OAuth." SSO mode = OIDC.
* image7 — Enterprise Application > Properties for the DEV app (appId 713d6ede..., SP objectId 2052c434-d7fa-4022-a2f8-a1676c187456). Browser tab "Public and confidential client" open. Assignment required? No; Visible to users? No.
* image8 — Enterprise Application > Self-service for the DEV app SP. Allow users to request access? No; Require approval? No.
* image9 — Enterprise Application > Conditional Access for the DEV app SP (objectId 2052c434...). 1 Microsoft-managed policy + 54 user-created CA policies. Visible policy names include: "Block - USR - All Apps - DeviceCode Flow", "Block - USR - All Apps - Guests or External Users - Baseline", "Block - USR - All Apps - Other Client - Specific", "Block - USR - All Apps - Pays Sanctions", "Block - USR - All Apps - User Risk (Report-Only)", "Block - USR - All apps HDI - Specific", "Block - USR - AllApps - Restriction Perimetre", "Block - USR - AllApps - Restriction Pentime Sans Type", "Block - USR - AllApps - SQuantumAPI", "Block - USR - Mobile Apps - Baseline (Report-Only)". "Restriction Perimetre" (perimeter restriction) is the most likely blocker of the AWS-IP event.
* image10 — Enterprise Application > Token encryption for the DEV app SP. "Token encryption is not available for this application." No token encryption configured.
* image11 — Remote-desktop session into a PROD environment; JSON pane shows the PROD app object: id f75b1dc5-6d7b-4a.., appId 92dd40a3-f7c2-... Confirms prod-prod.txt (objectId f75b1dc5-6d7b-4a37-a353-caf805022eaf, appId 92dd40a3-f7c2-42ff-9303-fff5928e195a).
* image12 — Croesus Central login page https://gpd-central.desjardins.com/CentralWebApp/logon.aspx with DevTools open. Buttons: "Se connecter" (username/password) and "Connexion d'entreprise" (enterprise/federated SSO). Footer: Database central_gpd_prod_db_prod, Central build 2025.12.1. JSON pane shows prod app servicePrincipalLockConfiguration isEnabled true, allProperties true, and spa.redirectUris ["https://gpd-central..."].
* image13 — Excel export "SignInLog" (workbook from gpd-central.desjardins.com/SessionId 007bc799-6350-25bf-fbe9-ebbcb7093b63). Header row lists the Entra sign-in columns. Two data rows (see Sign-in log table below). Row 2 TokenProtectionStatusDetails = {"signInSessionStatus":"bound","signInSessionStatusCode":0}; Row 3 = {"signInSessionStatus":"unbound","signInSessionStatusCode":1008}.
* image14 — Same SignInLog, columns A–H. Confirms: Row 2 IsInteractive TRUE / bound(0); Row 3 IsInteractive FALSE / unbound(1008). Both share SessionId 007bc799-6350-25bf-fbe9-ebbcb7093b63.
* image15 — Same SignInLog, columns H–O. Row 2 IPAddress 142.195.80.133 (Desjardins corporate), Row 3 IPAddress 3.97.32.113 (Amazon AWS). Both: AppDisplayName sp-CentralGPD-prod-fed, AppId 92dd40a3-f7c2-42ff-9303-fff5928e195a, ResourceDisplayName Microsoft Graph, ResultType 0 (success). DeviceDetail deviceId 5b4b24f4-4540-46a6-a4cf-4518e81f06c8 / displayName PP5CD3358905 on BOTH rows.
* image16 — Same SignInLog, DeviceDetail + tenant columns. Row 2 DeviceDetail {"deviceId":"5b4b24f4-4540-46a6-a4cf-4518e81f06c8","displayName":"PP5CD3358905","operatingSystem":"Windows10","browser":"Edge 146.0.0","isCompliant":true,"isManaged":true,"trustType":"Azure AD joined"}. Row 3 same deviceId, operatingSystem "Windows", browser "" (empty), isCompliant true, isManaged true, trustType "Azure AD joined". AADTenantId / HomeTenantId 728d20a5-0b44-47dd-9470-20f37cbf2d9a.

## Sign-in log table (the smoking gun — images 13–16)

Both rows: same user session, app sp-CentralGPD-prod-fed (appId 92dd40a3-f7c2-42ff-9303-fff5928e195a = prod-prod), resource Microsoft Graph, tenant 728d20a5-0b44-47dd-9470-20f37cbf2d9a, SessionId 007bc799-6350-25bf-fbe9-ebbcb7093b63, deviceId 5b4b24f4-4540-46a6-a4cf-4518e81f06c8 (PP5CD3358905), ResultType 0 (success).

| Field | Sign-in #1 (interactive) | Sign-in #2 (replay) |
| --- | --- | --- |
| TimeGenerated (UTC) | 3/31/2026 7:24:25.466 PM | 3/31/2026 7:23:35.189 PM |
| CreatedDateTime (UTC) | 3/31/2026 7:22:26.968 PM | 3/31/2026 7:22:28.277 PM |
| IsInteractive | TRUE | FALSE |
| IPAddress | 142.195.80.133 (Desjardins corp) | 3.97.32.113 (Amazon AWS) |
| TokenProtection signInSessionStatus | bound | unbound |
| signInSessionStatusCode | 0 | 1008 |
| browser | Edge 146.0.0 | (empty) |
| operatingSystem | Windows10 | Windows |
| isCompliant | true | true |
| isManaged | true | true |
| trustType | Azure AD joined | Azure AD joined |
| ResultType | 0 (success) | 0 (success) |

A second related Amazon IP, 3.99.119.124, appears in the image1 meeting-chat notes.

## What this adds beyond the .txt manifests

1. Live portal confirmation that the manifests match the running tenants: dev-dev implicit ID-token ON (image2), dev-prod implicit OFF (image4), prod-prod SPA redirect + SP lock (image12), all appIds/objectIds reconciled (images 1,3,4,11).
2. Enterprise-application (service principal) object IDs not present in the application-object exports: DEV app 713d6ede → SP 2052c434-d7fa-4022-a2f8-a1676c187456; dev-prod app e3e358ea → SP 323e25ff-4afb-47c1-aa3e-0d1142515473. Both: Assignment required = No, Visible to users = No (images 5,7).
3. The Conditional Access surface: 54 user-created CA policies in the DEV tenant, including a "Restriction Perimetre" (perimeter) block — the probable blocker of the second event (image9).
4. No token encryption configured (image10).
5. The actual user flow: the Croesus Central login page exposes a "Connexion d'entreprise" (federated SSO) path; prod build 2025.12.1 (image12).
6. THE PREVIOUSLY-MISSING RAW SIGN-IN LOGS (images 13–16) — verbatim Entra sign-in records for both events, including Token Protection status, source IPs, device claims, and resource/app. This is the PROD reference behaviour that the RMS-locked PROD.docx was expected to hold.

## Decisive interpretation

* The second, non-interactive sign-in is a TOKEN REPLAY, now directly evidenced: Token Protection records it as signInSessionStatus "unbound" (code 1008) versus "bound" (0) on the legitimate interactive sign-in, while carrying the SAME deviceId and the original session's "Azure AD joined / isCompliant:true" claims from an AWS IP (3.97.32.113) with no real device context.
* It is NOT a standards-compliant Entra OBO: the resource is Microsoft Graph (User.Read), not a custom backend API audience; the manifests have no confidential-client credential and no exposed API scope. This corroborates DD-01 (the advisory PDF's "OBO" label is imprecise).
* In PROD the replay succeeds (ResultType 0) and is only flagged (unbound) by Token Protection; in non-prod (DEV tenant) Conditional Access blocks it — consistent with the cross-tenant device-compliance gap.
* Security note (answers the meeting-chat question "Unbound is saying token REUSE — is this dangerous?"): a replayed token inheriting "Azure AD joined / compliant" device claims from an external AWS IP can satisfy device-based Conditional Access grants that it should not. Entra Token Protection correctly flags it as unbound; enforcing a Token Protection / token-binding Conditional Access control would deny it. This is a meaningful, concrete risk finding — not merely cosmetic.

## Impact on prior research gaps

* WI-02 (raw Entra sign-in logs) — SATISFIED for the prod-prod scenario by images 13–16 (verbatim TokenProtection, IPs, device claims, resource). Still missing: the verbatim AADSTS failure code + CA policy name for the non-prod DEV-tenant denial.
* WI-03 (Croesus AWS egress IPs) — PARTIALLY SATISFIED: confirmed Amazon IP 3.97.32.113 (and 3.99.119.124 in chat notes). Still want Croesus's full published egress range.
* DR-02 (RMS-locked authoritative docs) — DOWNGRADE: the screenshot doc supplies the prod reference behaviour and the raw sign-in evidence those docs were expected to contain; the OAuth integration report PDF would still add the vendor's intended flow definition.
