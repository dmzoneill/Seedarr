import { useCallback } from "react";
import { extractDefaultValue, useI18nStore } from "./i18nStore";
import { SUPPORTED_LANGUAGES, getLanguageMetadata } from "./languages";
import { LocaleCode, TranslationParams } from "./types";

export function useTranslation() {
  const locale = useI18nStore((state) => state.locale);
  const setLocale = useI18nStore((state) => state.setLocale);
  const rawT = useI18nStore((state) => state.t);

  const t = useCallback(
    (key: string, params?: TranslationParams, defaultVal?: string) => {
      const { fallbackDefault, interpolationParams } = extractDefaultValue(
        params,
        defaultVal,
      );
      return rawT(key, interpolationParams, fallbackDefault);
    },
    [rawT, locale],
  );

  const currentLanguage = getLanguageMetadata(locale);

  return {
    t,
    locale,
    setLocale,
    currentLanguage,
    languages: SUPPORTED_LANGUAGES,
  };
}

export function translate(
  key: string,
  params?: TranslationParams,
  defaultVal?: string,
): string {
  const { fallbackDefault, interpolationParams } = extractDefaultValue(
    params,
    defaultVal,
  );
  return useI18nStore.getState().t(key, interpolationParams, fallbackDefault);
}
