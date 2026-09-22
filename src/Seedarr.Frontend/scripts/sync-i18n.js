const fs = require("fs");
const path = require("path");
const ts = require("typescript");

const LOCALES_DIR = path.resolve(__dirname, "../src/i18n/locales");

const LOCALE_VARS = {
  en: "en",
  "zh-CN": "zhCN",
  es: "es",
  de: "de",
  fr: "fr",
  pt: "pt",
  ru: "ru",
  it: "it",
  ja: "ja",
  ko: "ko",
  hi: "hi",
  ar: "ar",
  id: "id",
  tr: "tr",
  vi: "vi",
  bn: "bn",
  mr: "mr",
  te: "te",
  ta: "ta",
  ur: "ur",
};

const manualFallbacks = {
  // copilot
  "copilot.welcomeMessage": "Hi! I'm your Seedarr AI Copilot. How can I help you manage your torrents today?",
  "copilot.buttonTitle": "AI Copilot",
  "copilot.title": "AI Copilot",
  "copilot.compact": "Compact view",
  "copilot.expand": "Expand view",
  "copilot.minimize": "Minimize",
  "copilot.tabChat": "Chat",
  "copilot.tabParse": "Release Parser",
  "copilot.tabSecurity": "Security Scanner",
  "copilot.quick": "Quick Actions",
  "copilot.speedTips": "Speed Optimization Tips",
  "copilot.piecePickers": "Analyze Piece Pickers",
  "copilot.vpnSecurity": "Check VPN & Security",
  "copilot.thinking": "Thinking ({provider})...",
  "copilot.inputPlaceholder": "Ask about your torrents, seed ratio, speed optimization...",
  "copilot.rawSceneRelease": "Raw Scene / Torrent Release Name",
  "copilot.rawScenePlaceholder": "e.g. Big.Buck.Bunny.2008.1080p.BluRay.x264-SEEDARR",
  "copilot.deobfuscate": "Parse & Deobfuscate",
  "copilot.fileListToInspect": "Torrent File List to Inspect",
  "copilot.fileListPlaceholder": "Paste file list or paths here (one per line)...",
  "copilot.scanTraps": "Scan for Traps & Malware",
  "copilot.threatDetected": "Potential Threats Detected",
  "copilot.cleanSafe": "Clean & Safe",

  // dashboard
  "dashboard.statusActive": "Active",

  // torrents.detail diag
  "torrents.detail.diagTitle": "AI Swarm & Tracker Diagnostics",
  "torrents.detail.diagCollapse": "Collapse Diagnostics",
  "torrents.detail.diagExpand": "Expand Diagnostics",
  "torrents.detail.diagPromptText": "Analyze swarm health, peer distribution, tracker connectivity, and get recommendations.",
  "torrents.detail.diagDiagnoseSwarm": "Diagnose Swarm Health",
  "torrents.detail.diagAnalyzing": "Analyzing Swarm & Trackers...",
  "torrents.detail.diagSwarm": "Swarm Analysis:",
  "torrents.detail.diagTracker": "Tracker Health:",
  "torrents.detail.diagHealthScore": "Swarm Health Score",
  "torrents.detail.diagDetectedIssues": "Detected Issues",
  "torrents.detail.diagRecommendations": "Recommended Actions",
  "torrents.detail.diagReAnnounceTrackers": "Reannounce to All Trackers",
  "torrents.detail.diagForceRecheck": "Force Recheck Pieces",
  "torrents.detail.diagReEvaluate": "Re-evaluate Swarm",

  // system
  "system.realTimeTelemetry": "Real-Time Telemetry",
  "system.realTime": "Real-time engine metrics, memory allocation, and socket utilization",
  "system.realTimeHardwareUtilization": "Real-Time Hardware Utilization",
  "system.processCpuThreads": "Process CPU & Threads",
  "system.cores": "cores",
  "system.processLoad": "Process Load",
  "system.threads": "Threads:",
  "system.handles": "Handles:",
  "system.threadpoolActive": "ThreadPool Active:",
  "system.uptime": "Uptime:",
  "system.ramManagedGc": "RAM & Managed GC",
  "system.residentSet": "Resident Set",
  "system.workingSet": "Working Set:",
  "system.managedHeap": "Managed Heap:",
  "system.privateBytes": "Private Bytes:",
  "system.gcGen01": "GC Gen 0/1:",
  "system.gcGen2": "GC Gen 2:",
  "system.diskIoCache": "Disk I/O & Cache",
  "system.hitRatio": "Hit Ratio",
  "system.writeThroughput": "Write Throughput",
  "system.cacheAlloc": "Cache Allocated:",
  "system.pendingQueue": "Pending Queue:",
  "system.blocks": "blocks",
  "system.diskRead": "Disk Read:",
  "system.totalWritten": "Total Written:",
  "system.swarmNetworkSockets": "Swarm Network & Sockets",
  "system.dhtNodes": "DHT nodes",
  "system.openSockets": "Open Sockets",
  "system.max": "max",
  "system.tcpUtp": "TCP / uTP:",
  "system.encryptedRc4": "Encrypted RC4:",
  "system.seedsLeechers": "Seeds / Leechers:",
  "system.overheadRatio": "Overhead Ratio:",
  "system.subsystemsOperationalStatus": "Subsystems Operational Status",
  "system.activeModularSubsystems": "Active modular subsystems for indexing, search, trackers, and proxy tunneling.",
  "system.configureSubsystems": "Configure Subsystems",
  "system.provider": "Provider:",
  "system.perTorrentBreakdown": "Per-Torrent Telemetry & Sockets",
  "system.sessionTelemetryPerSwarm": "Detailed session telemetry, protocol overhead, and buffer distribution per swarm.",
  "system.filterTorrents": "Filter torrents by name or hash...",
  "system.swarms": "swarms",
  "system.noActiveTorrentsMatch": "No active torrents match the filter.",
  "system.torrentCategory": "Torrent & Category",
  "system.payloadSpeed": "Payload Speed",
  "system.protocolOverhead": "Protocol Overhead",
  "system.bufferMemory": "Buffer Memory",
  "system.peersSeedsLeech": "Peers (Seeds/Leech)",
  "system.transportCrypto": "Transport / Crypto",
  "system.piecesVerification": "Pieces & Verification",
  "system.availability": "Availability",
  "system.inspect": "Inspect",
  "system.efficiency": "Efficiency",
  "system.inFlight": "in flight",
  "system.connected": "connected",
  "system.seeds": "seeds",
  "system.leeches": "leeches",
  "system.tcp": "TCP",
  "system.utp": "uTP",
  "system.encrypted": "Encrypted",
  "system.pcs": "pcs",
  "system.downloadPayload": "Download Payload",
  "torrents.speed": "Speed",
  "system.memoryBufferPieceCache": "Memory Buffer & Piece Cache",
  "system.piecesInFlight": "pieces in flight",
  "system.piece": "piece",
  "system.swarmCryptoNetwork": "Swarm Crypto & Network",
  "common.eta": "ETA",

  // systemStatus
  "systemStatus.percentUsed": "{percent}% Used",

  // settingsTabs
  "settingsTabs.ai.switchSuccess": "Switched AI provider to {provider}",
  "settingsTabs.ai.switchError": "Failed to switch AI provider: {error}",
  "settingsTabs.notifications.unknownError": "An unknown error occurred",
  "settingsTabs.ai.probeFailed": "Health probe failed: {error}",
  "settingsTabs.ai.resetSuccess": "AI Copilot button position reset to bottom right",
  "settingsTabs.ai.loading": "Loading AI settings...",
  "settingsTabs.ai.engineTitle": "AI Engine Configuration",
  "settingsTabs.ai.engineDescription": "Select and configure the intelligence provider powering Copilot, release parsing, and swarm diagnostics.",
  "settingsTabs.subsystems.statusActive": "Active",
  "settingsTabs.subsystems.probing": "Probing...",
  "settingsTabs.ai.testHealth": "Test Health",
  "settingsTabs.ai.activateProvider": "Activate Provider",
  "settingsTabs.ai.connectionTitle": "Provider Connection Settings",
  "settingsTabs.ai.connectionDescription": "Configure API keys, local URLs, and models for the selected provider.",
  "settingsTabs.ai.ollamaTitle": "Ollama (Local LLM)",
  "settingsTabs.ai.ollamaUrl": "Ollama Endpoint URL",
  "settingsTabs.ai.defaultModelName": "Model Name",
  "settingsTabs.ai.geminiTitle": "Google Gemini (Cloud AI)",
  "settingsTabs.ai.geminiApiKey": "Gemini API Key",
  "settingsTabs.ai.geminiApiKeyPlaceholder": "Enter AIza... API key",
  "settingsTabs.ai.geminiModel": "Gemini Model",
  "settingsTabs.ai.geminiOptions.flash2": "Gemini 2.0 Flash (Fastest, Recommended)",
  "settingsTabs.ai.geminiOptions.flash15": "Gemini 1.5 Flash",
  "settingsTabs.ai.geminiOptions.pro15": "Gemini 1.5 Pro",
  "settingsTabs.ai.onnxTitle": "ONNX Runtime (Embedded)",
  "settingsTabs.ai.onnxPath": "Local Model Path",
  "settingsTabs.ai.uiFeaturesTitle": "UI Features & Overlays",
  "settingsTabs.ai.uiFeaturesDescription": "Enable or disable individual AI features across the Seedarr web interface.",
  "settingsTabs.ai.enableCopilot": "Enable Floating AI Copilot",
  "settingsTabs.ai.enableSearch": "Enable Semantic Smart Search",
  "settingsTabs.ai.enableDiagnostics": "Enable Swarm Health Diagnostics",
  "settingsTabs.ai.resetButtonPosition": "Reset Copilot Button Position",

  // trackerBoost
  "trackerBoost.settings.processedTrackersToast": "Processed {count} unique trackers",
  "trackerBoost.radar.swarmsCountLower": "{count} swarms",
  "trackerBoost.tabs.trackerRadar": "📡 Tracker Radar ({count})",
  "trackerBoost.tabs.activityLogs": "📋 Activity Logs ({count})",

  // keyboard shortcuts
  "keyboardShortcuts.title": "Keyboard Shortcuts",
  "keyboardShortcuts.subtitle": "Quick Navigation & Action Reference",
  "keyboardShortcuts.dismissEsc": "Press Esc to close",
  "modals.keyboardShortcuts.title": "Keyboard Shortcuts",
  "modals.keyboardShortcuts.subtitle": "Quick Navigation & Action Reference",
  "modals.keyboardShortcuts.close": "Close keyboard shortcuts"
};

