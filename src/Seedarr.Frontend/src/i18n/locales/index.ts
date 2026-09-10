import { LocaleCode, TranslationDictionary } from "../types";
import en from "./en";
import zhCN from "./zh-CN";
import es from "./es";
import de from "./de";
import fr from "./fr";
import pt from "./pt";
import ru from "./ru";
import it from "./it";
import ja from "./ja";
import ko from "./ko";
import hi from "./hi";
import ar from "./ar";
import id from "./id";
import tr from "./tr";
import vi from "./vi";
import bn from "./bn";
import mr from "./mr";
import te from "./te";
import ta from "./ta";
import ur from "./ur";

export const dictionaries: Record<LocaleCode, TranslationDictionary> = {
  en,
  "zh-CN": zhCN,
  es,
  de,
  fr,
  pt,
  ru,
  it,
  ja,
  ko,
  hi,
  ar,
  id,
  tr,
  vi,
  bn,
  mr,
  te,
  ta,
  ur,
};

export {
  en,
  zhCN,
  es,
  de,
  fr,
  pt,
  ru,
  it,
  ja,
  ko,
  hi,
  ar,
  id,
  tr,
  vi,
  bn,
  mr,
  te,
  ta,
  ur,
};
