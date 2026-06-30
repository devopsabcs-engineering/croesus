<!-- markdownlint-disable-file -->
# Release Changes: Croesus Bad-Path Replay Demo

**Related Plan**: croesus-bad-path-replay-demo-plan.instructions.md
**Implementation Date**: 2026-06-30

## Summary

Add a safe, client-only Tier 1 bad-path replay demonstration to the Croesus SPA: a button that reuses the existing API-audienced token against Microsoft Graph and renders the expected 401 audience rejection. Tier 2 backend replay lab remains deferred.

## Changes

### Added

* spa/src/components/ReplayAttemptPanel.tsx - Adds the bad-path negative-control evidence panel for the Graph replay attempt, including audience, status, and claim metadata without exposing raw tokens.

### Modified

* spa/src/api.ts - Adds safe JWT payload claim decoding and `callGraphWithApiTokenWrongWay()` to replay the existing API-audienced token to Microsoft Graph and interpret the expected 401 response.
* spa/src/App.tsx - Adds replay attempt state, handler, authenticated bad-path button, error display, and `ReplayAttemptPanel` rendering while preserving the existing OBO evidence flow.
* docs/obo-demo-guide.md - Documents the interactive Tier 1 negative-control button, expected Graph 401 result, and Token Protection 1008 boundary while preserving script-based negative-test guidance.
* README.md - Updates the wrong-versus-right explanation to describe both the successful OBO path and the safe bad-path Graph 401 rejection without claiming the mock emits Token Protection 1008.

### Removed

## Additional or Deviating Changes

* Phase 1 validation completed with `npm run build` from the SPA project.
  * The first command attempt reported a harmless path issue because the subagent terminal was already in `spa`; rerunning the build from the active SPA directory succeeded.
* Phase 2 documentation wording validation completed with an equivalent PowerShell search because `rg` was unavailable to the phase subagent shell.
  * The only matched 1008 wording was the intentional disclaimer that the UI control is not the literal Token Protection 1008 signal.
* Phase 3 test addition was skipped because the SPA has no existing test framework or test files.
  * No new test dependency was introduced solely for the Tier 1 helper; coverage relies on TypeScript build validation and manual browser flow validation.
* Phase 4 focused validation completed with `npm run build`, touched-file diagnostics, wording search, and `git diff --check`.
  * Manual browser validation remains pending because the sign-in flow requires an interactive tenant session: click `Call API`, then `Replay API token to Graph (wrong)`, and confirm the replay panel reports Graph `401` as expected.

## Release Summary

Implemented the Tier 1 bad-path replay demo as a safe SPA-only negative control. The change adds one new React component, updates two SPA source files, and updates two documentation files. No API, infrastructure, CORS, app registration, consent, or Graph-scope configuration changed. Tier 2 backend replay remains deferred behind a separate plan because it would intentionally introduce a token-forwarding endpoint and tenant permission changes.
