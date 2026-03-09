# Release Checklist Steam Demo

Status: Draft  
Owner: Production/Release  
Scope: Steam Demo candidate (Unity client + leaderboard backend)

## 1. Release Metadata

- Date:
- Commit SHA:
- Build profile: Demo
- Build artifact:
- Tested by:
- Result: PASS / FAIL

## 2. Technical Checklist

- [ ] `main` is up to date and release commit is tagged.
- [ ] CI workflow `Steam Demo Release Gate` passed.
- [ ] Demo build artifact exists and is downloadable from CI artifacts.
- [ ] Smoke artifact exists (`build_profile_smoke.txt`) and contains a valid marker.
- [ ] Gate `no devtools in demo` passed.
- [ ] Gate `telemetryMode=Off` passed.
- [ ] Perf budget gate passed (`perf_budget_gate_result.json`).
- [ ] Leaderboard backend on VPS is `Up/Healthy` (`db`, `api`, proxy/duckdns stack).
- [ ] Version lock between client and leaderboard backend is aligned (`VERSION_LOCK`).

## 3. Gameplay QA Checklist

- [ ] Core flow works: `MainMenu -> Loading -> Game -> GameOver -> Restart` x10.
- [ ] No soft-locks, crashes, or persistent input/audio loss in loop.
- [ ] Leaderboard submit on legit run returns accepted state and appears in ranking.
- [ ] Suspicious run is flagged (`manual_review` or `rejected`) and not public.
- [ ] Offline degradation works (no crash, retry/fallback UI present).
- [ ] Settings persistence works (resolution/window mode/vsync/fps cap/quality).
- [ ] Steam bootstrap behavior validated: with Steam and without Steam fallback.

## 4. Store and Packaging Checklist

- [ ] Steam branch target confirmed (demo branch/default branch).
- [ ] Build number/version string updated.
- [ ] Store capsule/screenshots/trailer metadata verified for demo build.
- [ ] Changelog snippet prepared for this release.

## 5. Legal and Policy Checklist

- [ ] `Docs/PRIVACY.md` is present and matches real telemetry behavior.
- [ ] `Docs/EULA.md` is present and linked from menu.
- [ ] `Docs/THIRD_PARTY_LICENSES.md` is present and up to date.
- [ ] Hosted legal URLs return 2xx:
  - `https://<host>/legal/privacy-policy.html`
  - `https://<host>/legal/eula.html`
  - `https://<host>/legal/third-party-licenses.html`
- [ ] HTTP requests to legal URLs redirect to HTTPS (`Tools/leaderboard/legal_links_check.ps1`).
- [ ] In-game Privacy/EULA/Licenses buttons open hosted URLs correctly in demo build.

## 6. Deployment and Verification Checklist

- [ ] VPS services restarted with latest code/config (`docker compose up -d --build`).
- [ ] API smoke tests passed (`integration_api_test.ps1`, `security_smoke_test.ps1`, `smoke_test.ps1`).
- [ ] Admin review flow validated for one flagged run (`accept` or `reject`).
- [ ] No auth failures, restart loops, or repeated 5xx in service logs.

## 7. Rollback Procedure

Use rollback when release gate fails after deployment or critical user-facing regression is confirmed.

1. Freeze rollout and stop further deploys.
2. Checkout previous known-good commit/tag on server.
3. Rebuild/restart stack:
   - `docker compose down`
   - `docker compose up -d --build`
4. Verify health (`docker compose ps`, API/DB logs).
5. Re-run smoke tests against production URL.
6. Confirm leaderboard submissions and reads are healthy.
7. Announce rollback with reason and impacted time window.

## 8. Hotfix Checklist

Use hotfix flow when rollback is not sufficient or issue exists in latest stable baseline.

1. Create hotfix branch from latest stable tag.
2. Implement minimal fix (no scope creep).
3. Run targeted local validation and CI `Steam Demo Release Gate`.
4. Create release notes with root cause + fix summary.
5. Deploy hotfix to VPS/Steam demo branch.
6. Run full smoke and leaderboard checks after deploy.
7. Merge hotfix back to `main`.
8. Document incident, fix, and preventive actions.

## 9. Final Sign-Off

- [ ] Release owner sign-off:
- [ ] QA sign-off:
- [ ] Engineering sign-off:
- [ ] Timestamp UTC:
