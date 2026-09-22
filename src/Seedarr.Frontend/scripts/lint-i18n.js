const fs = require("fs");
const path = require("path");
const ts = require("typescript");

const LOCALES_DIR = path.resolve(__dirname, "../src/i18n/locales");
const SRC_DIR = path.resolve(__dirname, "../src");

const ALL_LOCALES = [
  "en",
  "zh-CN",
  "es",
  "de",
  "fr",
  "pt",
  "ru",
  "it",
  "ja",
  "ko",
  "hi",
  "ar",
  "id",
  "tr",
  "vi",
  "bn",
  "mr",
  "te",
  "ta",
  "ur",
];

function loadLocaleFile(filePath) {
  const src = fs.readFileSync(filePath, "utf8");
  const js = ts.transpileModule(src, {
    compilerOptions: { module: ts.ModuleKind.CommonJS },
  }).outputText;
  const mod = { exports: {} };
  new Function("module", "exports", js)(mod, mod.exports);
  return mod.exports.default || mod.exports;
}

function getNestedValue(obj, keyPath) {
  const parts = keyPath.split(".");
  let cur = obj;
  for (const p of parts) {
    if (!cur || typeof cur !== "object") return undefined;
    cur = cur[p];
  }
  return typeof cur === "string" ? cur : undefined;
}

function collectAllKeys(obj, prefix = "") {
  const keys = [];
  for (const [k, v] of Object.entries(obj)) {
    const fullKey = prefix ? `${prefix}.${k}` : k;
    if (typeof v === "string") {
      keys.push(fullKey);
    } else if (typeof v === "object" && v !== null && !Array.isArray(v)) {
      keys.push(...collectAllKeys(v, fullKey));
    }
  }
  return keys;
}

function getAllSourceFiles(dir, exts = [".ts", ".tsx"]) {
  let files = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (
        entry.name !== "node_modules" &&
        entry.name !== "dist" &&
        entry.name !== ".git" &&
        fullPath !== LOCALES_DIR
      ) {
        files = files.concat(getAllSourceFiles(fullPath, exts));
      }
    } else if (exts.some((ext) => entry.name.endsWith(ext))) {
      files.push(fullPath);
    }
  }
  return files;
}

function runLint() {
  console.log("====================================");
  console.log("       Seedarr i18n Linter          ");
  console.log("====================================\n");

  let hasError = false;

  // 1. Load en.ts as ground truth
  const enPath = path.join(LOCALES_DIR, "en.ts");
  if (!fs.existsSync(enPath)) {
    console.error("ERROR: en.ts not found at", enPath);
    process.exit(1);
  }
  const enObj = loadLocaleFile(enPath);
  const enKeys = collectAllKeys(enObj);
  console.log(`[en] Ground truth loaded with ${enKeys.length} keys.`);

  // 2. Check all 19 other locales for parity
  console.log("\n[1/2] Verifying dictionary structural parity across all 20 locales...");
  for (const loc of ALL_LOCALES) {
    if (loc === "en") continue;
    const locPath = path.join(LOCALES_DIR, `${loc}.ts`);
    if (!fs.existsSync(locPath)) {
      console.error(`  FAIL: Missing locale file for ${loc}`);
      hasError = true;
      continue;
    }
    const locObj = loadLocaleFile(locPath);
    const locKeys = new Set(collectAllKeys(locObj));

    const missingInLoc = enKeys.filter((k) => !locKeys.has(k));
    if (missingInLoc.length > 0) {
      console.error(`  FAIL: [${loc}] missing ${missingInLoc.length} keys:`);
      missingInLoc.slice(0, 10).forEach((k) => console.error(`    - ${k}`));
      if (missingInLoc.length > 10) console.error(`    ... and ${missingInLoc.length - 10} more`);
      hasError = true;
    } else {
      console.log(`  PASS: [${loc}] 100% parity (${locKeys.size}/${enKeys.length} keys)`);
    }
  }

  // 3. Check all codebase references in src/
  console.log("\n[2/2] Scanning codebase for unresolved t() / translate() calls...");
  const sourceFiles = getAllSourceFiles(SRC_DIR);
  const callRegex = /\b(?:t|translate)\(\s*["\x27]([a-zA-Z0-9_.-]+)["\x27]/g;

  const unresolved = [];

  for (const file of sourceFiles) {
    const content = fs.readFileSync(file, "utf8");
    let match;
    while ((match = callRegex.exec(content)) !== null) {
      const key = match[1];
      // Skip dynamic key prefixes ending with "."
      if (key.endsWith(".")) continue;

      const val = getNestedValue(enObj, key);
      if (val === undefined) {
        unresolved.push({
          key,
          file: path.relative(SRC_DIR, file),
        });
      }
    }
  }

  if (unresolved.length > 0) {
    console.error(`\n  FAIL: Found ${unresolved.length} unresolved keys in code:`);
    unresolved.forEach(({ key, file }) => console.error(`    - "${key}" in ${file}`));
    hasError = true;
  } else {
    console.log(`  PASS: All translation calls in ${sourceFiles.length} source files resolve cleanly!`);
  }

  console.log("\n====================================");
  if (hasError) {
    console.error("  i18n Lint FAILED with errors!");
    console.log("====================================");
    process.exit(1);
  } else {
    console.log("  i18n Lint PASSED: 0 errors!");
    console.log("====================================");
    process.exit(0);
  }
}

runLint();
