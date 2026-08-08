<!-- markdownlint-disable-file -->
# Planning Log: Classic BFF App Service Deployment

**Related Plan**: classic-net-bff-appservice-deployment-plan.instructions.md

## Discrepancy Log

Gaps and deviations identified during implementation.

### Unaddressed Research Items

* (none)

### Implementation Deviations

* DD-01: The first modern self-contained publish required a runtime-specific restore.
  * Plan specifies: Validate the `win-x64` self-contained publish.
  * Implementation differs: The initial `--no-restore` attempt failed, then a normal publish restored RID assets and succeeded.
  * Rationale: The project had not previously restored assets for `win-x64`.
* DD-02: The optional `.bicepparam` entry point uses an environment-only secret binding.
  * Plan specifies: Add a non-secret parameter example if useful.
  * Implementation differs: The parameter file requires `CROESUS_DEPLOYMENT_CLIENT_SECRET` with no fallback; the workflow supplies parameters directly.
  * Rationale: This keeps the parameter file compilable without committing credential material.
* DD-03: Entra teardown uses deterministic ownership tags instead of a cross-run state artifact.
  * Plan specifies: Preserve ownership state for guarded teardown without creating cross-run trust problems.
  * Implementation differs: Teardown rediscovers an exact display-name match and verifies the workflow ownership tag.
  * Rationale: This avoids publishing mutable state while preventing the workflow from claiming unrelated registrations.
* DD-04: The deployment identity received Microsoft Graph `Application.ReadWrite.All` with tenant-admin consent.
  * Plan specifies: Use GitHub OIDC for non-interactive Azure and Graph deployment.
  * Implementation differs: Azure resource-group Contributor remained narrowly scoped, while Graph directory access was granted independently on the bootstrap application.
  * Rationale: Azure RBAC does not authorize Microsoft Graph application creation or updates.
* DD-05: Graph requests use file-backed JSON and bounded transient retries.
  * Plan specifies: Converge the Entra application and rotate its short-lived credential non-interactively.
  * Implementation differs: Request bodies are passed through temporary UTF-8 files; exact replication and credential-concurrency failures retry up to four attempts.
  * Rationale: Windows native argument handling corrupted inline JSON, and Graph exposed eventual-consistency boundaries during live deployment.
* DD-06: Live App Service diagnostics required two runtime configuration corrections.
  * Plan specifies: Publish modern self-contained for `win-x64` and deploy the legacy `net452` proof.
  * Implementation differs: The modern app explicitly uses a 64-bit worker, and legacy web.config redirects Newtonsoft.Json to 13.0.0.0.
  * Rationale: The first package deployment exposed a modern host bitness mismatch and a legacy assembly binding failure.
* DD-07: Deployment smoke tests validate protected routes and Entra challenge parameters instead of application roots alone.
  * Plan specifies: Verify both deployed applications are testable.
  * Implementation differs: Readiness checks assert protected endpoint behavior and exact login host, client ID, and callback URI with bounded startup retries.
  * Rationale: The legacy root is intentionally anonymous and returned 200 even while its authentication challenge was failing.
* DD-08: Interactive sign-in reopened deployment implementation after challenge checks passed.
  * Plan specifies: Verify both applications through protected-route Entra redirects.
  * Implementation differs: Phase 7 now covers IIS callback query capacity and legacy anonymous API response semantics.
  * Rationale: A 2,100-character callback probe reproduced the browser's generic IIS 404, and legacy Katana rewrote the session API's explicit 401 into a cross-origin OIDC redirect.
* DD-09: Callback verification uses bounded synthetic queries before a user-driven authorization-code retry.
  * Plan specifies: Complete interactive sign-in verification after deployment.
  * Implementation differs: Hosted probes verify IIS-to-application routing at 2,100 and 6,000 characters; credential entry remains in the user's browser.
  * Rationale: Authentication credentials and browser session material must not pass through automation or chat tools.
* DD-10: Real authorization callbacks exposed a second legacy query limit and missing modern failure telemetry.
  * Plan specifies: An 8192-character IIS request-filtering limit admits code-flow callbacks.
  * Implementation differs: Phase 8 also aligns classic ASP.NET `httpRuntime` and adds bounded modern failure classification.
  * Rationale: The legacy event log showed `maxQueryStringLength` rejection before OWIN, while modern deliberately hid its remote-failure category.
