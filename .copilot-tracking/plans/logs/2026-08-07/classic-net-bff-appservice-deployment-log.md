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

## Suggested Follow-On Work

* WI-01: Run the validation-only workflow manually after merge to confirm the hosted runner toolchain. (medium)
  * Source: Phase 4, workflow validation
  * Dependency: Repository workflow availability
* WI-02: Configure required reviewers and Azure public variables on the `poc-demo` GitHub environment. (high)
  * Source: Phase 4, deployment safety boundary
  * Dependency: Repository administrator access

## User Decisions

* ID-01: B1 App Service deployment design - approved
  * Rationale: Use one shared Windows B1 plan, two web apps, one confidential comparison registration, GitHub OIDC, and an explicit `Poc` credential policy.