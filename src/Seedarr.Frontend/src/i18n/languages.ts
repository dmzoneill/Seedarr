import { Language, LocaleCode } from "./types";

export const STORAGE_KEY_LANGUAGE = "seedarr_lang";

export const SUPPORTED_LANGUAGES: Language[] = [
  { code: "en", name: "English", nativeName: "English", flag: "🇬🇧" },
  { code: "zh-CN", name: "Chinese (Simplified)", nativeName: "简体中文", flag: "🇨🇳" },
  { code: "es", name: "Spanish", nativeName: "Español", flag: "🇪🇸" },
  { code: "de", name: "German", nativeName: "Deutsch", flag: "🇩🇪" },
  { code: "fr", name: "French", nativeName: "Français", flag: "🇫🇷" },
  { code: "pt", name: "Portuguese", nativeName: "Português", flag: "🇵🇹" },
  { code: "ru", name: "Russian", nativeName: "Русский", flag: "🇷🇺" },
  { code: "it", name: "Italian", nativeName: "Italiano", flag: "🇮🇹" },
  { code: "ja", name: "Japanese", nativeName: "日本語", flag: "🇯🇵" },
  { code: "ko", name: "Korean", nativeName: "한국어", flag: "🇰🇷" },
  { code: "hi", name: "Hindi", nativeName: "हिन्दी", flag: "🇮🇳" },
  { code: "ar", name: "Arabic", nativeName: "العربية", flag: "🇸🇦", rtl: true },
  { code: "id", name: "Indonesian", nativeName: "Bahasa Indonesia", flag: "🇮🇩" },
  { code: "tr", name: "Turkish", nativeName: "Türkçe", flag: "🇹🇷" },
  { code: "vi", name: "Vietnamese", nativeName: "Tiếng Việt", flag: "🇻🇳" },
  { code: "bn", name: "Bengali", nativeName: "বাংলা", flag: "🇧🇩" },
  { code: "mr", name: "Marathi", nativeName: "मराठी", flag: "🇮🇳" },
  { code: "te", name: "Telugu", nativeName: "తెలుగు", flag: "🇮🇳" },
  { code: "ta", name: "Tamil", nativeName: "தமிழ்", flag: "🇮🇳" },
  { code: "ur", name: "Urdu", nativeName: "اردو", flag: "🇵🇰", rtl: true },
];

export const DEFAULT_LOCALE: LocaleCode = "en";

export function isSupportedLocale(code: string): code is LocaleCode {
  return SUPPORTED_LANGUAGES.some((lang) => lang.code === code);
}

export function getLanguageMetadata(code: LocaleCode): Language {
  return (
    SUPPORTED_LANGUAGES.find((lang) => lang.code === code) ||
    SUPPORTED_LANGUAGES[0]
  );
}

export function detectBrowserLanguage(): LocaleCode {
  if (typeof window === "undefined") {
    return DEFAULT_LOCALE;
  }

  // 1. User saved preference in localStorage
  try {
    const saved = localStorage.getItem(STORAGE_KEY_LANGUAGE);
    if (saved && isSupportedLocale(saved)) {
      return saved;
    }
  } catch {
    // ignore localStorage errors
  }

  // 2. Browser language preferences
  const candidateLangs: string[] = [];
  if (navigator.languages && navigator.languages.length > 0) {
    candidateLangs.push(...navigator.languages);
  }
  if (navigator.language) {
    candidateLangs.push(navigator.language);
  }

  for (const rawLang of candidateLangs) {
    if (!rawLang) continue;
    const cleanLang = rawLang.trim();

    // Exact match (e.g. "zh-CN", "en", "es")
    if (isSupportedLocale(cleanLang)) {
      return cleanLang;
    }

    // Case-insensitive exact match
    const lower = cleanLang.toLowerCase();
    const caseMatch = SUPPORTED_LANGUAGES.find(
      (l) => l.code.toLowerCase() === lower
    );
    if (caseMatch) {
      return caseMatch.code;
    }

    // Prefix match (e.g. "es-MX" -> "es", "zh-TW" -> "zh-CN", "de-AT" -> "de")
    const primary = lower.split("-")[0];
    if (primary === "zh") {
      return "zh-CN";
    }
    const prefixMatch = SUPPORTED_LANGUAGES.find(
      (l) => l.code.toLowerCase() === primary
    );
    if (prefixMatch) {
      return prefixMatch.code;
    }
  }

  // 3. Fallback
  return DEFAULT_LOCALE;
}
