# Changelog

All notable changes to **Seedarr** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [v1.3.38](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.38) - 2026-09-10

### 🐛 Bug Fixes
- fix(security): enhance API key reveal, copy, regeneration, and topbar hover behavior

---

## [v1.3.37](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.37) - 2026-09-10

### ✨ Features
- feat(frontend): implement quick controls toolbar and collapsible settings drawer
- feat(ui): implement dual-tier collapsible sidebars for main nav and filter panel
- feat(docs): implement interactive OpenAPI v3 and Swagger UI documentation
- feat(i18n): implement multi-language and internationalization framework (#120)
- feat(media): implement media metadata enrichment engine and artwork cache
- feat(api): implement download client compatibility endpoints for qBittorrent, Deluge, and Transmission

### 🐛 Bug Fixes
- fix(integration-test): use dynamic port for tracker server and handle port rebind
- fix(core): optimize test execution speed and resolve all unit test hangs
- fix(ci): resolve integration tests swagger action binding and full frontend localization
- fix(ci): resolve migration duplicate index, client profile startup, and transport tests
- fix(ui): realign navigation hierarchy between torrents and activity
- fix(frontend): align topbar actions order and add getting started launcher
- fix(simulation): wire simulation cluster into SeedingEngine and SpeedPolicy
- fix(transport): implement functional uTP transport layer with TCP fallback
- fix(trackers): wire real tracker protocol execution into announce flow
- fix(hygiene): resolve async/clock/random hygiene and eliminate sync-over-async
- fix(seeding): split SeedingEngine and batch database updates in tick loop
- fix(dht): connect dht peer learning into discovery pipeline and add announce loop
- fix(torrent): enforce entity state invariants and lifecycle transitions
- fix(torrents): extract ITorrentImportService from TorrentController

### 🔧 Maintenance & Improvements
- style: format frontend files with prettier

---

## [v1.3.36](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.36) - 2026-09-09

### 🐛 Bug Fixes
- fix(torrents): persist parsed TorrentFile entities in AddTorrentCommandExecutor and WatchFolderService
- fix(arrwebhook): safely handle connection URLs with trailing slashes
- fix(backup): support PostgreSQL database backups and handle pre-existing SQLite staging files
- fix(arrintegration): handle ArrType names case-insensitively in CreateProvider
- fix(indexers): support torznab leechers attribute in TorznabIndexer
- fix(seeding): respect ForceStart flag in SelectDownloadStoppedTorrents
- fix(environmentinfo): return host os version in OsInfo.Version
- fix(trackerserver): parse binary info_hash correctly and normalize hex keys
- fix(dht): calculate closest nodes to target ID in find_node query handler
- fix(configuration): synchronize ConfigFileProvider dictionary mutations and reads
- fix(peers): eliminate 100% CPU busy-spin on peer TCP disconnect EOF in PeerServer
- fix(jobs): prevent background scheduler starvation on unregistered task types
- fix(environment): use ApplicationData for default AppData path on Linux/macOS
- fix(validation): prevent SSRF filter bypasses for 0.0.0.0, IPv6 Any, IPv4-mapped IPv6, and ULA

---

## [v1.3.35](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.35) - 2026-09-09

### 🔧 Maintenance & Improvements
- ci: remove undeclared VALIDATE_MARKDOWN_PRETTIER from workflow
- docs(readme): prettier table formatting
- ci: disable VALIDATE_MARKDOWN_PRETTIER in workflow

---

## [v1.3.34](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.34) - 2026-09-09

### 🔧 Maintenance & Improvements
- docs(readme): streamline README layout to match Leecharr and reposition UI screenshot
- docs(readme): add application screenshot before features section

---

## [v1.3.33](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.33) - 2026-09-04

### 🔧 Maintenance & Improvements
- ci: restore workflow inputs lost in bot wipe (5bad871-equivalent)
- Add or update GitHub Actions workflows
- Add or update GitHub Actions workflows
- Add or update GitHub Actions workflows

---

## [v1.3.32](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.32) - 2026-09-02

### ✨ Features
- feat(frontend): add getting started guide carousel and interactive setup wizard
- feat(trackers): execute real per-tracker announces with detailed event logging on manual announce
- feat(api): return JSON payload with torrent and tracker details from announce endpoint
- feat(trackers): add tracker metrics telemetry, historical snapshots, and analytics dashboard
- feat(trackerboost): implement scrape-verified TrackerBoost with live harvesting and cross-matrix explorer
- feat(system): overhaul look and feel across all 7 System pages with elevated cards, page-headers, and status pills
- feat(settings): upgrade all Settings tabs and Tags page with SectionCard layouts, unified SaveBar toolbar, and elevated cards
- feat(settings): upgrade Settings pages with standard page-header, elevated SaveBar toolbar card, and section cards for General settings
- feat(statistics): fix SpeedGraph SVG stretching with proportional viewBox and gradient area fills, elevate gamification and tracker buffer cards
- feat(schedule): elevate Speed Schedule with multi-column rate limit stat cards, matrix calendar, and rich schedule table
- feat(tracker): nest Inbuilt and Boost submenus under Tracker navigation, elevate stat cards and announce URLs, and integrate Download++ into Boost
- feat(activity): upgrade metrics into responsive 420px elevated cards with SVG gradients, clean typography, and live heartbeat indicator
- feat(activity): add rich poster art, [Posters | Table] view switcher, clean page-header, and Arr links to Download Client torrents page
- feat(torrents): add dedicated /torrents/add page and expand AddTorrentModal to wide 1020px responsive container with rich indexer search table
- feat(ui): add poster art to activity grid & tracker server, sonarr-style soft buttons, cards drop shadows, and form control consistency
- feat(library): auto-reconcile existing torrents into history and enrich metadata via Sonarr/Radarr/Lidarr title lookups
- feat(download++): implement Download++ agent with tracker radar, Prowlarr harvesting, per-torrent detection matrix, and swarm booster
- feat(ui): add seeding achievements hall of fame, gamification milestones, HNR minimum seed time protection, and tracker buffer estimator
- feat(ui): implement usability & integration enhancements across dashboard, torrents, peer map, tracker server, and system status
- feat(ui): add Arr deep-links, external metadata links, and clickable actor search across UI
- feat(history): enrich download history with Arr posters, actors, and media grid view
- feat(indexers): add test connection button and detailed feedback banner to indexer modal
- feat(downloadclient): add dynamic agent submenus and client torrents view with library import
- feat(downloadclient): add modal test button with detailed connection feedback
- feat: Add configurable WebhookHost to Arr Connections
- feat: finite global speed limits with sane defaults
- feat: multi-file uploads, per-torrent event logs, webhook secret, toolbar rework
- feat: add authenticated webhook E2E test with two-stack CI integration
- feat: replace custom events with typed ModelEvent, wire SignalR
- feat(network): event-driven external IP refresh via NetworkAddressChanged
- feat(network): refresh external IP every hour via background service
- feat(health): add setup guidance checks for connections, clients, indexers
- feat(ui): wire up heart (donate) and user (actions menu) topbar buttons
- feat(peers): add outgoing peer connections and network diagnostics
- feat(network): add diagnostics endpoint and UI page
- feat(ui): add 4 new pages, bulk actions, file tree, dashboard widgets, unsaved guard
- feat(coverage): add dotnet-coverage instrumentation to container
- feat: migrate all bash smoke tests to NzbDrone.Automation.Test
- feat: add Selenium ChromeDriver automation test project
- feat: add peer connection time-series with D3 mindmap and config API refactor
- feat: add seedarr.net website references throughout codebase
- feat: add make integration target for CI integration test job
- feat: add webhook receiver, indexer management, and integration test suite
- feat: comprehensive UI overhaul, seeding engine fixes, CI lint fixes
- feat: expand settings to 121 config properties across 10 sections

### 🐛 Bug Fixes
- fix(ci): restore valid dispatch inputs in main workflow
- fix(ci,test): configure 4-thread test runner and resolve frontend type errors
- fix(automation): disable getting started modal auto-pop in webdriver test runners and add e2e test
- fix(ui): ensure download history and tracker server grids handle large collections with full vertical scroll and box sizing
- fix(ui): prevent grid poster compression and ensure full card height with vertical scrolling
- fix(trackerboost): parse host correctly in IsValidPublicTrackerUrl to avoid falsely excluding subdomains with 127.0.0.1
- fix(network): update primary external IP endpoint path to /ip/
- fix(network): update primary external IP endpoint to www.seedarr.net
- fix(ui): fix poster rectangular 2:3 aspect ratio and prevent grid cards from stretching to viewport height
- fix(ui): relocate Tracker Boost tab navigation directly above changing content with clear button styling
- fix(ui): apply content-area wrapper to dashboard and history pages for consistent horizontal spacing
- fix(db): add TotalVerifiedTorrents migration 029 and handle update api rate limits
- fix(ui): update menu and headers to Tracker Boost
- fix(styles): remove duplicate css selector and add root stylelintrc
- fix(styles): modernize css color syntax and adjust stylelint rules
- fix(trackers): fix torrent tracker parsing and persist trackers on sync and webhooks
- fix(trackerboost): adjust indentation in TrackerBoostService for editorconfig
- fix(peermap): aggregate active torrent swarms and tracker peers into graph endpoint and elevate Peer Map visualization container
- fix(history): align Historical Downloads and Tracker headers, view-toggles, and poster grid width to reference standard
- fix(dashboard): resolve empty health alerts void, elevate stat cards with drop shadows, and make speed graph responsive
- fix(ci): remove invalid workflow input
- fix(test): make torrentRepository optional in ArrMetadataEnricherService and add test cases
- fix(download++): connect real download agents (qBit, Transmission, Deluge) and fix editorconfig indentation
- fix(layout): constrain torrent table scrolling and pin selected detail panel in viewport
- fix(frontend): rename to Indexer Search, add auto-debounce typing and empty state guidance
- fix(navigation): ensure top-level Torrents navigates to /torrents and update navigation tests
- fix(sync): refine fallback condition when importing download client items
- fix(sync): allow importing torrents from client items when .torrent bytes unavailable & add enable toggle to indexers
- fix(stylecop): satisfy SA1513 blank line rule in DownloadClientController
- fix(downloadclient): ensure default Implementation and ConfigContract on download client creation
- fix(arr): compare existing API key safely when empty
- fix(arr): update existing webhook notification in place and use loopback for local arr connections
- fix(arr): support full URL in WebhookHost and ignore hex container IDs in BindAddress
- fix(ci): fix shellcheck and shfmt formatting in checks.sh and disable bash linters
- fix(ci): add checks.sh and disable failing linters for super-linter
- fix(arr): isolate connection testing, add modal test button with error feedback, and enhance sync reporting
- fix: Dynamically populate AnnounceInterval and NextUpdate columns in torrent list
- fix: guard distribution manager caches and align redistribution modes (Closes #92) (#94)
- fix: attach X-Api-Key in automation webhook posts
- fix: supply real instance api key to automation tests via env
- fix: read instance api key from test data config.xml
- fix: fetch instance api key via config endpoint in webhook tests
- fix: webhook integration tests authenticate with instance API key
- fix: drop unused using
- fix: enforce X-Api-Key on webhook receiver
- fix: unblock integration suites
- fix: provide repo stylelint config to override broken CI default
- fix: resolve linter violations flagged by CI
- fix: rename idEl to avoid codespell false positive
- fix: use Dns.GetHostName() instead of localhost for wildcard bind address
- fix: stop auto-generating WebhookSecret on connection create
- fix: allow unauthenticated access to webhook endpoint and fix notification payload
- fix: allow unauthenticated webhooks when connection has no secret set
- fix: serve .torrent files from /fixtures by enabling unknown MIME types
- fix: correct TagIds and TrackerEntry.Downloaded data model types
- fix: skip IPv6 peers in PeerExchange CompactPeers to prevent crash
- fix: eliminate thread-safety races in TrafficPatternSimulator and MultiTrackerManager
- fix: redact embedded credentials from tracker URLs before logging
- fix: bake test fixtures into image to fix fixture 404 in CI
- fix: resolve automation test failures for webhooks and fixture serving
- fix: resolve flaky DHT test and webhook integration test setup
- fix: update webhook integration tests to pass X-Seedarr-Secret header
- fix: resolve remaining lint failures
- fix: resolve all lint failures — whitespace, codespell, prettier, typescript-es
- fix: apply wave-2 quality fixes across 80 files, all tests green
- fix: address 15 critical and major quality review findings
- fix: address quality review findings across frontend
- fix: plug protocol crashes, LPD wiring, type safety, infrastructure smells
- fix: address code smell audit — bugs, performance, safety
- fix: plug auth bypass and cancellation bugs
- fix(frontend): use apiClient for system restart/shutdown
- fix: wire command system, add handlers, fix API null crashes, show errors in UI
- fix(container): install coverage tools in SDK stage, not runtime stage
- fix(ci): disable VALIDATE_TSX (super-linter v8 ESLint 9 incompatible with project)
- fix(logging): use correct NLog v6 ArchiveSuffixFormat syntax
- fix(ci): remove deprecated VALIDATE_TYPESCRIPT_STANDARD (removed upstream)
- fix(ci): remove VALIDATE_TRIVY (not supported by dispatch)
- fix(ci): disable new super-linter v8 validators (biome, zizmor, trivy)
- fix: revert to write-all (CHECKOV disabled upstream), fix update cache test
- fix(ci): add .npmrc with legacy-peer-deps for ESLint 10 compatibility
- fix(ci): format workflow YAML for prettier
- fix(ci): scope permissions to pass CHECKOV CKV2_GHA_1
- fix(lint): use modern CSS color notation for stylelint
- fix(lint): expand keyframe selectors to multi-line for stylelint
- fix(ui): prevent sidebar logo resize and fix formatBytes for sub-byte values
- fix(updates): don't cache failed GitHub API responses for 6 hours
- fix(container): add --legacy-peer-deps for ESLint 10 peer conflict
- fix(settings): replace useBlocker with beforeunload for unsaved guard
- fix(seeding): use per-torrent threshold over global default
- fix(deps): resolve all dependabot security alerts
- fix(webhook): add periodic re-registration and surface failures
- fix: unblock 5 of 7 skipped integration tests
- fix: add milliseconds to backup filename to prevent collision within same second
- fix: correct integration test failures from 15 failing CI tests
- fix: extract CreateTestConnectionAsync helper to eliminate jscpd clone in ArrConnectionCrudTests
- fix: use seedarr.local internal hostname for Radarr release/push downloadUrl
- fix: resolve automation test failures and coverlet lock conflict
- fix: resolve jscpd and editorconfig lint violations in Automation.Test
- fix: read Prowlarr API key from container to avoid masked key issue
- fix: correct shfmt formatting for Prowlarr apps curl pipe
- fix: remove masked API key from Prowlarr health and apps checks
- fix: allow torrent enrichment to fetch from configured arr instance URLs
- fix: extract PostWebhookAsync helper to eliminate jscpd clone violations
- fix: merge .NET integration tests into make integration target
- fix: comprehensive security hardening across auth, protocol, and crypto layers
- fix: dotnet-format whitespace in ExternalIpService HttpClient initializer
- fix: comprehensive memory leak remediation across backend and frontend
- fix: remove -f from curl in validation test that expects 400
- fix: resolve lint failures for editorconfig and jscpd
- fix: show correct version and full release history on updates page
- fix: update docs to match actual codebase, fix skull logo color
- fix: add seedarr text logo below skull in README
- fix: update README logo to new skull design, remove old logo
- fix: use --no-deps for configure service to avoid depends_on blocking
- fix: use --no-deps when starting seedarr to avoid depends_on blocking
- fix: start dependency services before seedarr to avoid podman-compose hang
- fix: make integration runs full podman-compose stack + test-integration.sh
- fix: add file-level SC2034 disable for eval-used variables
- fix: file-level SC2016 disable and tab continuation indents for shfmt
- fix: resolve shellcheck, shfmt, and ts-standard lint errors
- fix: resolve CSS and editorconfig lint errors
- fix: resolve CI lint failures, update READMEs

### 🔧 Maintenance & Improvements
- Add or update GitHub Actions workflows
- Add seeder and tracker logging, multi-tracker filtering, and improve add torrent layout
- Generate and persist client UUID, and update external IP lookup to query seedarr.net with UUID
- Enhance PeerMap topology with dynamic radial spacing, collision avoidance, hover neighborhood highlighting, and zoom controls
- Fix button icon/text horizontal alignment and enable vertical scrolling with sticky header on historical downloads and client torrents
- Expand AddTorrentPage and AddTorrentForm to fill available viewport space
- Fix TrackerBoost pane scrolling and viewport fill across tabs
- Standardize TorrentGrid card dimensions and layout with DownloadHistory
- Ensure SpeedGraph dynamically spans 100% width of parent container
- Implement rich tracker multi-select modal with search, priority sorting, and auto health probing
- Fix dynamic tracker updates, domain extraction, and SignalR query invalidation
- Fix horizontal padding and alignment on torrent details page
- Add command palette, keyboard shortcuts, piece map, peer client badges, seeding simulator, and bulk tracker tools
- Format CSS and frontend components with Prettier
- Expand swarms list to full available height and enhance action button visibility
- Add rich poster grid view to swarm cross matrix, tracker favicon discovery, and clean swarm actions
- Fix metric chart boundary overflow, pin tracker management bar, and add tracker boost activity logs
- Add tracker management with live swarm indicators, remove actions, and instant announce
- ci: disable css linting in checks.sh
- chore: update gitignore
- style: format frontend with prettier and add VALIDATE_CSS_PRETTIER: false to ci
- ci: disable super-linter css validator
- Add persistent download history, Prowlarr indexer search, and reorganize navigation (v1.3.0)
- style: fix all editorconfig left-padding indentation violations
- test(downloadclient): add unit test coverage for DownloadClientSyncService
- style: fix indentation in ArrWebhookRegistration to satisfy editorconfig
- style: format entire codebase with prettier
- Fix trailing whitespace in App.tsx
- Implement actual indexer querying functionality by hash for DownloadClient Sync
- Implement Download Client Sync Framework and UI
- Add API Key to topbar
- Enhance UI status bar with system info and larger text
- style: Improve frontend UI contrast
- refactor: extract TorrentDetails.tsx tabs into separate components
- refactor: extract TorrentIndex.tsx into focused components
- refactor: extract TorrentDetailPanel.tsx tabs into separate components
- refactor: extract Settings.tsx tabs into separate components
- refactor(api): break up TorrentController God class
- refactor(frontend): extract TorrentContextMenu from TorrentTable
- refactor(frontend): extract inline icons from App.tsx to AppIcons module
- test: update TrackerServer tests for binary compact peers and IP parsing fixes
- chore(lint): ignore superSeeding in codespell (BitTorrent technical term)
- perf(container): reduce image size ~100MB
- refactor(ui): consolidate duplicated code, add logo pulse animation
- chore(deps): upgrade React 18→19, react-router 7→8, ESLint 8→10, webpack tools
- chore: upgrade container Node.js from 20 to 24 LTS
- chore(deps): upgrade all outdated packages
- Bump serialize-javascript and copy-webpack-plugin
- ci: disable ts-standard linter incompatible with project tsconfig
- ci: disable JSCPD validation for C# test code duplication
- test: add integration tests for 5 previously uncovered controllers
- test: add 11 new automation test files covering untested API endpoints
- test: add .NET integration test project (27 tests, real app boot)
- test: achieve 90.5% unit test coverage (2388 tests, 0 failures)
- Fix markdown lint errors in README and Docker Hub description
- Add comprehensive README and Docker Hub description
- Disable analyzers in container publish step
- Fix Docker build: restore Console project instead of full solution
- Downgrade Swashbuckle to 8.1.1 for Microsoft.OpenApi v1 compatibility
- Remove explicit Microsoft.OpenApi 1.6.14 causing package downgrade
- Deduplicate client profile tests to fix jscpd clone detection
- Fix build errors: SA1117, SA1515 in tests, add Microsoft.OpenApi ref
- Fix editorconfig indentation in FastExtension.cs
- Fix C# analyzer errors: SA1117, SA1119, CA1805, CS0221
- Sync package-lock.json with package.json
- Fix editorconfig indentation errors in MseHandshake and PeerConnection
- Enable container build in dispatch pipeline, fix MSE/PE encryption
- Rename Docker files to Podman, remove redundant CI workflows
- Implement all remaining features: 25 issues resolved
- Add SVG icons and integrate into frontend navigation
- Add tags, backup, notifications, LPD, uTP, health checks, logo, frontend polish
- Fix build errors and add complete React frontend
- Fix unused using and null-forgiving operator in tracker/health code
- Add DHT, protocol extensions, built-in tracker, health checks, notifications, network infra
- Add Sonarr/Radarr integration with auto-sync
- Add peer protocol: TCP handshake, message framing, peer server
- Fix SA1203: move constants before non-constant fields
- Add client behavior simulation and traffic patterns
- Add tracker communication: HTTP/UDP announce, multi-tracker failover
- Add seeding simulation engine with speed distribution
- Add torrent file parser, InfoHash calculator, and watch folder
- Add or update GitHub Actions workflows
- Add typecheck and test scripts to frontend package.json
- Add torrent domain model, migration, and REST API
- Add React frontend scaffold with dark theme
- Add command system, ThingiProvider, ConfigService, scheduler
- Fix IDE0005: remove unnecessary using in NzbDroneMigrationBase
- Set NuGetAuditLevel to critical to unblock high-severity transitive dep
- Add database infrastructure: Dapper, FluentMigrator, BasicRepository
- Fix CI: skip frontend jobs when no frontend project exists
- Fix build: simplify Swagger config, remove unused using in Bootstrap
- Fix DryIoc reference: use DryIoc.dll (precompiled) instead of DryIoc (source)
- Fix CS0433: use DryIoc as precompiled library, not embedded source
- Fix IDE0005: remove unnecessary FluentValidation using in RestController
- Fix CS0246: add missing using for IRouteTemplateProvider
- Fix IDE0005: remove unnecessary using in SignalRMessageBroadcaster
- Fix SA1127: put generic constraints on own line in Core messaging
- Fix build errors: suppress DryIoc analyzer warnings, fix style issues
- Add Phase 1 skeleton: bootable app on port 9898
- Add project scaffolding: docs, CI workflows, code quality config

---

## [v1.3.30](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.30) - 2026-08-31

### ✨ Features
- feat(frontend): add getting started guide carousel and interactive setup wizard

### 🐛 Bug Fixes
- fix(automation): disable getting started modal auto-pop in webdriver test runners and add e2e test

---

## [v1.3.29](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.29) - 2026-08-31

### ✨ Features
- feat(trackers): execute real per-tracker announces with detailed event logging on manual announce

---

## [v1.3.28](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.28) - 2026-08-31

### 🔧 Maintenance & Improvements
- Add or update GitHub Actions workflows

---

## [v1.3.27](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.27) - 2026-08-31

### ✨ Features
- feat(api): return JSON payload with torrent and tracker details from announce endpoint

---

## [v1.3.26](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.26) - 2026-08-30

### 🐛 Bug Fixes
- fix(ui): ensure download history and tracker server grids handle large collections with full vertical scroll and box sizing
- fix(ui): prevent grid poster compression and ensure full card height with vertical scrolling

---

## [v1.3.25](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.25) - 2026-08-30

### ✨ Features
- feat(trackers): add tracker metrics telemetry, historical snapshots, and analytics dashboard

### 🔧 Maintenance & Improvements
- Add seeder and tracker logging, multi-tracker filtering, and improve add torrent layout

---

## [v1.3.24](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.24) - 2026-08-30

### 🐛 Bug Fixes
- fix(trackerboost): parse host correctly in IsValidPublicTrackerUrl to avoid falsely excluding subdomains with 127.0.0.1

---

## [v1.3.23](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.23) - 2026-08-30

### 🐛 Bug Fixes
- fix(network): update primary external IP endpoint path to /ip/
- fix(network): update primary external IP endpoint to www.seedarr.net

---

## [v1.3.22](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.22) - 2026-08-30

### 🐛 Bug Fixes
- fix(ui): fix poster rectangular 2:3 aspect ratio and prevent grid cards from stretching to viewport height

### 🔧 Maintenance & Improvements
- Generate and persist client UUID, and update external IP lookup to query seedarr.net with UUID

---

## [v1.3.21](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.21) - 2026-08-30

### 🔧 Maintenance & Improvements
- Enhance PeerMap topology with dynamic radial spacing, collision avoidance, hover neighborhood highlighting, and zoom controls

---

## [v1.3.20](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.20) - 2026-08-30

### 🔧 Maintenance & Improvements
- Fix button icon/text horizontal alignment and enable vertical scrolling with sticky header on historical downloads and client torrents
- Expand AddTorrentPage and AddTorrentForm to fill available viewport space
- Fix TrackerBoost pane scrolling and viewport fill across tabs
- Standardize TorrentGrid card dimensions and layout with DownloadHistory
- Ensure SpeedGraph dynamically spans 100% width of parent container
- Implement rich tracker multi-select modal with search, priority sorting, and auto health probing

---

## [v1.3.19](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.19) - 2026-08-30

### 🔧 Maintenance & Improvements
- Fix dynamic tracker updates, domain extraction, and SignalR query invalidation

---

## [v1.3.18](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.18) - 2026-08-30

### 🔧 Maintenance & Improvements
- Fix horizontal padding and alignment on torrent details page
- Add command palette, keyboard shortcuts, piece map, peer client badges, seeding simulator, and bulk tracker tools

---

## [v1.3.17](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.17) - 2026-08-30

### 🔧 Maintenance & Improvements
- Format CSS and frontend components with Prettier
- Expand swarms list to full available height and enhance action button visibility
- Add rich poster grid view to swarm cross matrix, tracker favicon discovery, and clean swarm actions

---

## [v1.3.16](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.16) - 2026-08-30

### 🔧 Maintenance & Improvements
- Fix metric chart boundary overflow, pin tracker management bar, and add tracker boost activity logs

---

## [v1.3.15](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.15) - 2026-08-30

### 🔧 Maintenance & Improvements
- Add tracker management with live swarm indicators, remove actions, and instant announce

---

## [v1.3.14](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.14) - 2026-08-30

### 🐛 Bug Fixes
- fix(ui): relocate Tracker Boost tab navigation directly above changing content with clear button styling

---

## [v1.3.13](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.13) - 2026-08-30

### ✨ Features
- feat(trackerboost): implement scrape-verified TrackerBoost with live harvesting and cross-matrix explorer
- feat(system): overhaul look and feel across all 7 System pages with elevated cards, page-headers, and status pills
- feat(settings): upgrade all Settings tabs and Tags page with SectionCard layouts, unified SaveBar toolbar, and elevated cards
- feat(settings): upgrade Settings pages with standard page-header, elevated SaveBar toolbar card, and section cards for General settings
- feat(statistics): fix SpeedGraph SVG stretching with proportional viewBox and gradient area fills, elevate gamification and tracker buffer cards
- feat(schedule): elevate Speed Schedule with multi-column rate limit stat cards, matrix calendar, and rich schedule table
- feat(tracker): nest Inbuilt and Boost submenus under Tracker navigation, elevate stat cards and announce URLs, and integrate Download++ into Boost
- feat(activity): upgrade metrics into responsive 420px elevated cards with SVG gradients, clean typography, and live heartbeat indicator
- feat(activity): add rich poster art, [Posters | Table] view switcher, clean page-header, and Arr links to Download Client torrents page
- feat(torrents): add dedicated /torrents/add page and expand AddTorrentModal to wide 1020px responsive container with rich indexer search table
- feat(ui): add poster art to activity grid & tracker server, sonarr-style soft buttons, cards drop shadows, and form control consistency
- feat(library): auto-reconcile existing torrents into history and enrich metadata via Sonarr/Radarr/Lidarr title lookups
- feat(download++): implement Download++ agent with tracker radar, Prowlarr harvesting, per-torrent detection matrix, and swarm booster
- feat(ui): add seeding achievements hall of fame, gamification milestones, HNR minimum seed time protection, and tracker buffer estimator
- feat(ui): implement usability & integration enhancements across dashboard, torrents, peer map, tracker server, and system status
- feat(ui): add Arr deep-links, external metadata links, and clickable actor search across UI
- feat(history): enrich download history with Arr posters, actors, and media grid view
- feat(indexers): add test connection button and detailed feedback banner to indexer modal
- feat(downloadclient): add dynamic agent submenus and client torrents view with library import
- feat(downloadclient): add modal test button with detailed connection feedback
- feat: Add configurable WebhookHost to Arr Connections
- feat: finite global speed limits with sane defaults
- feat: multi-file uploads, per-torrent event logs, webhook secret, toolbar rework
- feat: add authenticated webhook E2E test with two-stack CI integration
- feat: replace custom events with typed ModelEvent, wire SignalR
- feat(network): event-driven external IP refresh via NetworkAddressChanged
- feat(network): refresh external IP every hour via background service
- feat(health): add setup guidance checks for connections, clients, indexers
- feat(ui): wire up heart (donate) and user (actions menu) topbar buttons
- feat(peers): add outgoing peer connections and network diagnostics
- feat(network): add diagnostics endpoint and UI page
- feat(ui): add 4 new pages, bulk actions, file tree, dashboard widgets, unsaved guard
- feat(coverage): add dotnet-coverage instrumentation to container
- feat: migrate all bash smoke tests to NzbDrone.Automation.Test
- feat: add Selenium ChromeDriver automation test project
- feat: add peer connection time-series with D3 mindmap and config API refactor
- feat: add seedarr.net website references throughout codebase
- feat: add make integration target for CI integration test job
- feat: add webhook receiver, indexer management, and integration test suite
- feat: comprehensive UI overhaul, seeding engine fixes, CI lint fixes
- feat: expand settings to 121 config properties across 10 sections

### 🐛 Bug Fixes
- fix(ui): apply content-area wrapper to dashboard and history pages for consistent horizontal spacing
- fix(db): add TotalVerifiedTorrents migration 029 and handle update api rate limits
- fix(ui): update menu and headers to Tracker Boost
- fix(styles): remove duplicate css selector and add root stylelintrc
- fix(styles): modernize css color syntax and adjust stylelint rules
- fix(trackers): fix torrent tracker parsing and persist trackers on sync and webhooks
- fix(trackerboost): adjust indentation in TrackerBoostService for editorconfig
- fix(peermap): aggregate active torrent swarms and tracker peers into graph endpoint and elevate Peer Map visualization container
- fix(history): align Historical Downloads and Tracker headers, view-toggles, and poster grid width to reference standard
- fix(dashboard): resolve empty health alerts void, elevate stat cards with drop shadows, and make speed graph responsive
- fix(ci): remove invalid workflow input
- fix(test): make torrentRepository optional in ArrMetadataEnricherService and add test cases
- fix(download++): connect real download agents (qBit, Transmission, Deluge) and fix editorconfig indentation
- fix(layout): constrain torrent table scrolling and pin selected detail panel in viewport
- fix(frontend): rename to Indexer Search, add auto-debounce typing and empty state guidance
- fix(navigation): ensure top-level Torrents navigates to /torrents and update navigation tests
- fix(sync): refine fallback condition when importing download client items
- fix(sync): allow importing torrents from client items when .torrent bytes unavailable & add enable toggle to indexers
- fix(stylecop): satisfy SA1513 blank line rule in DownloadClientController
- fix(downloadclient): ensure default Implementation and ConfigContract on download client creation
- fix(arr): compare existing API key safely when empty
- fix(arr): update existing webhook notification in place and use loopback for local arr connections
- fix(arr): support full URL in WebhookHost and ignore hex container IDs in BindAddress
- fix(ci): fix shellcheck and shfmt formatting in checks.sh and disable bash linters
- fix(ci): add checks.sh and disable failing linters for super-linter
- fix(arr): isolate connection testing, add modal test button with error feedback, and enhance sync reporting
- fix: Dynamically populate AnnounceInterval and NextUpdate columns in torrent list
- fix: guard distribution manager caches and align redistribution modes (Closes #92) (#94)
- fix: attach X-Api-Key in automation webhook posts
- fix: supply real instance api key to automation tests via env
- fix: read instance api key from test data config.xml
- fix: fetch instance api key via config endpoint in webhook tests
- fix: webhook integration tests authenticate with instance API key
- fix: drop unused using
- fix: enforce X-Api-Key on webhook receiver
- fix: unblock integration suites
- fix: provide repo stylelint config to override broken CI default
- fix: resolve linter violations flagged by CI
- fix: rename idEl to avoid codespell false positive
- fix: use Dns.GetHostName() instead of localhost for wildcard bind address
- fix: stop auto-generating WebhookSecret on connection create
- fix: allow unauthenticated access to webhook endpoint and fix notification payload
- fix: allow unauthenticated webhooks when connection has no secret set
- fix: serve .torrent files from /fixtures by enabling unknown MIME types
- fix: correct TagIds and TrackerEntry.Downloaded data model types
- fix: skip IPv6 peers in PeerExchange CompactPeers to prevent crash
- fix: eliminate thread-safety races in TrafficPatternSimulator and MultiTrackerManager
- fix: redact embedded credentials from tracker URLs before logging
- fix: bake test fixtures into image to fix fixture 404 in CI
- fix: resolve automation test failures for webhooks and fixture serving
- fix: resolve flaky DHT test and webhook integration test setup
- fix: update webhook integration tests to pass X-Seedarr-Secret header
- fix: resolve remaining lint failures
- fix: resolve all lint failures — whitespace, codespell, prettier, typescript-es
- fix: apply wave-2 quality fixes across 80 files, all tests green
- fix: address 15 critical and major quality review findings
- fix: address quality review findings across frontend
- fix: plug protocol crashes, LPD wiring, type safety, infrastructure smells
- fix: address code smell audit — bugs, performance, safety
- fix: plug auth bypass and cancellation bugs
- fix(frontend): use apiClient for system restart/shutdown
- fix: wire command system, add handlers, fix API null crashes, show errors in UI
- fix(container): install coverage tools in SDK stage, not runtime stage
- fix(ci): disable VALIDATE_TSX (super-linter v8 ESLint 9 incompatible with project)
- fix(logging): use correct NLog v6 ArchiveSuffixFormat syntax
- fix(ci): remove deprecated VALIDATE_TYPESCRIPT_STANDARD (removed upstream)
- fix(ci): remove VALIDATE_TRIVY (not supported by dispatch)
- fix(ci): disable new super-linter v8 validators (biome, zizmor, trivy)
- fix: revert to write-all (CHECKOV disabled upstream), fix update cache test
- fix(ci): add .npmrc with legacy-peer-deps for ESLint 10 compatibility
- fix(ci): format workflow YAML for prettier
- fix(ci): scope permissions to pass CHECKOV CKV2_GHA_1
- fix(lint): use modern CSS color notation for stylelint
- fix(lint): expand keyframe selectors to multi-line for stylelint
- fix(ui): prevent sidebar logo resize and fix formatBytes for sub-byte values
- fix(updates): don't cache failed GitHub API responses for 6 hours
- fix(container): add --legacy-peer-deps for ESLint 10 peer conflict
- fix(settings): replace useBlocker with beforeunload for unsaved guard
- fix(seeding): use per-torrent threshold over global default
- fix(deps): resolve all dependabot security alerts
- fix(webhook): add periodic re-registration and surface failures
- fix: unblock 5 of 7 skipped integration tests
- fix: add milliseconds to backup filename to prevent collision within same second
- fix: correct integration test failures from 15 failing CI tests
- fix: extract CreateTestConnectionAsync helper to eliminate jscpd clone in ArrConnectionCrudTests
- fix: use seedarr.local internal hostname for Radarr release/push downloadUrl
- fix: resolve automation test failures and coverlet lock conflict
- fix: resolve jscpd and editorconfig lint violations in Automation.Test
- fix: read Prowlarr API key from container to avoid masked key issue
- fix: correct shfmt formatting for Prowlarr apps curl pipe
- fix: remove masked API key from Prowlarr health and apps checks
- fix: allow torrent enrichment to fetch from configured arr instance URLs
- fix: extract PostWebhookAsync helper to eliminate jscpd clone violations
- fix: merge .NET integration tests into make integration target
- fix: comprehensive security hardening across auth, protocol, and crypto layers
- fix: dotnet-format whitespace in ExternalIpService HttpClient initializer
- fix: comprehensive memory leak remediation across backend and frontend
- fix: remove -f from curl in validation test that expects 400
- fix: resolve lint failures for editorconfig and jscpd
- fix: show correct version and full release history on updates page
- fix: update docs to match actual codebase, fix skull logo color
- fix: add seedarr text logo below skull in README
- fix: update README logo to new skull design, remove old logo
- fix: use --no-deps for configure service to avoid depends_on blocking
- fix: use --no-deps when starting seedarr to avoid depends_on blocking
- fix: start dependency services before seedarr to avoid podman-compose hang
- fix: make integration runs full podman-compose stack + test-integration.sh
- fix: add file-level SC2034 disable for eval-used variables
- fix: file-level SC2016 disable and tab continuation indents for shfmt
- fix: resolve shellcheck, shfmt, and ts-standard lint errors
- fix: resolve CSS and editorconfig lint errors
- fix: resolve CI lint failures, update READMEs

### 🔧 Maintenance & Improvements
- ci: disable css linting in checks.sh
- chore: update gitignore
- style: format frontend with prettier and add VALIDATE_CSS_PRETTIER: false to ci
- ci: disable super-linter css validator
- Add persistent download history, Prowlarr indexer search, and reorganize navigation (v1.3.0)
- style: fix all editorconfig left-padding indentation violations
- test(downloadclient): add unit test coverage for DownloadClientSyncService
- style: fix indentation in ArrWebhookRegistration to satisfy editorconfig
- style: format entire codebase with prettier
- Fix trailing whitespace in App.tsx
- Implement actual indexer querying functionality by hash for DownloadClient Sync
- Implement Download Client Sync Framework and UI
- Add API Key to topbar
- Enhance UI status bar with system info and larger text
- style: Improve frontend UI contrast
- refactor: extract TorrentDetails.tsx tabs into separate components
- refactor: extract TorrentIndex.tsx into focused components
- refactor: extract TorrentDetailPanel.tsx tabs into separate components
- refactor: extract Settings.tsx tabs into separate components
- refactor(api): break up TorrentController God class
- refactor(frontend): extract TorrentContextMenu from TorrentTable
- refactor(frontend): extract inline icons from App.tsx to AppIcons module
- test: update TrackerServer tests for binary compact peers and IP parsing fixes
- chore(lint): ignore superSeeding in codespell (BitTorrent technical term)
- perf(container): reduce image size ~100MB
- refactor(ui): consolidate duplicated code, add logo pulse animation
- chore(deps): upgrade React 18→19, react-router 7→8, ESLint 8→10, webpack tools
- chore: upgrade container Node.js from 20 to 24 LTS
- chore(deps): upgrade all outdated packages
- ci: disable ts-standard linter incompatible with project tsconfig
- ci: disable JSCPD validation for C# test code duplication
- test: add integration tests for 5 previously uncovered controllers
- test: add 11 new automation test files covering untested API endpoints
- test: add .NET integration test project (27 tests, real app boot)
- test: achieve 90.5% unit test coverage (2388 tests, 0 failures)
- Bump serialize-javascript and copy-webpack-plugin
- Fix markdown lint errors in README and Docker Hub description
- Add comprehensive README and Docker Hub description
- Disable analyzers in container publish step
- Fix Docker build: restore Console project instead of full solution
- Downgrade Swashbuckle to 8.1.1 for Microsoft.OpenApi v1 compatibility
- Remove explicit Microsoft.OpenApi 1.6.14 causing package downgrade
- Deduplicate client profile tests to fix jscpd clone detection
- Fix build errors: SA1117, SA1515 in tests, add Microsoft.OpenApi ref
- Fix editorconfig indentation in FastExtension.cs
- Fix C# analyzer errors: SA1117, SA1119, CA1805, CS0221
- Sync package-lock.json with package.json
- Fix editorconfig indentation errors in MseHandshake and PeerConnection
- Enable container build in dispatch pipeline, fix MSE/PE encryption
- Rename Docker files to Podman, remove redundant CI workflows
- Implement all remaining features: 25 issues resolved
- Add SVG icons and integrate into frontend navigation
- Add tags, backup, notifications, LPD, uTP, health checks, logo, frontend polish
- Fix build errors and add complete React frontend
- Fix unused using and null-forgiving operator in tracker/health code
- Add DHT, protocol extensions, built-in tracker, health checks, notifications, network infra
- Add Sonarr/Radarr integration with auto-sync
- Add peer protocol: TCP handshake, message framing, peer server
- Fix SA1203: move constants before non-constant fields
- Add client behavior simulation and traffic patterns
- Add tracker communication: HTTP/UDP announce, multi-tracker failover
- Add seeding simulation engine with speed distribution
- Add torrent file parser, InfoHash calculator, and watch folder
- Add or update GitHub Actions workflows
- Add typecheck and test scripts to frontend package.json
- Add torrent domain model, migration, and REST API
- Add React frontend scaffold with dark theme
- Add command system, ThingiProvider, ConfigService, scheduler
- Fix IDE0005: remove unnecessary using in NzbDroneMigrationBase
- Set NuGetAuditLevel to critical to unblock high-severity transitive dep
- Add database infrastructure: Dapper, FluentMigrator, BasicRepository
- Fix CI: skip frontend jobs when no frontend project exists
- Fix build: simplify Swagger config, remove unused using in Bootstrap
- Fix DryIoc reference: use DryIoc.dll (precompiled) instead of DryIoc (source)
- Fix CS0433: use DryIoc as precompiled library, not embedded source
- Fix IDE0005: remove unnecessary FluentValidation using in RestController
- Fix CS0246: add missing using for IRouteTemplateProvider
- Fix IDE0005: remove unnecessary using in SignalRMessageBroadcaster
- Fix SA1127: put generic constraints on own line in Core messaging
- Fix build errors: suppress DryIoc analyzer warnings, fix style issues
- Add Phase 1 skeleton: bootable app on port 9898
- Add project scaffolding: docs, CI workflows, code quality config

---

## [v1.3.12](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.12) - 2026-08-18

### ✨ Features
- feat(trackerboost): implement scrape-verified TrackerBoost with live harvesting and cross-matrix explorer

### 🐛 Bug Fixes
- fix(trackerboost): adjust indentation in TrackerBoostService for editorconfig

---

## [v1.3.11](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.11) - 2026-08-17

### ✨ Features
- feat(system): overhaul look and feel across all 7 System pages with elevated cards, page-headers, and status pills
- feat(settings): upgrade all Settings tabs and Tags page with SectionCard layouts, unified SaveBar toolbar, and elevated cards
- feat(settings): upgrade Settings pages with standard page-header, elevated SaveBar toolbar card, and section cards for General settings

---

## [v1.3.10](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.10) - 2026-08-16

### ✨ Features
- feat(statistics): fix SpeedGraph SVG stretching with proportional viewBox and gradient area fills, elevate gamification and tracker buffer cards
- feat(schedule): elevate Speed Schedule with multi-column rate limit stat cards, matrix calendar, and rich schedule table
- feat(tracker): nest Inbuilt and Boost submenus under Tracker navigation, elevate stat cards and announce URLs, and integrate Download++ into Boost
- feat(activity): upgrade metrics into responsive 420px elevated cards with SVG gradients, clean typography, and live heartbeat indicator
- feat(activity): add rich poster art, [Posters | Table] view switcher, clean page-header, and Arr links to Download Client torrents page
- feat(torrents): add dedicated /torrents/add page and expand AddTorrentModal to wide 1020px responsive container with rich indexer search table

### 🐛 Bug Fixes
- fix(peermap): aggregate active torrent swarms and tracker peers into graph endpoint and elevate Peer Map visualization container

---

## [v1.3.9](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.9) - 2026-08-13

### 🐛 Bug Fixes
- fix(history): align Historical Downloads and Tracker headers, view-toggles, and poster grid width to reference standard
- fix(dashboard): resolve empty health alerts void, elevate stat cards with drop shadows, and make speed graph responsive

---

## [v1.3.8](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.8) - 2026-08-12

### ✨ Features
- feat(ui): add poster art to activity grid & tracker server, sonarr-style soft buttons, cards drop shadows, and form control consistency

### 🐛 Bug Fixes
- fix(ci): remove invalid workflow input

### 🔧 Maintenance & Improvements
- style: format frontend with prettier and add VALIDATE_CSS_PRETTIER: false to ci

---

## [v1.3.7](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.7) - 2026-08-12

### ✨ Features
- feat(library): auto-reconcile existing torrents into history and enrich metadata via Sonarr/Radarr/Lidarr title lookups

### 🐛 Bug Fixes
- fix(test): make torrentRepository optional in ArrMetadataEnricherService and add test cases

---

## [v1.3.6](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.6) - 2026-08-11

### ✨ Features
- feat(download++): implement Download++ agent with tracker radar, Prowlarr harvesting, per-torrent detection matrix, and swarm booster

### 🐛 Bug Fixes
- fix(download++): connect real download agents (qBit, Transmission, Deluge) and fix editorconfig indentation

---

## [v1.3.5](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.5) - 2026-08-11

### ✨ Features
- feat(ui): add seeding achievements hall of fame, gamification milestones, HNR minimum seed time protection, and tracker buffer estimator

---

## [v1.3.4](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.4) - 2026-08-10

### ✨ Features
- feat(ui): implement usability & integration enhancements across dashboard, torrents, peer map, tracker server, and system status

---

## [v1.3.3](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.3) - 2026-08-10

### ✨ Features
- feat(ui): add Arr deep-links, external metadata links, and clickable actor search across UI

### 🐛 Bug Fixes
- fix(layout): constrain torrent table scrolling and pin selected detail panel in viewport
- fix(frontend): rename to Indexer Search, add auto-debounce typing and empty state guidance

### 🔧 Maintenance & Improvements
- ci: disable super-linter css validator

---

## [v1.3.2](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.2) - 2026-08-09

### ✨ Features
- feat(history): enrich download history with Arr posters, actors, and media grid view

---

## [v1.3.1](https://github.com/dmzoneill/Seedarr/releases/tag/v1.3.1) - 2026-08-08

### 🐛 Bug Fixes
- fix(navigation): ensure top-level Torrents navigates to /torrents and update navigation tests

### 🔧 Maintenance & Improvements
- Add persistent download history, Prowlarr indexer search, and reorganize navigation (v1.3.0)

---

## [v1.1.3](https://github.com/dmzoneill/Seedarr/releases/tag/v1.1.3) - 2026-08-07

### ✨ Features
- feat(indexers): add test connection button and detailed feedback banner to indexer modal

### 🐛 Bug Fixes
- fix(sync): refine fallback condition when importing download client items
- fix(sync): allow importing torrents from client items when .torrent bytes unavailable & add enable toggle to indexers

---

## [v1.1.2](https://github.com/dmzoneill/Seedarr/releases/tag/v1.1.2) - 2026-08-07

### ✨ Features
- feat(downloadclient): add dynamic agent submenus and client torrents view with library import

### 🐛 Bug Fixes
- fix(stylecop): satisfy SA1513 blank line rule in DownloadClientController
- fix(downloadclient): ensure default Implementation and ConfigContract on download client creation

### 🔧 Maintenance & Improvements
- style: fix all editorconfig left-padding indentation violations

---

## [v1.1.1](https://github.com/dmzoneill/Seedarr/releases/tag/v1.1.1) - 2026-08-06

### ✨ Features
- feat(downloadclient): add modal test button with detailed connection feedback

### 🔧 Maintenance & Improvements
- test(downloadclient): add unit test coverage for DownloadClientSyncService

---

## [v1.0.41](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.41) - 2026-08-05

### 🐛 Bug Fixes
- fix(arr): compare existing API key safely when empty
- fix(arr): update existing webhook notification in place and use loopback for local arr connections
- fix(arr): support full URL in WebhookHost and ignore hex container IDs in BindAddress

### 🔧 Maintenance & Improvements
- style: fix indentation in ArrWebhookRegistration to satisfy editorconfig

---

## [v1.0.40](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.40) - 2026-08-04

### 🐛 Bug Fixes
- fix(ci): fix shellcheck and shfmt formatting in checks.sh and disable bash linters
- fix(ci): add checks.sh and disable failing linters for super-linter
- fix(arr): isolate connection testing, add modal test button with error feedback, and enhance sync reporting

### 🔧 Maintenance & Improvements
- style: format entire codebase with prettier

---

## [v1.0.39](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.39) - 2026-08-03

### 🔧 Maintenance & Improvements
- Fix trailing whitespace in App.tsx
- Implement actual indexer querying functionality by hash for DownloadClient Sync
- Implement Download Client Sync Framework and UI
- Add API Key to topbar
- Enhance UI status bar with system info and larger text

---

## [v1.0.38](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.38) - 2026-07-31

### 🐛 Bug Fixes
- fix: Dynamically populate AnnounceInterval and NextUpdate columns in torrent list

### 🔧 Maintenance & Improvements
- style: Improve frontend UI contrast

---

## [v1.0.37](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.37) - 2026-07-31

### ✨ Features
- feat: Add configurable WebhookHost to Arr Connections

---

## [v1.0.36](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.36) - 2026-07-31

### 🐛 Bug Fixes
- fix: guard distribution manager caches and align redistribution modes (Closes #92) (#94)

---

## [v1.0.35](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.35) - 2026-07-30

### ✨ Features
- feat: finite global speed limits with sane defaults
- feat: multi-file uploads, per-torrent event logs, webhook secret, toolbar rework

### 🐛 Bug Fixes
- fix: attach X-Api-Key in automation webhook posts
- fix: supply real instance api key to automation tests via env
- fix: read instance api key from test data config.xml
- fix: fetch instance api key via config endpoint in webhook tests
- fix: webhook integration tests authenticate with instance API key
- fix: drop unused using
- fix: enforce X-Api-Key on webhook receiver
- fix: unblock integration suites
- fix: provide repo stylelint config to override broken CI default
- fix: resolve linter violations flagged by CI

---

## [v1.0.34](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.34) - 2026-07-27

### ✨ Features
- feat: add authenticated webhook E2E test with two-stack CI integration

### 🐛 Bug Fixes
- fix: rename idEl to avoid codespell false positive

---

## [v1.0.33](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.33) - 2026-07-27

### 🐛 Bug Fixes
- fix: use Dns.GetHostName() instead of localhost for wildcard bind address

---

## [v1.0.32](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.32) - 2026-07-26

### 🐛 Bug Fixes
- fix: stop auto-generating WebhookSecret on connection create
- fix: allow unauthenticated access to webhook endpoint and fix notification payload
- fix: allow unauthenticated webhooks when connection has no secret set
- fix: serve .torrent files from /fixtures by enabling unknown MIME types
- fix: correct TagIds and TrackerEntry.Downloaded data model types
- fix: skip IPv6 peers in PeerExchange CompactPeers to prevent crash
- fix: eliminate thread-safety races in TrafficPatternSimulator and MultiTrackerManager
- fix: redact embedded credentials from tracker URLs before logging
- fix: bake test fixtures into image to fix fixture 404 in CI
- fix: resolve automation test failures for webhooks and fixture serving
- fix: resolve flaky DHT test and webhook integration test setup
- fix: update webhook integration tests to pass X-Seedarr-Secret header
- fix: resolve remaining lint failures
- fix: resolve all lint failures — whitespace, codespell, prettier, typescript-es
- fix: apply wave-2 quality fixes across 80 files, all tests green
- fix: address 15 critical and major quality review findings
- fix: address quality review findings across frontend
- fix: plug protocol crashes, LPD wiring, type safety, infrastructure smells
- fix: address code smell audit — bugs, performance, safety

### 🔧 Maintenance & Improvements
- refactor: extract TorrentDetails.tsx tabs into separate components
- refactor: extract TorrentIndex.tsx into focused components
- refactor: extract TorrentDetailPanel.tsx tabs into separate components
- refactor: extract Settings.tsx tabs into separate components
- refactor(api): break up TorrentController God class
- refactor(frontend): extract TorrentContextMenu from TorrentTable
- refactor(frontend): extract inline icons from App.tsx to AppIcons module
- test: update TrackerServer tests for binary compact peers and IP parsing fixes

---

## [v1.0.31](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.31) - 2026-07-16

### ✨ Features
- feat: replace custom events with typed ModelEvent, wire SignalR

### 🐛 Bug Fixes
- fix: plug auth bypass and cancellation bugs

---

## [v1.0.30](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.30) - 2026-07-16

### 🐛 Bug Fixes
- fix(frontend): use apiClient for system restart/shutdown

---

## [v1.0.29](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.29) - 2026-07-16

### 🐛 Bug Fixes
- fix: wire command system, add handlers, fix API null crashes, show errors in UI

### 🔧 Maintenance & Improvements
- chore(lint): ignore superSeeding in codespell (BitTorrent technical term)

---

## [v1.0.28](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.28) - 2026-07-15

### 🐛 Bug Fixes
- fix(container): install coverage tools in SDK stage, not runtime stage

### 🔧 Maintenance & Improvements
- perf(container): reduce image size ~100MB

---

## [v1.0.27](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.27) - 2026-07-15

### ✨ Features
- feat(network): event-driven external IP refresh via NetworkAddressChanged
- feat(network): refresh external IP every hour via background service

---

## [v1.0.26](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.26) - 2026-07-14

### ✨ Features
- feat(health): add setup guidance checks for connections, clients, indexers

### 🐛 Bug Fixes
- fix(ci): disable VALIDATE_TSX (super-linter v8 ESLint 9 incompatible with project)

---

## [v1.0.25](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.25) - 2026-07-14

### 🐛 Bug Fixes
- fix(logging): use correct NLog v6 ArchiveSuffixFormat syntax

---

## [v1.0.24](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.24) - 2026-07-13

### ✨ Features
- feat(ui): wire up heart (donate) and user (actions menu) topbar buttons

### 🐛 Bug Fixes
- fix(ci): remove deprecated VALIDATE_TYPESCRIPT_STANDARD (removed upstream)
- fix(ci): remove VALIDATE_TRIVY (not supported by dispatch)
- fix(ci): disable new super-linter v8 validators (biome, zizmor, trivy)
- fix: revert to write-all (CHECKOV disabled upstream), fix update cache test
- fix(ci): add .npmrc with legacy-peer-deps for ESLint 10 compatibility
- fix(ci): format workflow YAML for prettier
- fix(ci): scope permissions to pass CHECKOV CKV2_GHA_1
- fix(lint): use modern CSS color notation for stylelint
- fix(lint): expand keyframe selectors to multi-line for stylelint
- fix(ui): prevent sidebar logo resize and fix formatBytes for sub-byte values
- fix(updates): don't cache failed GitHub API responses for 6 hours

### 🔧 Maintenance & Improvements
- refactor(ui): consolidate duplicated code, add logo pulse animation

---

## [v1.0.23](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.23) - 2026-07-10

### 🐛 Bug Fixes
- fix(container): add --legacy-peer-deps for ESLint 10 peer conflict
- fix(settings): replace useBlocker with beforeunload for unsaved guard

### 🔧 Maintenance & Improvements
- chore(deps): upgrade React 18→19, react-router 7→8, ESLint 8→10, webpack tools
- chore: upgrade container Node.js from 20 to 24 LTS
- chore(deps): upgrade all outdated packages

---

## [v1.0.22](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.22) - 2026-07-09

### 🔧 Maintenance & Improvements
- Maintenance and stability release

---

## [v1.0.21](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.21) - 2026-07-08

### ✨ Features
- feat(peers): add outgoing peer connections and network diagnostics

---

## [v1.0.20](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.20) - 2026-07-08

### ✨ Features
- feat(network): add diagnostics endpoint and UI page

---

## [v1.0.19](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.19) - 2026-07-08

### 🐛 Bug Fixes
- fix(seeding): use per-torrent threshold over global default
- fix(deps): resolve all dependabot security alerts

---

## [v1.0.18](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.18) - 2026-07-07

### 🔧 Maintenance & Improvements
- Bump serialize-javascript and copy-webpack-plugin

---

## [v1.0.17](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.17) - 2026-07-07

### ✨ Features
- feat(ui): add 4 new pages, bulk actions, file tree, dashboard widgets, unsaved guard

### 🔧 Maintenance & Improvements
- ci: disable ts-standard linter incompatible with project tsconfig

---

## [v1.0.16](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.16) - 2026-07-06

### ✨ Features
- feat(coverage): add dotnet-coverage instrumentation to container

### 🐛 Bug Fixes
- fix(webhook): add periodic re-registration and surface failures

### 🔧 Maintenance & Improvements
- ci: disable JSCPD validation for C# test code duplication

---

## [v1.0.15](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.15) - 2026-07-06

### 🐛 Bug Fixes
- fix: unblock 5 of 7 skipped integration tests

---

## [v1.0.14](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.14) - 2026-07-04

### 🐛 Bug Fixes
- fix: add milliseconds to backup filename to prevent collision within same second
- fix: correct integration test failures from 15 failing CI tests
- fix: extract CreateTestConnectionAsync helper to eliminate jscpd clone in ArrConnectionCrudTests

### 🔧 Maintenance & Improvements
- test: add integration tests for 5 previously uncovered controllers
- test: add 11 new automation test files covering untested API endpoints

---

## [v1.0.13](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.13) - 2026-07-02

### ✨ Features
- feat: migrate all bash smoke tests to NzbDrone.Automation.Test

### 🐛 Bug Fixes
- fix: use seedarr.local internal hostname for Radarr release/push downloadUrl
- fix: resolve automation test failures and coverlet lock conflict
- fix: resolve jscpd and editorconfig lint violations in Automation.Test

---

## [v1.0.12](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.12) - 2026-07-02

### ✨ Features
- feat: add Selenium ChromeDriver automation test project

---

## [v1.0.11](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.11) - 2026-07-01

### 🐛 Bug Fixes
- fix: read Prowlarr API key from container to avoid masked key issue
- fix: correct shfmt formatting for Prowlarr apps curl pipe
- fix: remove masked API key from Prowlarr health and apps checks
- fix: allow torrent enrichment to fetch from configured arr instance URLs
- fix: extract PostWebhookAsync helper to eliminate jscpd clone violations
- fix: merge .NET integration tests into make integration target
- fix: comprehensive security hardening across auth, protocol, and crypto layers

### 🔧 Maintenance & Improvements
- test: add .NET integration test project (27 tests, real app boot)
- test: achieve 90.5% unit test coverage (2388 tests, 0 failures)

---

## [v1.0.10](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.10) - 2026-06-29

### 🐛 Bug Fixes
- fix: dotnet-format whitespace in ExternalIpService HttpClient initializer
- fix: comprehensive memory leak remediation across backend and frontend

---

## [v1.0.9](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.9) - 2026-06-28

### ✨ Features
- feat: add peer connection time-series with D3 mindmap and config API refactor
- feat: add seedarr.net website references throughout codebase

### 🐛 Bug Fixes
- fix: remove -f from curl in validation test that expects 400
- fix: resolve lint failures for editorconfig and jscpd
- fix: show correct version and full release history on updates page
- fix: update docs to match actual codebase, fix skull logo color

---

## [v1.0.8](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.8) - 2026-06-26

### 🐛 Bug Fixes
- fix: add seedarr text logo below skull in README
- fix: update README logo to new skull design, remove old logo

---

## [v1.0.7](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.7) - 2026-06-25

### 🐛 Bug Fixes
- fix: use --no-deps for configure service to avoid depends_on blocking
- fix: use --no-deps when starting seedarr to avoid depends_on blocking
- fix: start dependency services before seedarr to avoid podman-compose hang
- fix: make integration runs full podman-compose stack + test-integration.sh

---

## [v1.0.6](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.6) - 2026-06-24

### ✨ Features
- feat: add make integration target for CI integration test job

---

## [v1.0.5](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.5) - 2026-06-24

### ✨ Features
- feat: add webhook receiver, indexer management, and integration test suite

### 🐛 Bug Fixes
- fix: add file-level SC2034 disable for eval-used variables
- fix: file-level SC2016 disable and tab continuation indents for shfmt
- fix: resolve shellcheck, shfmt, and ts-standard lint errors

---

## [v1.0.4](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.4) - 2026-06-23

### ✨ Features
- feat: comprehensive UI overhaul, seeding engine fixes, CI lint fixes
- feat: expand settings to 121 config properties across 10 sections

### 🐛 Bug Fixes
- fix: resolve CSS and editorconfig lint errors
- fix: resolve CI lint failures, update READMEs

---

## [v1.0.3](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.3) - 2026-06-22

### 🔧 Maintenance & Improvements
- Fix markdown lint errors in README and Docker Hub description
- Add comprehensive README and Docker Hub description

---

## [v1.0.2](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.2) - 2026-06-21

### 🔧 Maintenance & Improvements
- Disable analyzers in container publish step

---

## [v1.0.1](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.1) - 2026-06-19

### 🔧 Maintenance & Improvements
- Fix Docker build: restore Console project instead of full solution

---

## [v1.0.0](https://github.com/dmzoneill/Seedarr/releases/tag/v1.0.0) - 2026-06-19

### 🔧 Maintenance & Improvements
- Downgrade Swashbuckle to 8.1.1 for Microsoft.OpenApi v1 compatibility
- Remove explicit Microsoft.OpenApi 1.6.14 causing package downgrade
- Deduplicate client profile tests to fix jscpd clone detection
- Fix build errors: SA1117, SA1515 in tests, add Microsoft.OpenApi ref
- Fix editorconfig indentation in FastExtension.cs
- Fix C# analyzer errors: SA1117, SA1119, CA1805, CS0221
- Sync package-lock.json with package.json
- Fix editorconfig indentation errors in MseHandshake and PeerConnection
- Enable container build in dispatch pipeline, fix MSE/PE encryption
- Rename Docker files to Podman, remove redundant CI workflows
- Implement all remaining features: 25 issues resolved
- Add SVG icons and integrate into frontend navigation
- Add tags, backup, notifications, LPD, uTP, health checks, logo, frontend polish
- Fix build errors and add complete React frontend
- Fix unused using and null-forgiving operator in tracker/health code
- Add DHT, protocol extensions, built-in tracker, health checks, notifications, network infra

---

## [v0.0.3](https://github.com/dmzoneill/Seedarr/releases/tag/v0.0.3) - 2026-06-15

### 🔧 Maintenance & Improvements
- Add Sonarr/Radarr integration with auto-sync
- Add peer protocol: TCP handshake, message framing, peer server
- Fix SA1203: move constants before non-constant fields

---

## [v0.0.2](https://github.com/dmzoneill/Seedarr/releases/tag/v0.0.2) - 2026-06-13

### 🔧 Maintenance & Improvements
- Add client behavior simulation and traffic patterns
- Add tracker communication: HTTP/UDP announce, multi-tracker failover
- Add seeding simulation engine with speed distribution
- Add torrent file parser, InfoHash calculator, and watch folder
- Add or update GitHub Actions workflows
- Add typecheck and test scripts to frontend package.json
- Add torrent domain model, migration, and REST API
- Add React frontend scaffold with dark theme
- Add command system, ThingiProvider, ConfigService, scheduler
- Fix IDE0005: remove unnecessary using in NzbDroneMigrationBase
- Set NuGetAuditLevel to critical to unblock high-severity transitive dep
- Add database infrastructure: Dapper, FluentMigrator, BasicRepository
- Fix CI: skip frontend jobs when no frontend project exists
- Fix build: simplify Swagger config, remove unused using in Bootstrap
- Fix DryIoc reference: use DryIoc.dll (precompiled) instead of DryIoc (source)
- Fix CS0433: use DryIoc as precompiled library, not embedded source
- Fix IDE0005: remove unnecessary FluentValidation using in RestController
- Fix CS0246: add missing using for IRouteTemplateProvider
- Fix IDE0005: remove unnecessary using in SignalRMessageBroadcaster
- Fix SA1127: put generic constraints on own line in Core messaging
- Fix build errors: suppress DryIoc analyzer warnings, fix style issues
- Add Phase 1 skeleton: bootable app on port 9898
- Add project scaffolding: docs, CI workflows, code quality config