function loadLocaleFile(filePath) {
  const src = fs.readFileSync(filePath, "utf8");
  const js = ts.transpileModule(src, {
    compilerOptions: { module: ts.ModuleKind.CommonJS },
  }).outputText;
  const mod = { exports: {} };
  new Function("module", "exports", js)(mod, mod.exports);
  return mod.exports.default || mod.exports;
}

function serializeObject(obj, indent = 2) {
  const pad = " ".repeat(indent);
  const closingPad = " ".repeat(indent - 2);
  const lines = [];

  for (const [key, val] of Object.entries(obj)) {
    const safeKey = /^[a-zA-Z_$][a-zA-Z0-9_$]*$/.test(key)
      ? key
      : JSON.stringify(key);
    if (typeof val === "string") {
      lines.push(`${pad}${safeKey}: ${JSON.stringify(val)},`);
    } else if (typeof val === "object" && val !== null && !Array.isArray(val)) {
      lines.push(`${pad}${safeKey}: ${serializeObject(val, indent + 2)},`);
    } else {
      lines.push(`${pad}${safeKey}: ${JSON.stringify(val)},`);
    }
  }
  return `{\n${lines.join("\n")}\n${closingPad}}`;
}

function deepClone(obj) {
  if (typeof obj !== "object" || obj === null) return obj;
  const copy = {};
  for (const [k, v] of Object.entries(obj)) {
    copy[k] = deepClone(v);
  }
  return copy;
}

