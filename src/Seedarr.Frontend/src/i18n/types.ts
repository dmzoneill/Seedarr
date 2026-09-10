export type LocaleCode =
  | "en"
  | "zh-CN"
  | "es"
  | "de"
  | "fr"
  | "pt"
  | "ru"
  | "it"
  | "ja"
  | "ko"
  | "hi"
  | "ar"
  | "id"
  | "tr"
  | "vi"
  | "bn"
  | "mr"
  | "te"
  | "ta"
  | "ur";

export interface Language {
  code: LocaleCode;
  name: string;
  nativeName: string;
  flag: string;
  rtl?: boolean;
}

export type TranslationParams = Record<string, string | number> | (string | number)[];

export type TranslationDictionary = {
  [key: string]: string | TranslationDictionary;
};
