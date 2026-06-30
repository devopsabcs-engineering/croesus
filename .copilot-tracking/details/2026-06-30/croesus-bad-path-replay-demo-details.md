<!-- markdownlint-disable-file -->
# Implementation Details: Croesus Bad-Path Replay Demo

## Context Reference

Sources: user request to plan from `task-plan.prompt.md`, .copilot-tracking/research/2026-06-30/croesus-token-replay-bad-path-research.md, .copilot-tracking/research/subagents/2026-06-30/1008-reasoning-rubber-duck.md, .copilot-tracking/research/subagents/2026-06-30/bad-path-implementation-rubber-duck.md, current SPA/API source reads.

This plan implements only Tier 1 from the research: a safe client-only negative-control UX that replays the API-audienced token directly to Microsoft Graph and shows Graph rejecting it with 401. Tier 2, the faithful backend replay lab, is deferred because it intentionally introduces a token-forwarding endpoint and requires tenant/app-registration changes.

## Implementation Phase 1: Add Client-Side Replay Attempt

<!-- parallelizable: false -->

### Step 1.1: Add safe JWT payload decoding

Add a small local helper in `spa/src/api.ts` to decode JWT payload claims without introducing a dependency. Use it only for non-sensitive claim display, and never log or return the raw token.

Files:
* spa/src/api.ts - Add helper near the existing type declarations or before `callApiMe()`

Discrepancy references:
* None. Planning validation confirmed Tier 1 is aligned with the refined research.

Success criteria:
* Helper handles malformed tokens without throwing UI-breaking exceptions
* Helper returns at least `aud`, `scp`, `jti`, `iat`, and `hasCnf` when present
* Raw token is not stored in React state, logs, errors, or response models

Context references:
* .copilot-tracking/research/2026-06-30/croesus-token-replay-bad-path-research.md (Lines 148-173) - What Tier 1 proves and does not prove
* spa/src/api.ts (Lines 1-52) - Existing API and evidence types

Dependencies:
* Existing `getApiToken()` helper remains unchanged

### Step 1.2: Add `callGraphWithApiTokenWrongWay()`

Add a new exported function in `spa/src/api.ts` that reuses the existing API-scoped token acquisition flow, then calls `https://graph.microsoft.com/v1.0/me` with that API token. Capture only safe result metadata.

Recommended return shape:

```ts
export interface ReplayAttemptResult {
  attemptedTarget: string;
  expectedAudience: string;
  tokenAudience?: string;
  tokenScope?: string;
  tokenJti?: string;
  tokenIssuedAt?: string;
  hasCnf: boolean;
  status: number;
  ok: boolean;
  bodyPreview: string;
  interpretation: string;
}
```

Implementation notes:
* Use `getApiToken(instance, account)` to get token A.
* Do not expose the token outside the function.
* Fetch `https://graph.microsoft.com/v1.0/me` directly with `Authorization: Bearer <token A>`.
* Read `res.text()` and truncate the preview to a small bounded length, for example 500 characters.
* Interpret `401` as expected success for the negative control.

Files:
* spa/src/api.ts - Add `ReplayAttemptResult` and `callGraphWithApiTokenWrongWay()` after `callApiMe()` or near related fetch helpers

Discrepancy references:
* None. Tier 2 backend replay is tracked as follow-on work rather than a current discrepancy.

Success criteria:
* Function returns a structured result for 401 without throwing
* Function throws only for unexpected client-side/runtime failures, not for expected Graph 401
* Returned data includes enough evidence to explain wrong audience replay without exposing raw tokens

Context references:
* .copilot-tracking/research/2026-06-30/croesus-token-replay-bad-path-research.md (Lines 231-274) - Tier 1 implementation sketch
* spa/src/api.ts (Lines 56-78) - Existing `callApiMe()` fetch pattern

Dependencies:
* Step 1.1 completion

### Step 1.3: Add replay attempt result UI component

Create a new component to render the negative-control result. The component should visually match the existing evidence cards while making the failure expected and understandable.

Recommended component:
* `spa/src/components/ReplayAttemptPanel.tsx`

Recommended content:
* Title: `Bad path evidence: replay API token to Graph`
* Rows: attempted target, token audience, expected audience, Graph status, `hasCnf`, `jti`, `iat`
* Assertion bullets:
  * Token audience is API, not Graph
  * Graph rejected the replay with 401
  * This is not literal Token Protection 1008; it is the audience-bound negative control

Files:
* spa/src/components/ReplayAttemptPanel.tsx - New component

