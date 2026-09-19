# Changelog

All notable changes to **Seedarr** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [v1.8.0](https://github.com/dmzoneill/Seedarr/releases/tag/v1.8.0) - 2026-09-19

### ✨ Features
- feat(ui): make context menu selection-aware and add queue reordering buttons

## [v1.7.0](https://github.com/dmzoneill/Seedarr/releases/tag/v1.7.0) - 2026-09-19

### ✨ Features
- feat(peers): implement LAN peer detection, correct I flag semantics, and health-based peer eviction (Closes #402)

## [v1.6.11](https://github.com/dmzoneill/Seedarr/releases/tag/v1.6.11) - 2026-09-19

### ✨ Features
- feat(blocklist): implement HTTP conditional caching and rate-limit backoff (Closes #382)
- feat(fastresume): implement libtorrent-compliant bencode schema for FastResume state serialization (Closes #403)
- feat(portmapping): implement native NAT-PMP client (RFC 6886) (Closes #418)

### 🐛 Bug Fixes
- fix(test): set explicit timeout in UtpConnection deadlock test to prevent timeout

### 🔧 Maintenance & Improvements
- perf(docker): optimize release container size and separate test stage with coverage tools
- perf(lpd): implement paced multicast announcement rate limiting (Closes #401)

## [v1.6.10](https://github.com/dmzoneill/Seedarr/releases/tag/v1.6.10) - 2026-09-19

### ✨ Features
- feat(analytics): integrate Google Analytics (GA4) G-KTFS19RQ76
- feat(update): implement UpdatePackageProvider and channel selection (Closes #413)
- feat(rss): implement persistent release deduplication store (Closes #414)
- feat(pieces): implement incremental SwarmPieceHistogram with rarity buckets (Closes #519)
- feat(update): implement PostUpdateVerificationService and update status API (Closes #416)
- feat(portmapping): implement cross-platform gateway discovery and protocol probing hierarchy (Closes #417)
- feat(notifications): implement custom payload templates, HTTP method selection, and Basic Auth in Webhooks (Closes #406)
- feat(superseeding): implement automated exit criteria on swarm availability and secondary seed detection (Closes #426)
- feat(rss): respect feed TTL and enforce indexer rate-limit backoff (Closes #412)
- feat(trackers): implement BEP 15 multi-infohash UDP scrape batching (Closes #410)

### 🐛 Bug Fixes
- fix(tests): resolve hanging peer connections and fix remaining unit test failures
- fix(ci): fix editorconfig continuation indentation in NotificationController and GatewayDiscoveryServiceTests
- fix(fastresume): reconcile file size and mtime on boot with background recheck fallback (Closes #405)
- fix(backup): implement automated retention pruning and pre-restore SQLite integrity verification (Closes #250)
- fix(bandwidth): prevent starvation with largest-remainder distribution and account for transport overhead (Closes #391)
- fix(ci): remove undeclared workflow input VALIDATE_CSS_PRETTIER
- fix(ci): format App.css with prettier, fix editorconfig continuation indents, and disable CSS_PRETTIER in super-linter
- fix(trackers): set TrackerStatus.Announcing during in-flight announces and broadcast via SignalR (Closes #354)
- fix(datastore): extend Polly retry policy to derived repositories and PostgreSQL (Closes #303)
- fix(indexers): parse Torznab & Newznab XML error codes and prevent false connection success (Closes #361)
- fix(transport): add optional timeoutMs parameter to IUtpConnection.Receive
- fix(statemachine): persist stopped status when ratio limit is reached (Closes #197)
- fix(emulation): implement file priority persistence in QBittorrentApiController and FilesTab (Closes #348)
- fix(ci): use non-blocking socket Poll in UtpConnection.Receive to prevent unit test hang
- fix(pex): wire PeerExchange to PeerServer Extended message handler (Closes #225)
- fix(downloadclients): optimize batch import and normalize client type casing (Closes #178)
- fix(automation): execute banPeer action and publish PeerBannedEvent (Closes #263)
- fix(seeding): prevent StartAll from corrupting download states and fix StopAll (Closes #200)
- fix(ci): configure socket receive timeouts in uTP connection to prevent unit test deadlock
- fix(settings): integrate navigation blocker to prevent unsaved changes loss (Closes #204)
- fix(telemetry): eliminate duplicate SignalR invalidations and fix speed delta calculation (Closes #196)
- fix(automation): execute dropped pipeline actions and normalize progress percentage (Closes #161)
- fix(ui): bind BandwidthCard sliders to alternative limits in Turtle Mode (Closes #209)
- fix(bandwidth): prevent scheduled speed boost suppression and enforce aggregate caps (Closes #208)
- fix(navigation): support select query parameter in TorrentIndex (Closes #217)
- fix(signalr): wire accessTokenFactory and prevent 401 reconnection storm (Closes #298)
- fix(trackers): parse BEP 15 error string in UdpTrackerProvider (Closes #165)
- fix(categories): wire category SavePath override to torrent storage and support string category in setcategory (Closes #270)
- fix(scripts): remove duplicate script execution and fix env vars and path resolution (Closes #157)
- fix(shortcuts): suppress global hotkeys during open modals and prevent Escape event leakage (Closes #218)
- fix(ui): resolve NaN comparator breakdown and invert inactive ETA in TorrentTable (Closes #364)
- fix(ci): fix indentation, binary character encoding, and test string formatting
- fix(trackerserver): support BEP 7 IPv6 compact peers and peers6 key (Closes #241)
- fix(utp): implement dynamic RTO calculation and exponential backoff (Closes #271)
- fix(arrintegration): support query/bearer auth and case-insensitive ArrType (Closes #185)
- fix(notifications): escape reserved MarkdownV2 characters in Telegram notifications (Closes #324)
- fix(indexers): decode HTML entities and CDATA references in release titles (Closes #363)
- fix(palette): add global Pause/Resume/Turtle actions and fix navigation conflicts (Closes #357)
- fix(signalr): broadcast TaskCompleted and CommandCompleted events and invalidate SystemTasks queries (Closes #299)
- fix(torrents): cascade delete TorrentMediaMetadata and add delete confirmation modal (Closes #224)
- fix(mediaenrichment): prevent DeleteLocalFile from deleting files outside AppData (Closes #171)
- fix(speedschedule): enforce rule priority precedence in SpeedScheduler and WeeklyCalendar (Closes #279)
- fix(restore): prevent premature live config overwrite before database swap (Closes #183)
- fix(emulation): resolve duplicate torrent mismatch, label clearing, and SavePath mapping in TransmissionRpcController (Closes #186)
- fix(notifications): handle string boolean in EmailNotificationSender SSL settings and use Category fallback (Closes #184)
- fix(update): prevent synchronous network blocking and handle rate limits in UpdateService (Closes #187)
- fix(proxy): add dynamic handler reconfiguration to HttpTrackerProvider (Closes #214)
- fix(lpd): implement multihomed interface multicast and client cookie loopback filtering (Closes #400)
- fix(sqlite): remove Cache=Shared from connection string to restore WAL concurrency (Closes #155)
- fix(restore): prevent cross-device link failure in MainDatabase.ApplyPendingRestore (Closes #182)
- fix(torrents): batch queue reordering and stabilize sort order on collision (Closes #202)
- fix(peers): release halfOpenSemaphore immediately after handshake (Closes #228)
- fix(seeding): prevent SeedingTime incrementing during download and debounce events (Closes #199)
- fix(peers): prevent concurrent modification and enforce per-torrent limits in ConnectionManager (Closes #164)
- fix(lpd): prevent broadcasting and ingesting private torrents (Closes #163)
- fix(trackermetrics): record delta bytes instead of compounding cumulative totals (Closes #162)
- fix(utp): wire inbound uTP connections to PeerServer session loop (Closes #264)
- fix(history): exclude active and seeding torrents from retention pruning (Closes #236)
- fix(utp): bound out-of-order packet buffer and match send/receive connection IDs (Closes #272)
- fix(trackers): implement BEP 15 exponential retransmission and non-blocking sockets (Closes #409)
- fix(utp): parse BEP 29 extension headers and SACK bitmask in uTP decoder (Closes #268)
- fix(trackers): support BEP 15 compact 18-byte IPv6 peer list parsing (Closes #411)
- fix(deluge): resolve dynamic free space and parse save_path in add_torrent (Closes #297)
- fix(notifications): enforce Discord embed limits and dynamic colors (Closes #407)

### 🔧 Maintenance & Improvements
- test(webhook): fix automation webhook test and update service test isolation
- test(webhook): update webhook integration test to expect unhandled event type
- style: format index.html and analytics.ts with prettier
- perf(trackers): implement BEP 15 UDP connection ID caching with 60s expiry (Closes #408)
- security(automation): enforce script sandboxing, SSRF guards, and strict TLS in automation runner (Closes #513)
- perf(encryption): configure 160-bit DH private key length per BEP 8 (Closes #429)
- security(extraction): enforce Zip-Slip path sanitization and disk space verification (Closes #492)

## [v1.6.9](https://github.com/dmzoneill/Seedarr/releases/tag/v1.6.9) - 2026-09-18

### 🐛 Bug Fixes
- fix(mediainspection): restrict ContainerFormat to recognized media container extensions (Closes #174)
- fix(diskspace): resolve Linux mount points in DiskSpaceCheck via IDiskSpaceService (Closes #172)
- fix(onboarding): mark GettingStartedModal as completed on finish (Closes #219)
- fix(ui): prevent permanent log silencing after Clear in SystemLogs (Closes #194)
- fix(i18n): support defaultValue in options object and fallback rendering (Closes #203)
- fix(ui): prevent permanent event silencing after Clear in SystemEvents (Closes #238)
- fix(fastresume): protect fastresume files against truncation via atomic write (Closes #404)
- fix(seeding): eliminate execution-time drift in SeedingEngine tick loop (Closes #393)
- fix(theme): persist toggleTheme to localStorage and resolve system preference (Closes #216)

## [v1.6.8](https://github.com/dmzoneill/Seedarr/releases/tag/v1.6.8) - 2026-09-17

### 🐛 Bug Fixes
- fix(indexers): prevent ArgumentNullException on null ApiKey and attach header (Closes #179)
- fix(torrents): fix ratio calculation when Downloaded is 0 and harmonize SpeedPolicy (Closes #198)
- fix(ui): resolve formatRelativeTime future timestamp and add running indicator (Closes #315)

## [v1.6.7](https://github.com/dmzoneill/Seedarr/releases/tag/v1.6.7) - 2026-09-17

### ✨ Features
- feat(simulation): enforce swarm physical speed ceilings, zero-leecher upload suppression, and warm-up ramp curves (#433)
- feat(simulation): implement tracker warning circuit breaker, monotonic byte safeguards, and statistical announce interval jitter (#435)
- feat(tags): implement rule-based AutoTaggerService for dynamic media classification and tracker domain matching
- feat(tags): implement tag-based seeding policies, bandwidth limits, color customization, and bulk tag endpoints
- feat(relocation): implement resilient cross-filesystem file mover with EXDEV copy-verify-delete fallback and progress reporting (Closes #451)
- feat(streaming): implement playback head sliding window piece picking for HTTP 206 live media preview (Closes #462)
- feat(pieces): implement first-and-last piece prioritization for MP4/MKV container metadata discovery and preview (Closes #461)
- feat(webseed): implement BEP 17 Hoffman-style HTTP seeding and dual BEP 17/19 metainfo parsing
- feat(pieces): implement strict sequential piece picking with rarest-first swarm balancing (Closes #460)
- feat(queue): implement category-level concurrency slot limits and reserved download slots (Closes #442)
- feat(dht): implement BEP 42 IP-based Node ID verification and persistent DHT state (Closes #445)
- feat(trackerserver): implement passkey URL routing (/announce/{passkey}) and user access authorization (Closes #456)
- feat(bandwidth): implement Weighted Fair Queueing (WFQ) with minimum transmission floor to prevent low-priority swarm starvation (Closes #444)
- feat(dht): implement DHT routing table state persistence (dht.dat) and redundant bootstrap fallback (Closes #449)
- feat(arr): implement ReadarrConnection provider, API v1 history routing, and book/comic metadata modeling (Closes #458)
- feat(choking): implement rolling 20-second download rate estimation for Tit-For-Tat reciprocity and seeder upload reception rate sorting (Closes #465)
- feat(extraction): implement ArchiveExtractorService for multi-part scene archives (Closes #491)
- feat(choking): implement 30-second optimistic unchoke cycle with 3-round persistence, 3x new-peer bias, and dynamic slot reallocation (Closes #466)
- feat(setup): persist onboarding completion in backend configuration and enforce initial admin credential complexity (Closes #469)
- feat(protocol): implement bidirectional Interested and NotInterested wire state synchronization based on piece availability (Closes #468)
- feat(notifications): implement Slack Block Kit layouts, Gotify markdown/priority rendering, failover channel routing, and secret token log redaction (Closes #464)
- feat(indexers): implement dynamic Custom Formats scoring engine and quality definition size boundary validation (Closes #484)
- feat(arr): implement multi-episode and season pack parsing, movie edition extraction, sample file exclusion, and hardlink filesystem boundary detection (Closes #481)
- feat(extensions): implement BEP 10 extended handshake negotiation and m-dictionary dynamic mapping (Closes #477)
- feat(media): implement comprehensive ReleaseTitleParser with anime bracket stripping, year separation, and false-positive scene tag protection (Closes #483)
- feat(telemetry): implement selectable time-window ranges with LTTB downsampling and memory-safe ring buffer (Closes #475)
- feat(media): implement direct TMDb and IMDb metadata provider with rate limiting, exponential backoff, and database caching (Closes #485)
- feat(arr): implement WhisparrConnection provider, adult metadata schema, and Whisparr webhook lifecycle handlers (Closes #482)
- feat(ui): implement interactive multi-file PieceMapGrid with rarity heatmaps and file boundary overlays (Closes #526)
- feat(fast): implement leecher-side choke bypass for received AllowedFast pieces to bootstrap block acquisition (Closes #488)
- feat(pex): implement PexService background delta engine with per-peer connection tracking and self-exclusion (Closes #499)
- feat(telemetry): implement compressed RLE bitmask API and SignalR streaming for real-time piece map visualization (Closes #524)
- feat(table): implement arrow key navigation, contiguous range selection, and hotkey actions in TorrentTable (Closes #494)
- feat(pex): filter upload-only seeders (flag 0x02) from candidate pool when torrent is 100% complete (Closes #497)
- feat(jobs): implement persistent ScheduledTaskHistory table and audit logging in SQLite (Closes #500)
- feat(fast): implement SuggestPiece broadcast for warm cache sharing, AllowedFast set calculation, and HaveAll/HaveNone negotiation in FastExtension (BEP 6) (Closes #490)
- feat(jobs): implement task cancellation, timeout enforcement for manual executions, and abort endpoints (Closes #502)
- feat(utp): implement Fast Retransmit on 3 duplicate ACKs in UtpConnection (Closes #504)
- feat(discord): implement Discord Interactions endpoint with Ed25519 cryptographic signature verification and PING/PONG lifecycle (Closes #509)
- feat(discord): implement Discord Slash Commands engine, interactive button components, and Discord-compliant pagination guards (Closes #510)
- feat(remotepath): implement multi-strategy caller host resolution with reverse proxy headers, CIDR subnets, and wildcard fallback (Closes #523)
- feat(telegram): implement Telegram Webhook endpoint with X-Telegram-Bot-Api-Secret-Token validation and long-polling fallback worker (Closes #511)
- feat(indexers): implement Torznab/Newznab specialized query dispatch and expand EpisodicParser (Closes #520)
- feat(telegram): implement interactive inline keyboards, callback query answering, and authorized slash command dispatcher (Closes #512)
- feat(email): implement responsive HTML email templates with multipart/alternative fallback, inline CSS, and rich torrent metadata (Closes #516)
- feat(remotepath): implement interactive path translation dry-run test API and validation modal in DownloadClientsTab (Closes #525)
- feat(peers): implement atomic connection slot reservation to enforce MaxPerTorrentConnections and prevent cross-torrent starvation (#531)
- feat(downloadclients): implement batch import and unified progress reporting for multi-client migrations (Closes #535)
- feat(scripts): implement interactive custom script testing API with stderr capture and diagnostics modal in CustomScriptsTab (Closes #529)
- feat(downloadclients): implement continuous live state reconciliation, progress tracking, and status mapping in DownloadClientSyncService (Closes #532)
- feat(choking): implement dynamic upload slot allocation based on upload bandwidth limit (Bram Cohen / libtorrent model) (Closes #533)
- feat(diskspace): implement emergency low disk space auto-pause and pre-flight capacity validation to prevent SQLite database corruption (#536)
- feat(watchfolder): implement subfolder category mapping and watchfolder path validation (Closes #549)
- feat(metadata): implement pipelined MagnetMetadataDownloader with multi-peer piece scheduling and timeout fallback (Closes #561)
- feat(datastore): implement transactional InsertMany in BasicRepository (#554)
- feat(metadata): implement synthetic metadata generation for zero-file and single-file fake seeder simulation (Closes #562)
- feat(metadata): implement BEP 9 Reject message framing and inbound request handler for incomplete swarms (Closes #559)
- feat(mse): implement non-blocking asynchronous ValueTask handshake streaming in MseHandshake
- feat(torrents): detect and filter BEP 47 padding files to prevent disk bloating and UI pollution (Closes #587)
- feat(bencode): prioritize UTF-8 metainfo keys (name.utf-8, path.utf-8) with robust encoding fallback (Closes #588)
- feat(peers): prioritize high-speed LAN peers over WAN sources and protect from FIFO candidate eviction (Closes #597)
- feat(backup): implement automated scheduled BackupJob with BackupCreatedEvent notification dispatch (Closes #603)
- feat(choking): implement dedicated Seeding-Mode choking strategy with round-robin unchoke and anti-seed choking (Closes #613)
- feat(downloadclients): implement RemotePathMapping for path translation between host and download client (Closes #678)
- feat(ingestion): implement pre-flight validation, ingestion options, and debounced search (Closes #696)
- feat(peers): render interactive protocol flag breakdown and geographic latency tooltip (Closes #699)
- feat(downloadclients): implement multi-select missing torrent import and preserve completed progress (Closes #708)
- feat(notifications): add tag and category filtering, enforce trigger validation, and replace window.confirm (Closes #717)
- feat(arr): implement periodic background Arr sync and expose sync interval (Closes #722)
- feat(ui): implement pagination, calendar date formatting, log level filtering, and stack trace modal in SystemEvents (Closes #727)
- feat(network): implement bounded external port reachability test endpoint and UI (Closes #741)
- feat(torrent): add QueuedForChecking status and badge styling
- feat(stack): use dynamic LEECHARR_IMAGE fallback in compose
- feat(compose): replace transmission with leecharr as default download client in podman stack
- feat(ci): add deterministic podman-compose stack with fixed API keys and relative volume paths

### 🐛 Bug Fixes
- fix(ci): resolve final unit test failures and integration setup 500 error
- fix(ci): resolve all remaining unit and integration test failures
- fix(tests): resolve failing unit tests across peers, notifications, seeding, and torrents
- fix(ci): resolve integration ssrf, unit test regressions, and concurrency assertions
- fix(swagger): mark InvalidateBroadcastCache as NonAction to resolve OpenAPI 500 error
- fix(ci): resolve integration swagger 500, test regressions, and unbuffer test output
- fix(ci): resolve test regressions across automation, core services, and webhooks
- fix(di): eliminate bootstrap circular dependencies and suppress recursive event aggregation during startup
- fix(encryption): fix IA buffer size limit to 65535 bytes and pipeline outgoing BitTorrent handshakes in MSE IA payload (Closes #430)
- fix(ci): resolve indentation violations, DHT private IP bypass, and test suite failures
- fix(relocation): implement multi-file atomic rollback on move failure and sanitize Windows reserved filenames (Closes #453)
- fix(relocation): enforce active I/O locking, peer choking, and open file handle teardown during torrent relocation (Closes #452)
- fix(ci): fix dryioc auto registration, history null ref, and unit test assertions
- fix(webseed): enforce RFC 3986 URI percent-encoding for multi-file subpaths and unicode characters
- fix(webseed): handle HTTP 301/302/307/308 redirects with Range header preservation and loop guards
- fix(categories): fix editorconfig left-padding indentation in CategoryService
- fix(dht): fix editorconfig left-padding indentation in DhtSecurity and DhtSecurityTests
- fix(ci): fix super-linter formatting, gitleaks rules, podman stack compose and ChokeManager recursion
- fix(trackerserver): validate BEP 3 mandatory announce parameters and support BEP 23 non-compact dictionary peers (Closes #455)
- fix(dht): enforce strict per-infohash and global storage caps in DhtPeerStore (Closes #447)
- fix(deluge): enforce RFC-compliant JSON-RPC error object structure and support user password authentication in auth.login (Closes #448)
- fix(deluge): implement file priority persistence in core.set_torrent_file_priorities and project files and trackers in torrent status (Closes #450)
- fix(arr): handle Lidarr/Readarr specialized webhook events, multi-disc audio structures, and category destination routing (Closes #459)
- fix(notifications): implement Pushover API specification compliance with priority levels, emergency retry/expire, device/sound routing, and message truncation (Closes #463)
- fix(diagnostics): implement pre-flight directory write permission validation and volume disk space checks for download paths (Closes #470)
- fix(network): detect peer port collisions, enforce non-privileged port validation, and raise health alerts on bind failure (Closes #471)
- fix(transmission): enforce server-side X-Transmission-Session-Id validation and persist speed and ratio limits in session-set (Closes #472)
- fix(framing): resolve TCP stream framing desynchronization on socket timeouts and handle message boundary fragmentation (Closes #478)
- fix(transmission): project sizeWhenDone and fileStats in torrent-get, persist file-wanted and priorities in torrent-set, and query real free-space (Closes #473)
- fix(telemetry): implement dynamic Y-axis scale hysteresis and smooth exponential transition to prevent graph visual jitter (Closes #474)
- fix(automation): implement event recursion guards, DAG cycle detection, and step error containment in VisualPipeline (Closes #514)
- fix(emulation): implement torrents/renameFile in qBittorrent and fix torrent-rename-path in Transmission RPC to update file paths and trigger re-check (Closes #480)
- fix(ui): implement dynamic SVG artwork fallback generator and client-side poster error resilience (Closes #486)
- fix(lifecycle): orchestrate FastResume state persistence, piece buffer flushes, and tracker stopped announcements in AppLifetime.StopAsync (Closes #547)
- fix(fast): eliminate hardcoded HaveAll broadcast when torrent is incomplete (Closes #487)
- fix(fast): handle inbound RejectRequest to decrement in-flight count, restore pending piece block, and avoid request pipeline stalls (Closes #489)
- fix(jobs): implement startup catch-up throttle, staggered jitter, and drift-free NextExecution calculation (Closes #501)
- fix(emulation): synchronize Alternate Speed Mode (Turtle Mode) and dynamic rate limits across qBittorrent, Transmission, and Deluge (Closes #503)
- fix(utp): enforce flow control window limits in UtpConnection (Closes #506)
- fix(emulation): synchronize categories, custom labels, and save paths across qBittorrent, Transmission, and Deluge controllers (Closes #505)
- fix(utp): fix sequence number wrap-around at 0xFFFF in OutOfOrderPacketBuffer and loss calculation (Closes #507)
- fix(utp): implement graceful FIN teardown state machine and bidirectional socket drainage (Closes #508)
- fix(indexers): resolve false-success reset on non-success HTTP status codes and implement jittered backoff with 429 Retry-After and failure threshold (Closes #518)
- fix(remotepath): enforce path segment boundary matching, UNC support, and cross-platform case sensitivity in path translation engine (Closes #522)
- fix(email): resolve multiple-recipient FormatException crash and implement transient SMTP error retries (421/450/451/452) with exponential backoff (Closes #517)
- fix(scripts): implement process group and Windows job object isolation to terminate detached child process trees on timeout (Closes #527)
- fix(scripts): implement bounded concurrency throttling and complete missing lifecycle event hooks in LifecycleScriptEventHandler (Closes #528)
- fix(peers): prevent throughput collapse in RotateConnections by protecting high-throughput seeders from eviction (#530)
- fix(vpn): implement stabilization hysteresis hold-down timer and health verification to prevent interface flapping storms (#542)
- fix(migration): renumber add_is_vpn_paused_to_torrents to 049 to avoid collision with 048
- fix(diskspace): mitigate stale NFS/CIFS mount query hangs and deduplicate container bind-mounts by device ID (#537)
- fix(vpn): pause active torrents and defer tracker announces during VPN outage to avoid tracker timeout bans
- fix(history): resolve concurrent duplicate history entries on rapid status transitions (Closes #540)
- fix(lifecycle): prevent watchdog event storms by adding edge-triggered debouncing for stalled torrents, speed limits, and port forwarding
- fix(network): align NetworkStatusService.LocalSubnets and prevent false offline alarms (#544)
- fix(watchfolder): handle file rename events, ingest .magnet files, and implement post-import .imported marker for non-deleted torrents (Closes #548)
- fix(upnp): eliminate redundant SSDP device discovery during shutdown and enforce bounded teardown timeout
- fix(protocol): implement event=completed and event=stopped announcing on swarm state transitions (BEP 3)
- fix(datastore): implement automated pre-migration database snapshot and safe rollback in DbFactory (#555)
- fix(trackers): isolate failure backoff by info-hash in MultiTrackerManager to prevent cross-torrent tracker poisoning (Closes #551)
- fix(datastore): enforce 30s busy_timeout and foreign_keys during FluentMigrator migration runs
- fix(arr): prevent startup blocking in ArrWebhookRegistration, fix callback URL generation behind reverse proxies, and unregister remote webhooks on disable (Closes #557)
- fix(arr): validate downloadId infohash before creating torrent, fix connection matching in webhooks, and respect EnableAutomaticAdd (Closes #558)
- fix(auth): prevent scheme handler caching collisions and await dynamic OIDC scheme updates in IdentityProviderConfigController (Closes #564)
- fix(arr): extract external IDs in Arr connections and proxy remote MediaCover artwork
- fix(auth): persist DataProtection key ring to AppDataFolder and handle offline IdP discovery during startup (Closes #566)
- fix(mse): implement PrefixedStream Memory overloads and safe inner stream ownership lifecycle
- fix(security): prevent unmanaged memory leaks, log flooding, and disk I/O storms in Kestrel ServerCertificateSelector
- fix(security): populate LAN IP addresses and canonical interfaces in self-signed certificate SAN
- fix(downloadclients): resolve remote Transmission/Deluge .torrent export failures, Deluge daemon connect handshake, and Transmission session concurrency
- fix(security): preserve intermediate CA certificates in X509Chain validation (#576)
- fix(rss): enforce boundary validation, ReDoS regex timeouts, priority ordering, and category resolution in RssRuleController (Closes #573)
- fix(bencode): extract raw byte slice for info-hash computation instead of re-encoding BDictionary (BEP 3)
- fix(indexers): resolve Torznab freeleech detection edge cases, null seeder handling, and timezone-skewed pubDate parsing (Closes #575)
- fix(filesystem): prevent thread blocking and OOM crashes with bounded enumeration and pagination in FileSystemController (Closes #593)
- fix(swarm): prevent false-boost recommendation loops in SwarmAnalysisService (Closes #582)
- fix(traffic): eliminate negative speed multipliers and division by zero in TrafficPolicyEngine (Closes #580)
- fix(health): eliminate false positive warnings in standalone mode and persist dismissed alerts (Closes #584)
- fix(health): publish HealthIssueEvent on status transitions to trigger notification webhooks and automations (Closes #585)
- fix(peers): eliminate unbounded memory leak in PeerDiscoveryService by evicting deleted torrents and stale candidates (Closes #595)
- fix(filesystem): resolve Linux and Docker mount points correctly in FreeSpaceService and DirectoryBrowser (Closes #594)
- fix(backup): set busy timeout and execute WAL checkpoint prior to VACUUM INTO in BackupService (Closes #601)
- fix(backup): pre-flight check available disk space before initiating database vacuum staging (Closes #602)
- fix(deluge): project total_remaining and hash in get_torrent_status, and support filesystem paths in web.add_torrents (Closes #600)
- fix(qbittorrent): implement sorting, pagination, and file progress in torrents/info, and complete transfer/info speed limits (Closes #608)
- fix(distribution): prevent truncation drift and enforce exact bandwidth conservation in statistical speed distributors (Closes #605)
- fix(qbittorrent): resolve tag overwriting collisions in client emulation (Closes #609)
- fix(scheduling): fix premature morning activation in SpeedScheduler overnight schedules and handle 24h boundaries (Closes #604)
- fix(distribution): invalidate cached speed distributions on algorithm/spread configuration changes and eliminate array reference leaks (Closes #607)
- fix(update): support SemVer 2.0 prerelease tags in BuildInfo and UpdateService release parsing (Closes #610)
- fix(choking): partition upload slots per torrent to prevent cross-swarm starvation in multi-torrent operations (Closes #614)
- fix(update): reconcile 3-part vs 4-part Version equality in UpdateController.GetUpdates (Closes #611)
- fix(categories): synchronize torrent.Label and publish domain events on category rename/delete, and propagate category limits to torrents (Closes #618)
- fix(update): resolve date-as-URL capture bug in ParseChangelogMarkdown regex (Closes #612)
- fix(choking): prevent optimistic unchoke slot collapse when optimistic peer is promoted to regular unchoke (Closes #616)
- fix(tags): enforce atomic database transactions and uniqueness on tag creation (Closes #622)
- fix(emulation): resolve tag desynchronization in QBit API emulation (Closes #623)
- fix(torrents): enforce case-insensitive collation and normalization on InfoHash to prevent duplicate torrent collisions in SQLite (Closes #619)
- fix(lifecycle): disconnect swarm peer connections and zero active speeds upon torrent Stop and Pause (Closes #626)
- fix(mediaenrichment): implement atomic artwork cache rotation and quarantine corrupted image downloads (Closes #621)
- fix(tags): resolve string-to-ID tag mapping in torrent API and support category filtering in tag manager (Closes #624)
- fix(peers): decrement PendingRequestCount on piece request fulfillment to prevent pipeline lockup (Closes #633)
- fix(config): implement atomic config.xml file replacement to prevent config corruption on sudden shutdown (Closes #629)
- fix(lifecycle): prevent newly added torrents with zero progress from entering Seeding state when AutoStart is enabled (Closes #625)
- fix(torrents): enforce atomic uniqueness on InfoHash and optimize SortOrder calculation in TorrentService.Add (Closes #627)
- fix(config): eliminate asterisk masking collision and secret loss in ConfigFileProvider (Closes #631)
- fix(torrents): enforce sequential execution of torrent piece verification to prevent disk thrashing and SQLite lock contention (Closes #630)
- fix(config): enforce cross-field min-max bounds validation in ConfigService (Closes #632)
- fix(peers): evict zero-count IP entries from _connectionsPerIp to prevent unbounded memory leaks and false throttling (Closes #635)
- fix(utp): fix connection_id send/recv offset calculation (Closes #648)
- fix(dht): replace global query rate limiter with per-IP token bucket to prevent denial of service of DHT lookups (Closes #640)
- fix(utp): prevent silent stream truncation on fragmented packets (Closes #649)
- fix(dht): cap get_peers response values to safe UDP MTU and enforce KRPC error code reporting (Closes #642)
- fix(commands): persist command started status before queueing background worker (Closes #643)
- fix(logging): enable HTTP range streaming and inline display for log files (Closes #644)
- fix(system): detect active database engine and query applied migration version dynamically in system status (Closes #645)
- fix(utp): prevent UtpConnection.Dispose from blocking socket receive loop (Closes #651)
- fix(torrents): merge new tracker tiers and sanitize duplicates during torrent update (Closes #655)
- fix(media): implement LRU disk cache eviction and configurable quota for MediaCover artwork directory (Closes #664)
- fix(torrents): prefix multi-file torrent relative paths with root directory name during parsing (Closes #656)
- fix(magnet): prevent double URL decoding of tracker URLs and enforce hex infohash validation (Closes #657)
- fix(host): prevent Kestrel port collisions during dynamic reconfiguration (Closes #662)
- fix(di): prevent unbounded static type accumulation in ContainerBuilder (Closes #663)
- fix(mediaenrichment): prevent unique constraint violation on IMDB/TMDB duplicate IDs (Closes #667)
- fix(automation): eliminate HttpClient and socket exhaustion in ScriptHttpContext / WebhookService (Closes #668)
- fix(routing): prevent SPA fallback from hijacking missing API routes and enable HTTP response compression (Closes #661)
- fix(automation): dynamic API URL resolution in ScriptApiContext to support HTTPS and custom BindAddress configurations (Closes #669)
- fix(downloadclients): implement bi-directional live state reconciliation, concurrency locking, and status mapping in DownloadClientSyncService (Closes #676)
- fix(automation): truncate LastExecutionLog to prevent SQLite WAL bloat and out-of-memory errors on automation endpoints (Closes #671)
- fix(indexers): correct peer swarm accounting and validate positive seeder/leecher counts (Closes #672)
- fix(trackerserver): implement ConfigSavedEvent handler to dynamic reload ports and network bindings without daemon restart (Closes #674)
- fix(upnp): detect port mapping conflicts, reconcile lease renewal, and guard against gateway deadlock (Closes #680)
- fix(peerlog): normalize infohash casing and sanitize null torrent associations (Closes #682)
- fix(peerlog): replace 24h historical event table with live IConnectionManager query in GetActive (Closes #684)
- fix(peerlog): reconcile disconnection events in topology graph and enforce time-window bounds validation (Closes #685)
- fix(frontend): stabilize D3 force simulation alpha re-heating, persist drag coordinates, and update event closures in PeerMap (Closes #683)
- fix(trackermetrics): calculate SLA latency SLA and eliminate DivisionByZero (Closes #687)
- fix(ui): render missing download polyline, align category chips, and fix empty state in SpeedGraph (Closes #688)
- fix(scheduling): eliminate midnight 60-second throttle blackout, fix 24-hour schedule matching, and resolve 0 KB/s pause vs unlimited conflation (Closes #689)
- fix(trackerboost): eliminate orphaned socket connections and batch repository updates (Closes #693)
- fix(speedschedule): add validation and ArgumentOutOfRangeException prevention in SpeedScheduleService (Closes #690)
- fix(trackerboost): eliminate data races and prevent client blocking in TrackerBoostService (Closes #694)
- fix(torrentindex): resolve Grid View filter desync and resilient batch operations (Closes #695)
- fix(files): normalize mixed path separators and implement directory cascading priority (Closes #698)
- fix(seeding): prevent speed history spikes during torrent resume and normalize sampling (Closes #700)
- fix(milestones): prevent premature Hit & Run achievement unlock on partial seed time (Closes #701)
- fix(arr): prevent empty arrType substring matching and support urlBase in client links (Closes #702)
- fix(history): restore SavePath, Category, and MagnetUrl in ReAdd (Closes #704)
- fix(analytics): guard against Infinity ratio and display true tracker buffer deficit (Closes #703)
- fix(categories): enforce absolute save paths and verify directory write permissions (Closes #709)
- fix(categories): eliminate unbatched N*1 SQLite queries during category rename and deletion (Closes #710)
- fix(categories): enforce single default category invariant and prohibit deleting default category (Closes #712)
- fix(categories): normalize mount point path matching and support unlimited speed overrides (Closes #714)
- fix(settings): reconcile WebUI theme options, persist UI preferences, and initialize logging on boot (Closes #719)
- fix(config): enforce port collision check, validate bind addresses, and fix webhook SSL port (Closes #718)
- fix(arr): support SSL certificate validation bypass, validate URLs, and sanitize sync interval (Closes #723)
- fix(arr): respect EnableAutomaticAdd in sync and webhook processing (Closes #720)
- fix(arr): clean up orphaned remote webhooks on connection update or disable (Closes #721)
- fix(indexers): resolve masked API key testing and enhance indexer configuration (Closes #724)
- fix(ui): require confirmation modal for log deletion, use authenticated blob downloads, and expose host power controls (Closes #728)
- fix(rss): prevent duplicate rule names, map category selector, cascade indexer deletes, and rate limit sync (Closes #725)
- fix(system): bridge scheduled tasks to CommandExecutor, align execution models, and handle missing jobs (Closes #726)
- fix(protocols): dynamically start and stop DHT and LPD on configuration changes (Closes #732)
- fix(downloadclients): add delete confirmation, toast feedback, deluge fields, and port validation (Closes #730)
- fix(bittorrent): align EncryptionMode options, support forced synonym, and validate config (Closes #731)
- fix(settings): guard unsaved settings state and implement SeedGoalReachedAction (Closes #734)
- fix(network): resolve upload slots semantics and add connection bounds validation (Closes #733)
- fix(tags): atomic transactional cascading tag deletion and entity cleanup (Closes #735)
- fix(activity): eliminate spurious zero-speed drops, decouple torrents polling, and handle tab visibility (Closes #736)
- fix(linechart): eliminate SVG distortion, prevent duplicate ticks, and add tag delete confirmation (Closes #737)
- fix(backup): authenticated download, restore exception handling, and archive integrity check (Closes #738)
- fix(network): handle NaN in EncryptionDonut and optimize 24h peer log query (Closes #740)
- fix(update): support SemVer prereleases and detect container runtime (Closes #739)
- fix(network): enrich GetInterfaces API with classification and wire selector in UI (Closes #743)
- fix(simulation): suppress identity rotation on private swarms, validate profiles, and guard simulator (Closes #745)
- fix(trackerserver): stabilize swarm sorting, prevent flicker, and add mutation feedback (Closes #746)
- fix(trackerserver): resolve Prowlarr port collision, align UDP default, and validate bounds (Closes #744)
- fix(diskspace): resolve category save paths, preserve root mount, and guard network drives (Closes #755)
- fix(diskspace): implement edge-triggered health events and publish DiskSpaceRestoredEvent (Closes #756)
- fix(signalr): resolve UrlBase in hub URL, invalidate queries on reconnect, and guard RestControllerWithSignalR (Closes #757)
- fix(apidocs): add clipboard fallback for non-secure HTTP contexts and persist swagger auth (Closes #750)
- fix(signalr): eliminate 401 reconnection storm, handle connecting state, and show disconnected banner (Closes #754)
- fix(auth): prevent form double-submission on Enter key and harmonize error messages (Closes #752)
- fix(ui): enforce directory boundaries in disk matching and clamp usage percentages (Closes #753)
- fix(automation): prevent translation function shadowing causing render crash
- fix(ui): swap toolbar buttons on selection and unify button sizing
- fix(compose): add priority to prowlarr indexer, trigger sync, and fix transmission urlBase
- fix(compose): add api key headers, enable flag and error logging for prowlarr indexer config

### 🔧 Maintenance & Improvements
- style: fix indentation in NotificationEventHandler and NotificationPayloadBuilder
- style: format indentation to multiples of 4 for editorconfig compliance
- style: fix 4-space indentation in MediaCoverController and DiskSpaceService
- style: fix left-padding spaces to conform to editorconfig multiples of 4
- security(encryption): enforce strict single-bit crypto_select validation and prevent downgrade attacks in RequireEncrypted mode (Closes #431)
- perf(encryption): optimize RC4 stream cipher with in-place unrolled processing to reduce CPU overhead on high-speed transfers (Closes #432)
- security(trackers): align HTTP announce parameter ordering, case-sensitive escaping, and 32-bit key injection with emulated client profiles
- security(trackerserver): enforce torrent registration checks in private tracker mode for HTTP and UDP announces
- perf(webseed): implement piece-level HTTP range chunking and persistent connection pooling
- perf(queue): implement atomic BatchMoveQueue to eliminate N*M database lock thrashing in QBittorrent and Transmission RPC (Closes #443)
- perf(dht): replace O(N log N) full-table sorting in GetClosestNodes with prefix-tree K-bucket traversal (Closes #446)
- perf(trackerserver): implement in-memory scrape caching and concurrency limits to prevent lock starvation (Closes #457)
- perf(choking): implement choke hysteresis margin to prevent TCP slow-start resets and connection churn (Closes #467)
- security(handshake): verify incoming handshake infoHash against active torrent registry before allocating connection resources (Closes #476)
- a11y(telemetry): implement accessible progressbars, ARIA live region status badges, and screen reader announcements for torrent state changes (Closes #495)
- a11y(modals): implement LIFO modal stack manager for Escape hierarchy and focus trapping across nested dialogs (Closes #493)
- security(pex): enforce strict 60-second inbound rate limit and flood protection per peer connection (BEP 11) (Closes #496)
- security(pex): sanitize inbound PEX addresses against bogons, loopback, private ranges, and non-standard ports (Closes #498)
- security(vpn): implement SO_BINDTODEVICE and IP_UNICAST_IF socket options to enforce strict interface-level routing (#541)
- perf(logging): implement bounded Channel batching in SQLiteTarget to eliminate disk I/O bottlenecks during trace logging (Closes #534)
- perf(history): add missing TorrentId index to DownloadHistory, composite index to TorrentEventLogs, and implement batched chunked pruning (Closes #538)
- perf(diskspace): implement TTL caching for disk space queries and decouple event dispatching from GET endpoint (#539)
- perf(trackermetrics): implement periodic snapshot pruning and asynchronous SQLite batching for tracker metrics (#553)
- security(trackers): prohibit multi-tier and multi-tracker concurrent announces on private torrents (BEP 27) (Closes #552)
- security(metadata): enforce 10 MiB upper bound limit and SHA-1 cryptographic validation on reassembled metadata (Closes #560)
- security(auth): encrypt OIDC client secrets at rest and eliminate plaintext storage in IdentityProviderDefinition (Closes #563)
- perf(mse): precompute SKEY HASH('req2', infoHash) lookup table to achieve O(1) inbound torrent matching
- perf(arr): resolve N+1 remote HTTP query flood, synchronous request thread blocking, and 50-event history truncation in ArrMetadataEnricherService
- perf(mse): implement pre-computed Diffie-Hellman ephemeral keypair pool to eliminate handshake CPU spikes
- security(simulation): dynamically synchronize client profile User-Agent with tracker announces and BEP 10 identity (Closes #577)
- perf(downloadclients): eliminate socket exhaustion, pool HttpClient instances, and cache qBittorrent session authentication
- security(simulation): establish immutable per-torrent client identity sessions to prevent mid-swarm profile switching (Closes #578)
- security(filesystem): enforce root boundaries, sensitive directory blacklists, and hidden file filtering in FileSystemController (Closes #592)
- perf(health): implement TTL caching, timeout guards, and state-change logging in HealthCheckService (Closes #583)
- security(transmission): prevent catastrophic mass deletion and state corruption in torrent-remove and mutation endpoints (Closes #590)
- security(bencode): enforce strict 10 MiB stream and depth recursion limits in BencodeParser (Closes #589)
- perf(transmission): eliminate N+1 database queries, resolve socket leaks, and support filesystem paths in Transmission RPC (Closes #591)
- perf(peers): implement atomic in-flight reservations and prevent concurrent duplicate connection attempts (Closes #596)
- security(deluge): prevent whole-instance pause/resume toggle bypass in Deluge API (Closes #599)
- security(lpd): enforce private RFC 1918 / RFC 4193 subnet validation on incoming LPD multicast datagrams (Closes #598)
- security(distribution): sanitize priority weights to prevent divide-by-zero, NaN, and negative speed allocations (Closes #606)
- perf(categories): eliminate N+1 database queries in category operations and validate uniqueness and save paths (Closes #617)
- perf(choking): immediately reallocate freed upload slots upon NotInterested transitions (Closes #615)
- security(mediacover): enforce XML entity escaping and Content-Security-Policy on SVG artwork serving (Closes #620)
- perf(torrents): throttle and coalesce SignalR torrent state broadcasts in RestControllerWithSignalR (Closes #628)
- security(peers): gate incoming listener handshakes with half-open limits to prevent connection slot exhaustion (Closes #634)
- security(dht): verify response sender endpoint against pending query target to prevent transaction ID spoofing and announce hijacking (Closes #639)
- security(dht): enforce IP and subnet diversity in RoutingTable to prevent Sybil and Eclipse attacks (Closes #641)
- perf(peers): eliminate per-message N+1 SQLite full table scans in PeerServer.HandleMessage (Closes #636)
- perf(utp): prevent thread starvation and fuel loss by eliminating Task.Delay(1) busy-wait in send/receive loops (Closes #650)
- security(auth): prevent password bypass via API key in username and eliminate key reflection in claims (Closes #637)
- security(protocol): align BEP 10 extension handshake and handshake reserved bytes with client profile (Closes #647)
- security(simulation): replace flawed all-numeric Peer ID generation with client-specific alphanumeric entropy encoding (Closes #646)
- security(csrf): fix authentication cookie samesite and secure attributes in TokenService (Closes #652)
- security(auth): harden IsLocalUrl against protocol-relative and backslash bypasses (Closes #638)
- perf(notifications): replace unbounded Task.WhenAll with queued channel dispatcher in NotificationService (Closes #660)
- perf(rpc): eliminate O(N log N) pruning allocations in RpcSessionManager (Closes #654)
- security(host): fix apex domain matching in HostHeaderMiddleware (Closes #653)
- perf(torrents): replace synchronous database queries in torrent loop (Closes #658)
- security(notifications): enforce RFC 1918 private IP blocking in WebhookService and NotificationService (Closes #659)
- perf(mediaenrichment): replace synchronous blocking HTTP calls with parallel throttled batches (Closes #666)
- security(mediacover): prevent internal server filesystem path disclosure in MediaMetadataResource API (Closes #665)
- security(indexers): prevent SSRF and API key leakage in Torznab URL discovery (Closes #670)
- security(trackerserver): bind connection ID to client IP and prohibit spoofing (Closes #673)
- security(proxy): prevent local DNS leaks by enforcing SOCKS5 remote hostname resolution (Closes #679)
- perf(trackerserver): replace single-byte NetworkStream reads with pooled buffer (Closes #675)
- perf(trackerserver): eliminate full-table DB scans during tracker announce processing (Closes #677)
- security(network): validate external IP extraction and guard against SSRF (Closes #681)
- security(trackerboost): prevent private torrent infohash leakage in external client injection (Closes #691)
- perf(trackermetrics): paginate and bound historical telemetry logs (Closes #686)
- security(trackerboost): prevent private tracker leaks and validate swarm eligibility (Closes #692)
- security(trackers): prohibit adding public trackers to private torrents and allow tier editing (Closes #697)
- security(downloadclients): extract IsPrivate flag and prohibit boosting private swarms (Closes #706)
- perf(downloadclients): eliminate O(N) client queries in batch import (Closes #707)
- security(history): sanitize CSV formula injection and stream full export (Closes #705)
- security(auth): enforce SSRF protections on IdP metadata and validate ProviderId (Closes #711)
- security(notifications): implement credential masking, secret preservation, and schema discovery (Closes #716)
- security(auth): enforce RoleMappingRules in OIDC callback and add API key regeneration confirmation (Closes #713)
- security(notifications): prevent SSRF in email sender and harden custom script testing (Closes #715)
- security(downloadclients): prevent SSRF in test, preserve asterisk passwords, and unify factory (Closes #729)
- security(network): prevent silent fallback to 0.0.0.0 and fail closed on unplumbed interface (Closes #742)
- security(trackerserver): partition rate limiting per swarm and eliminate UDP reflection amplification (Closes #748)
- security(auth): configure ForwardedHeaders and scope auth cookie path (Closes #751)
- security(scripts): sanitize environment variables and enforce timeout bounds (Closes #747)
- security(swagger): gate swagger endpoints behind authentication and enforce frame-ancestors CSP (Closes #749)

## [v1.6.6](https://github.com/dmzoneill/Seedarr/releases/tag/v1.6.6) - 2026-09-14

### ✨ Features
- feat(automation): link metrics to events and enable end-to-end event propagation
- Implement History, Tasks, Logging, Backup & Validation (fixes #142, #136, #144, #143, #137, #133, #134)

### 🐛 Bug Fixes
- fix(core): resolve AddDriveInfo overload reflection ambiguity and handle unconfigured choke manager in PeerServer
- fix(core): restore cache shared, overload AddDriveInfo and HandleMessage, and trigger immediate scheduler execution
- fix: resolve peer message overload, speed scheduler zero limits and getting started modal title
- fix(host): use DownloadSpeed and UploadSpeed in AppLifetime watchdog
- fix(host): correct watchdog ITorrentService and IConfigService method references
- fix(core): remove unused using in PeerConnection
- fix: harmonize SignalR events, dynamic versioning, and cleanup History component (#127, #129, #130)
- fix(emulation): implement share limits, file/piece prio, and separate categories and tags
- fix(indexers): implement search, pagination, freeleech parsing, health backoff, and magnet download (#139, #152)
- fix(webhook): resolve Polly retry overload signature in WebhookDispatcher
- Fix automation pipeline, duplicate script execution, interpreter resolution, process reaping, and notifications

### 🔧 Maintenance & Improvements
- style: fix indentation in SpeedScheduler
- style: fix indentation in AutomationService
- style: fix StyleCop and unused imports in SignalR components
- chore: ignore patch*.js and update*.js
- chore: remove scratch patch python scripts and ignore patch*.py
- style: format left-padding indentations for editorconfig compliance
- Update retry handler in WebhookDispatcher
- style(ui): standardize border theme variables and card outlines across all components

## [v1.6.5](https://github.com/dmzoneill/Seedarr/releases/tag/v1.6.5) - 2026-09-13

### ✨ Features
- feat(api): add atomic bulk torrent action endpoint
- feat(ui): enhance real-time reconnection, table multi-selection, category filters, and folder browsing in seedarr
- feat(i18n): comprehensively internationalize 100% of automation page strings and catalog

### 🔧 Maintenance & Improvements
- style(ui): align AddTorrentPage and TorrentDetails headers with Automation style
- style(ui): unify all page headers and layout with Automation header design

## [v1.6.4](https://github.com/dmzoneill/Seedarr/releases/tag/v1.6.4) - 2026-09-13

### ✨ Features
- feat(i18n): internationalize entire automation pipeline page and register automation translation catalog

### 🔧 Maintenance & Improvements
- style: remove trailing whitespace in AutomationPage
- chore: remove scratch scripts

## [v1.6.3](https://github.com/dmzoneill/Seedarr/releases/tag/v1.6.3) - 2026-09-13

### ✨ Features
- feat(ui): complete parity for automation actions, rich r-values, and dry-run simulator
- feat(automation): add media, swarm, rich webhook, and flow control step actions
- feat(automation): add media, swarm, rich webhook, and flow control step actions

### 🔧 Maintenance & Improvements
- chore: remove scratch scripts

## [v1.6.2](https://github.com/dmzoneill/Seedarr/releases/tag/v1.6.2) - 2026-09-13

### 🔧 Maintenance & Improvements
- style(automation): inline condition builder row for L-value, operator, and R-value

## [v1.6.1](https://github.com/dmzoneill/Seedarr/releases/tag/v1.6.1) - 2026-09-13

### 🐛 Bug Fixes
- fix(automation): type-aware condition builders with boolean/numeric operators and live previews

### 🔧 Maintenance & Improvements
- style: fix indentation in YamlScriptRunner to adhere to editorconfig

## [v1.6.0](https://github.com/dmzoneill/Seedarr/releases/tag/v1.6.0) - 2026-09-13

### ✨ Features
- feat(automation): implement full suite of step actions across backend runners and visual builder
- feat(automation): add type-aware condition builder with boolean, enum, and lazy custom matchers
- feat(automation): expand subscription triggers and alert events across lifecycle

## [v1.5.8](https://github.com/dmzoneill/Seedarr/releases/tag/v1.5.8) - 2026-09-13

### 🐛 Bug Fixes
- fix(ui): use standard SVG AutomationIcon to align sidebar navigation text
- fix(ui): expand code editor textarea with full width, monospace typography, minHeight, and tab indentation
- fix(ui): polish automation modal layout, form controls, padding, and field sizing

## [v1.5.7](https://github.com/dmzoneill/Seedarr/releases/tag/v1.5.7) - 2026-09-13

### ✨ Features
- feat(ui): add visual pipeline editor, point-and-click step builder, and run history viewer
- feat(automation): add DSL scripting engine, marketplace, expanded event triggers, and system/api contexts

### 🐛 Bug Fixes
- fix(ui): eliminate modal background transparency and enforce solid theme background with backdrop blur
- fix(automation): use string concatenation for YAML templates to satisfy both yaml parser and editorconfig
- fix(lint): format yaml string literals to strict 4-space multiple indentation
- fix(lint): format multiline string literals with 4-space indentation for editorconfig

## [v1.5.6](https://github.com/dmzoneill/Seedarr/releases/tag/v1.5.6) - 2026-09-12

### 🐛 Bug Fixes
- fix(container): remove artificial GC heap hard limit percentage

## [v1.5.5](https://github.com/dmzoneill/Seedarr/releases/tag/v1.5.5) - 2026-09-12

### 🔧 Maintenance & Improvements
- perf(runtime): bake workstation concurrent GC and heap limits into MSBuild props and Containerfile

## [v1.5.4](https://github.com/dmzoneill/Seedarr/releases/tag/v1.5.4) - 2026-09-12

### 🐛 Bug Fixes
- fix(host): allow reverse proxy origins for CORS to ensure SignalR real-time websocket connectivity

### 🔧 Maintenance & Improvements
- style(host): fix indentation in Startup.cs to conform to editorconfig

## [v1.5.3](https://github.com/dmzoneill/Seedarr/releases/tag/v1.5.3) - 2026-09-11

### 🐛 Bug Fixes
- fix(ui,update): align getting started modal blur and border with leecharr, embed changelog fallback

### 🔧 Maintenance & Improvements
- style(api): fix indentation in UpdateController for editorconfig compliance

## [v1.5.2](https://github.com/dmzoneill/Seedarr/releases/tag/v1.5.2) - 2026-09-11

### 🐛 Bug Fixes
- fix(core,api): enhance sqlite connection pragmas, backup deletion safety, and category path validation
- fix(ui): persist settings accordion state, prevent signalr listener leaks, and add system action error toasts

### 🔧 Maintenance & Improvements
- chore: trigger CI pipeline
- chore: trigger CI pipeline
- style(frontend): fix indentation in App.tsx for editorconfig compliance
- docs: add CHANGELOG.md, overhaul DOCKER_HUB.md, and expand update ingestion

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

