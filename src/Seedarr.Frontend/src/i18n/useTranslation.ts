import { useCallback } from "react";
import { extractDefaultValue, useI18nStore } from "./i18nStore";
import { SUPPORTED_LANGUAGES, getLanguageMetadata } from "./languages";
import { LocaleCode, TranslationParams } from "./types";

export type TFunction = {
  (key: string, defaultValue?: string, params?: TranslationParams): string;
  (key: string, params?: TranslationParams, defaultValue?: string): string;
};

export function useTranslation() {
  const locale = useI18nStore((state) => state.locale);
  const setLocale = useI18nStore((state) => state.setLocale);
  const rawT = useI18nStore((state) => state.t);

  const t: TFunction = useCallback(
    (key: string, arg1?: any, arg2?: any) => {
      const { fallbackDefault, interpolationParams } = extractDefaultValue(
        arg1,
        arg2,
      );
      return rawT(key, interpolationParams, fallbackDefault);
    },
    [rawT, locale],
  ) as TFunction;

  const currentLanguage = getLanguageMetadata(locale);

  return {
    t,
    locale,
    setLocale,
    currentLanguage,
    languages: SUPPORTED_LANGUAGES,
  };
}

export function translate(key: string, arg1?: any, arg2?: any): string {
  const { fallbackDefault, interpolationParams } = extractDefaultValue(
    arg1,
    arg2,
  );
  return useI18nStore.getState().t(key, interpolationParams, fallbackDefault);
}