function syncLocale(enRef, targetLocale, stats = { preserved: 0, backfilled: 0 }) {
  const result = {};

  for (const [key, enVal] of Object.entries(enRef)) {
    const hasTarget = targetLocale && key in targetLocale;
    const targetVal = hasTarget ? targetLocale[key] : undefined;

    if (typeof enVal === "object" && enVal !== null && !Array.isArray(enVal)) {
      const childTarget = typeof targetVal === "object" && targetVal !== null ? targetVal : {};
      result[key] = syncLocale(enVal, childTarget, stats);
    } else if (typeof enVal === "string") {
      if (typeof targetVal === "string" && targetVal.trim() !== "") {
        result[key] = targetVal;
        stats.preserved++;
      } else {
        result[key] = enVal;
        stats.backfilled++;
      }
    } else {
      result[key] = targetVal !== undefined ? targetVal : enVal;
    }
  }

  return result;
}

function countLeafKeys(obj) {
  let count = 0;
  for (const k of Object.keys(obj)) {
    if (typeof obj[k] === "string") count++;
    else if (typeof obj[k] === "object" && obj[k] !== null) count += countLeafKeys(obj[k]);
  }
  return count;
}

function main() {
  console.log("Starting i18n synchronization...");

  // 1. Load en.ts
  const enPath = path.join(LOCALES_DIR, "en.ts");
  const enObj = loadLocaleFile(enPath);

  // 2. Merge missing keys from /tmp/all_missing_keys.json if present
  const missingKeysPath = "/tmp/all_missing_keys.json";
  if (fs.existsSync(missingKeysPath)) {
    const missing = JSON.parse(fs.readFileSync(missingKeysPath, "utf8"));
    for (const [keyPath, val] of Object.entries(missing)) {
      const text = typeof val === "object" && val.def !== undefined ? val.def : val;
      if (typeof text !== "string") continue;
      const parts = keyPath.split(".");
      let cur = enObj;
      for (let i = 0; i < parts.length - 1; i++) {
        const p = parts[i];
        if (!cur[p]) cur[p] = {};
        cur = cur[p];
      }
      cur[parts[parts.length - 1]] = text;
    }
  }

  // 3. Merge manual fallbacks
  for (const [keyPath, val] of Object.entries(manualFallbacks)) {
    const parts = keyPath.split(".");
    let cur = enObj;
    for (let i = 0; i < parts.length - 1; i++) {
      const p = parts[i];
      if (!cur[p]) cur[p] = {};
      cur = cur[p];
    }
    cur[parts[parts.length - 1]] = val;
  }

  // 4. Ensure gettingStarted at root matches modals.gettingStarted
  if (enObj.modals && enObj.modals.gettingStarted) {
    enObj.gettingStarted = deepClone(enObj.modals.gettingStarted);
  }

  // 5. Write back en.ts
  const enTotalKeys = countLeafKeys(enObj);
  const enContent = `const en = ${serializeObject(enObj)};\n\nexport default en;\n`;
  fs.writeFileSync(enPath, enContent, "utf8");
  console.log(`[en] Updated en.ts (Total keys: ${enTotalKeys})`);

  // 6. Synchronize each of the 19 other locales
  const otherLocales = Object.keys(LOCALE_VARS).filter((l) => l !== "en");

  for (const loc of otherLocales) {
    const filePath = path.join(LOCALES_DIR, `${loc}.ts`);
    const varName = LOCALE_VARS[loc];

    let currentObj = {};
    if (fs.existsSync(filePath)) {
      currentObj = loadLocaleFile(filePath);
    }

    const stats = { preserved: 0, backfilled: 0 };
    const syncedObj = syncLocale(enObj, currentObj, stats);

    // Keep root gettingStarted aligned with modals.gettingStarted
    if (syncedObj.modals && syncedObj.modals.gettingStarted) {
      syncedObj.gettingStarted = deepClone(syncedObj.modals.gettingStarted);
    }

    const fileContent = `const ${varName} = ${serializeObject(syncedObj)};\n\nexport default ${varName};\n`;
    fs.writeFileSync(filePath, fileContent, "utf8");

    const total = countLeafKeys(syncedObj);
    console.log(
      `[${loc}] Preserved: ${stats.preserved}, Backfilled: ${stats.backfilled}, Total: ${total}`,
    );
  }

  console.log("i18n synchronization completed successfully!");
}

main();