* DD-11: Token-handler claim projection required host-specific tenant metadata handling.
  * Plan specifies: Both hosts enforce strict tenant-to-issuer binding and the configured tenant allowlist.
  * Implementation differs: Legacy accepts both `tid` and Katana's mapped Microsoft tenant-id claim URI; modern binds the tenant claim to `TokenValidatedContext.SecurityToken.Issuer` instead of a principal `iss` claim.
  * Rationale: Real callbacks reached token validation, where Katana can map the tenant claim and ASP.NET Core does not guarantee that the validated token issuer is copied into the principal.
* DD-12: The latest interactive callback required host-specific completion boundaries.
  * Plan specifies: Complete interactive authorization-code redemption after the bounded callback diagnostics identify the remaining failure.
  * Implementation differs: Modern session projection now accepts the validated mapped tenant claim, and legacy wraps the full downstream OWIN pipeline before cookie and OIDC middleware.
  * Rationale: Modern sign-in established a session but returned a null tenant ID, while the 2026-08-08 14:46:48 legacy callback bypassed the OIDC `AuthenticationFailed` notification and failed during `OwinHttpModule` `EndRequest`.
* DD-13: Legacy SystemWeb hosting now relies only on Katana pre-application startup.
  * Plan specifies: Complete the legacy authorization-code callback through the configured OWIN pipeline.
  * Implementation differs: The explicit `web.config` registration of `OwinHttpModule` was removed and a static regression prevents its return.
  * Rationale: The safe outer boundary classified the live failure as `InvalidOperationException` with no protocol code, and Katana 4.2.3 already registers the same module automatically before application startup.
* DD-14: Legacy cookie teardown now emits a bounded phase diagnostic.
  * Plan specifies: Complete the legacy authorization-code callback after host registration is corrected.
  * Implementation differs: The Katana cookie provider records only component, enum phase, exception type, HRESULT, and recognized protocol code while preserving rethrow behavior.
  * Rationale: The post-correction callback changed to `HttpException` with no protocol code and IIS `404.0`; the phase marker distinguishes cookie issuance from later host finalization without exposing authentication material.
* DD-15: Legacy session cookies use Katana's header-based cookie manager.
  * Plan specifies: Issue a protected server-side session cookie after authorization-code redemption.
  * Implementation differs: The application explicitly selects `Microsoft.Owin.Infrastructure.CookieManager` instead of `SystemWebCookieManager` while retaining every cookie security and lifetime setting.
  * Rationale: The manager preserves the cookie policy and avoids `HttpResponse.AppendCookie`, but the live result from commit `450a1c8` proved that manager substitution alone does not permit writes after response commitment.
* DD-16: Legacy OIDC and cookie middleware now share the IIS authentication stage.
  * Plan specifies: Complete the legacy authorization-code callback through the configured OWIN pipeline.
  * Implementation differs: A post-OIDC `PipelineStage.Authenticate` marker groups OIDC with cookie middleware in the `AuthenticateRequest` segment while preserving cookie-before-OIDC registration order.
  * Rationale: Katana's cookie extension inserts an authentication marker immediately, while unmarked OIDC otherwise runs at `PreRequestHandlerExecute`; the split caused the earlier cookie segment to apply the queued sign-in grant after the callback response was committed.

## Suggested Follow-On Work

* WI-01: Run the validation-only workflow manually after merge to confirm the hosted runner toolchain. (completed)
  * Source: Phase 4, workflow validation
  * Dependency: Repository workflow availability
* WI-02: Configure required reviewers and Azure public variables on the `poc-demo` GitHub environment. (completed)
  * Source: Phase 4, deployment safety boundary
  * Dependency: Repository administrator access
* WI-03: Correct the validation workflow's false-green Azure prerequisite handling. (completed)
  * Source: Hosted run 31215402276
  * Dependency: None
* WI-04: Retry interactive sign-in to the corrected legacy app and verify authorization-code redemption and session establishment. (medium)
  * Source: Phase 8 cookie-grant and integrated-pipeline diagnostics
  * Dependency: Deployment of the post-OIDC authentication stage marker and an authorized tenant user in a fresh interactive browser session

## User Decisions

* ID-01: B1 App Service deployment design - approved
  * Rationale: Use one shared Windows B1 plan, two web apps, one confidential comparison registration, GitHub OIDC, and an explicit `Poc` credential policy.