# Seedarr Parity & Modernization Migration Plan
*Derived from Leecharr v2.0.x synchronization baseline (Sep 25, 2026 – Oct 1, 2026)*

---

## 1. Executive Summary
Between September 25, 2026 and October 1, 2026, Leecharr underwent major quality, security, client compatibility, and documentation upgrades (achieving 100% SonarCloud green quality gate, 0 open issues, 85%+ coverage, and official client interoperability). This document defines the actionable migration items brought to full parity in Seedarr.

---

## 2. Workpackages & Actionable Tasks

### Workpackage 1: Copyright & Header Cleanup
- [x] **1.1** Replace all occurrences of `PlaceholderCompany` with `FeedItOut` across file headers in `src/`.

### Workpackage 2: Docker Hub README & Badge Alignment
- [x] **2.1** Refactor `DOCKER_HUB.md` badge list to use borderless HTML table rows (`<table><tr><td>...</td></tr></table>`) to bypass Docker Hub's global `img { display: block; }` CSS reset.
- [x] **2.2** Verify screenshot banner (`logo/ss.png`) path compatibility and raw GitHub URL replacement in CI.

### Workpackage 3: Transmission Remote Protocol Interoperability
- [x] **3.1** Implement `TransmissionRpcInputFormatter` in `Seedarr.Api.V1/Transmission/` to accept `application/x-www-form-urlencoded` payloads containing JSON and reconstruct bodies split on `&`.
- [x] **3.2** Register `TransmissionRpcInputFormatter` at index 0 of `InputFormatters` in `NzbDrone.Host/Startup.cs`.
- [x] **3.3** Add path normalization middleware in `NzbDrone.Host/Startup.cs` mapping `/transmission/rpc/` to `/transmission/rpc` to eliminate routing `AmbiguousMatchException` on official `transmission-remote` CLI invocations.

### Workpackage 4: Security & Vulnerability Hardening
- [x] **4.1** Ensure cryptographic random generation in frontend (`crypto.getRandomValues`) for UUID/token creation via `src/utils/random.ts`.
- [x] **4.2** Add ReDoS protection in Kestrel startup: `AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(2))` in `Startup.cs`.
- [x] **4.3** Update `serialize-javascript` dependency override to `>= 7.1.2` (CVE-2026-97711).

### Workpackage 5: Quality Gate & CI/CD Pipelines
- [x] **5.1** Add `.sonarcloud.properties` configured with exclusions for i18n locales (`**/locales/**`) to prevent false-positive structural duplication.
- [x] **5.2** Synchronize CI workflows in `.github/workflows/` (add `sonarcloud.yml` with coverage and Roslyn decoupling).

### Workpackage 6: Integration Testing & Verification
- [x] **6.1** Verify full frontend test suite (`npm test`) passes with 99 passing tests including random utilities.
- [x] **6.2** Verify `dotnet build` compiles cleanly with zero warnings/errors across all projects.
- [x] **6.3** Verify i18n catalogue integrity using `node src/Seedarr.Frontend/scripts/lint-i18n.js` (100% key parity across all 20 locales).