Discrepancy references:
* None. The UI wording follows the research boundary that Tier 1 does not emit literal 1008.

Success criteria:
* UI clearly labels 401 as the expected safe negative-control outcome
* UI does not say the mock emitted Token Protection 1008
* UI keeps terminology aligned with `EvidencePanel` and README language

Context references:
* spa/src/components/EvidencePanel.tsx (Lines 21-58) - Existing card and row style
* .copilot-tracking/research/subagents/2026-06-30/1008-reasoning-rubber-duck.md (Lines 220-277) - Honest claim boundaries for the mock

Dependencies:
* Step 1.2 result shape finalized

### Step 1.4: Wire button and state in `App.tsx`

Add separate state for the replay attempt result, replay loading, and replay error. Add a handler near `handleCallApi()` that calls `callGraphWithApiTokenWrongWay()` for the first signed-in account.

Implementation notes:
* Keep the existing `Call API` button as the good-path action.
* Add a second authenticated button labeled `Replay API token to Graph (wrong)` or similar.
* Do not clear the good-path evidence when replay runs unless the UX becomes confusing.
* Render `ReplayAttemptPanel` below or near `ContrastPanel` so the wrong/right comparison is visible.
* Keep `loginRequest` scoped only to the API; do not add Graph scopes in Tier 1.

Files:
* spa/src/App.tsx - Import the new function and component, add state/handler/button/rendering

Discrepancy references:
* None. Tier 1 intentionally remains client-only and avoids tenant/config changes.

Success criteria:
* Authenticated user sees both good-path and bad-path buttons
* Good-path OBO panel still renders unchanged
* Bad-path panel renders Graph 401 as an expected negative-control result
* No new Graph consent prompt appears in Tier 1

Context references:
* spa/src/App.tsx (Lines 88-115) - Existing state and `handleCallApi()`
* spa/src/App.tsx (Lines 119-137) - Existing authenticated button row
* spa/src/App.tsx (Lines 144-148) - Existing evidence panel rendering

Dependencies:
* Steps 1.2 and 1.3 completion

### Step 1.5: Validate SPA changes

Run the SPA build and, if available, any focused lint/test command for the SPA.

Validation commands:
* `cd spa; npm run build` - TypeScript compile and Vite production build
* `cd spa; npm audit --omit=dev` - Optional production dependency audit check

Success criteria:
* SPA build succeeds
* TypeScript has no diagnostics in touched files
* Existing good-path behavior is not broken by type changes

Dependencies:
* Steps 1.1-1.4 completion

## Implementation Phase 2: Update Documentation For Tier 1

<!-- parallelizable: true -->

### Step 2.1: Update demo guide negative-control section

Update `docs/obo-demo-guide.md` so Step 4 includes the new interactive Tier 1 UI result in addition to the existing script-based negative tests.

Content requirements:
* Explain that the button replays token A to Graph and expects 401
* State that this proves Graph rejects the API-audienced token
* State that this is not the literal Token Protection 1008 signal
* Keep the existing `scripts/negative-test.sh` guidance intact

Files:
* docs/obo-demo-guide.md - Update Step 4

Discrepancy references:
* None. Documentation updates should preserve the validated 1008 boundary.

Success criteria:
* Documentation names Tier 1 as a negative-control contrast
* Documentation preserves current OBO proof language
* Documentation points readers to the evidence narrative for 1008 boundaries

Context references:
* docs/obo-demo-guide.md (Lines 129-139) - Existing negative tests section
* .copilot-tracking/research/2026-06-30/croesus-token-replay-bad-path-research.md (Lines 460-494) - Recommended implementation order

Dependencies:
* Can run in parallel with Phase 1 after result naming is agreed

### Step 2.2: Update README wrong-versus-right table prose

Update README's demo section to mention the interactive bad-path negative control without changing the executive finding that the real customer logs reported Token Protection 1008.

Content requirements:
* Clarify that the live demo now shows two things:
  * good path: OBO succeeds with distinct audiences
  * safe bad path: API token replayed to Graph fails with 401
* Avoid implying the mock emits Token Protection 1008
* Preserve the broken baseline table unless implementation changes require a row addition

Files:
* README.md - Update the `Wrong versus right` subsection

Discrepancy references:
* None. README updates should preserve the distinction between customer 1008 evidence and mock evidence.

Success criteria:
* README remains consistent with the research caveat
* README gives users a reason to click the new bad-path button

Context references:
* README.md (Lines 70-84) - Existing wrong-versus-right prose and table
* .copilot-tracking/research/subagents/2026-06-30/1008-reasoning-rubber-duck.md (Lines 220-277) - Suggested safer wording

Dependencies:
* Can run in parallel with Phase 1 after UX label is known

### Step 2.3: Validate Markdown documentation

Run markdown or repository documentation validation if available. If no markdown lint script exists, review the changed sections manually and run a narrow grep for over-strong 1008 claims.

Validation commands:
* `rg "reproduce.*1008|emits? Token Protection|literal Token Protection" README.md docs/obo-demo-guide.md` - Ensure wording does not overpromise
* `git diff -- README.md docs/obo-demo-guide.md` - Review documentation-only changes

Success criteria:
* No new documentation claims that the mock emits literal Token Protection 1008
* Markdown formatting remains consistent with existing documents

Dependencies:
* Steps 2.1 and 2.2 completion

## Implementation Phase 3: Add Focused Tests If Practical

<!-- parallelizable: false -->

### Step 3.1: Add unit coverage for JWT claim decoder

If the SPA test framework is already configured, add tests for the JWT decode helper. If no test framework exists, skip adding one and rely on `npm run build` plus manual browser validation.

Test cases:
* Decodes valid payload and returns `aud`
* Handles malformed token without throwing
* Detects `cnf` presence as `hasCnf = true`

Files:
* spa/src/api.ts - Export helper only if tests need it; otherwise keep helper private
* SPA test file if existing test infrastructure is present

Discrepancy references:
* None. Adding tests remains conditional on existing SPA test infrastructure.

Success criteria:
* Tests added only if they fit existing project conventions
* No new dependencies introduced solely for Tier 1 tests

Context references:
* .copilot-tracking/research/subagents/2026-06-30/bad-path-implementation-rubber-duck.md (Lines 226-251) - Test gate suggestions and safety controls

Dependencies:
* Phase 1 helper design complete

## Implementation Phase 4: Final Validation

<!-- parallelizable: false -->

### Step 4.1: Run full focused validation

Run the validation commands that cover all modified production files.

Validation commands:
* `cd spa; npm run build`
* `dotnet test api/Tests` - Only if API code or shared repo validation requires it; Tier 1 should not touch API code
* `git diff --check`

Success criteria:
* SPA build succeeds
* No whitespace errors
* API tests remain unchanged or pass if run

Dependencies:
* Phases 1-3 complete

### Step 4.2: Exercise the deployed or local browser flow

After deploy, sign in and click both buttons.

Manual validation:
* `Call API` renders the existing OBO evidence with leg 1 API audience and leg 2 Graph audience
* `Replay API token to Graph (wrong)` renders a 401 result from Graph
* UI states the 401 is expected and does not present it as an app failure
* Browser console has no raw token output

Dependencies:
* Build/deploy completed outside this plan or local Vite dev server configured

### Step 4.3: Record implementation evidence

Update the existing changes log with the Tier 1 result and validation commands.

Files:
* .copilot-tracking/changes/2026-06-30/croesus-bad-path-replay-demo-changes.md - Create or update during implementation

Success criteria:
* Changes log states Tier 1 implemented and Tier 2 deferred
* Validation results are recorded
* No raw tokens or secrets are recorded

Dependencies:
* Steps 4.1 and 4.2 complete

## Deferred Phase: Tier 2 Replay Lab

<!-- parallelizable: false -->

Tier 2 is explicitly out of scope for the first implementation pass. Create a separate plan before implementation.

Deferred requirements:
* Confirm tenant willingness to grant SPA Graph `User.Read` delegated consent
* Add `VITE_ENABLE_REPLAY_DEMO=false` and `Demo:EnableReplay=false` gates
* Add API-authenticated, fixed-target replay endpoint
* Add token redaction tests
* Route deploy through a manually approved replay-lab environment

Reason for deferral:
* Tier 2 intentionally introduces a token-forwarding vulnerability and requires tenant/app-registration changes. The research recommends implementing Tier 1 first because it provides useful teaching value without new security exposure.

## Dependencies

* Existing SPA dependencies from `spa/package.json`
* Existing MSAL token acquisition via `getApiToken()`
* Browser network access to Microsoft Graph
* No new tenant permission required for Tier 1

## Success Criteria

* Tier 1 bad-path button attempts to use token A against Graph and shows expected Graph 401
* Good-path OBO button still succeeds and renders existing evidence
* Documentation explains Tier 1 as a negative-control contrast, not literal Token Protection 1008 reproduction
* No raw tokens are logged, stored, or returned in UI state
* Tier 2 replay lab remains deferred and gated in planning artifacts
