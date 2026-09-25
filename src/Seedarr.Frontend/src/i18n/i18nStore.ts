import { create } from "zustand";
import { LocaleCode, TranslationDictionary, TranslationParams } from "./types";
import {
  DEFAULT_LOCALE,
  STORAGE_KEY_LANGUAGE,
  detectBrowserLanguage,
  getLanguageMetadata,
  isSupportedLocale,
} from "./languages";
import { dictionaries } from "./locales";

function getNestedValue(
  obj: TranslationDictionary | undefined,
  path: string,
): string | undefined {
  if (!obj) return undefined;
  const parts = path.split(".");
  let current: any = obj;
  for (const part of parts) {
    if (
      current === undefined ||
      current === null ||
      typeof current !== "object"
    ) {
      return undefined;
    }
    current = current[part];
  }
  return typeof current === "string" ? current : undefined;
}

function interpolate(text: string, params?: TranslationParams): string {
  if (!params) return text;
  let result = text;
  if (Array.isArray(params)) {
    params.forEach((val, idx) => {
      result = result
        .split(`\${${idx}}`)
        .join(String(val))
        .split(`{${idx}}`)
        .join(String(val));
    });
  } else if (typeof params === "object") {
    for (const [key, val] of Object.entries(params)) {
      result = result
        .split(`\${${key}}`)
        .join(String(val))
        .split(`{{${key}}}`)
        .join(String(val))
        .split(`{${key}}`)
        .join(String(val));
    }
  }
  return result;
}

function syncDomAttributes(locale: LocaleCode) {
  if (typeof document === "undefined") return;
  const meta = getLanguageMetadata(locale);
  document.documentElement.lang = locale;
  document.documentElement.dir = meta.rtl ? "rtl" : "ltr";
}

export function extractDefaultValue(
  params?: any,
  defaultVal?: any,
): { fallbackDefault?: string; interpolationParams?: TranslationParams } {
  if (typeof params === "string") {
    if (typeof defaultVal === "object" && defaultVal !== null) {
      return { fallbackDefault: params, interpolationParams: defaultVal };
    }
    return { fallbackDefault: params, interpolationParams: undefined };
  }
  let fallbackDefault = typeof defaultVal === "string" ? defaultVal : undefined;
  let interpolationParams = params;
  if (
    params &&
    typeof params === "object" &&
    !Array.isArray(params) &&
    "defaultValue" in params
  ) {
    fallbackDefault =
      (params as { defaultValue?: string }).defaultValue ?? fallbackDefault;
    const { defaultValue: _, ...rest } = params as Record<string, any>;
    interpolationParams = rest;
  }
  return { fallbackDefault, interpolationParams };
}

export interface I18nState {
  locale: LocaleCode;
  setLocale: (locale: LocaleCode) => void;
  t: (key: string, params?: TranslationParams, defaultVal?: string) => string;
}

const initialLocale: LocaleCode = detectBrowserLanguage();
syncDomAttributes(initialLocale);

export const useI18nStore = create<I18nState>((set, get) => ({
  locale: initialLocale,
  setLocale: (locale: LocaleCode) => {
    if (!isSupportedLocale(locale)) return;
    try {
      localStorage.setItem(STORAGE_KEY_LANGUAGE, locale);
    } catch {
      // ignore
    }
    syncDomAttributes(locale);
    set({ locale });
  },
  t: (key: string, params?: TranslationParams, defaultVal?: string): string => {
    const { fallbackDefault, interpolationParams } = extractDefaultValue(
      params,
      defaultVal,
    );
    const currentLocale = get().locale;
    const currentDict = dictionaries[currentLocale];
    const defaultDict = dictionaries[DEFAULT_LOCALE];

    // 1. Current locale lookup
    let template = getNestedValue(currentDict, key);

    // 2. Fallback to default (en) locale
    if (template === undefined && currentLocale !== DEFAULT_LOCALE) {
      template = getNestedValue(defaultDict, key);
    }

    // 3. Fallback to fallbackDefault or key itself
    if (template === undefined) {
      template = fallbackDefault !== undefined ? fallbackDefault : key;
    }

    return interpolate(template, interpolationParams);
  },
}));
