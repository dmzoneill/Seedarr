import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { useI18nStore, extractDefaultValue } from "./i18nStore";

describe("i18nStore", () => {
  it("extractDefaultValue should extract default from string parameter", () => {
    const res1 = extractDefaultValue("Default Text");
    assert.equal(res1.fallbackDefault, "Default Text");
    assert.equal(res1.interpolationParams, undefined);

    const res2 = extractDefaultValue("Default Text", { foo: "bar" });
    assert.equal(res2.fallbackDefault, "Default Text");
    assert.deepEqual(res2.interpolationParams, { foo: "bar" });
  });

  it("extractDefaultValue should extract defaultValue property from object", () => {
    const res = extractDefaultValue({ defaultValue: "Custom Default", count: 5 });
    assert.equal(res.fallbackDefault, "Custom Default");
    assert.deepEqual(res.interpolationParams, { count: 5 });
  });

  it("extractDefaultValue should return fallback when defaultValue is string", () => {
    const res = extractDefaultValue(undefined, "String Fallback");
    assert.equal(res.fallbackDefault, "String Fallback");
    assert.equal(res.interpolationParams, undefined);
  });

  it("t should translate key or return fallback default value", () => {
    const store = useI18nStore.getState();
    store.setLocale("en");

    // Known ground truth key
    const val = store.t("common.save", undefined, "Save");
    assert.equal(typeof val, "string");

    // Unknown key with fallback
    const missingKey1 = `test.${"missing"}`;
    const fallback = store.t(missingKey1, undefined, "Fallback Message");
    assert.equal(fallback, "Fallback Message");

    // Unknown key without fallback returns key
    const missingKey2 = `test.${"missing.two"}`;
    const missing = store.t(missingKey2);
    assert.equal(missing, missingKey2);
  });

  it("t should interpolate variables correctly", () => {
    const store = useI18nStore.getState();
    store.setLocale("en");

    const objKey = `test.${"interpolate.obj"}`;
    const textWithObj = store.t(objKey, { name: "Seedarr" }, "Hello {name}");
    assert.equal(textWithObj, "Hello Seedarr");

    const arrKey = `test.${"interpolate.arr"}`;
    const textWithArray = store.t(arrKey, ["Alice", "Bob"], "Users: {0} and {1}");
    assert.equal(textWithArray, "Users: Alice and Bob");
  });

  it("setLocale should ignore unsupported locales", () => {
    const store = useI18nStore.getState();
    store.setLocale("en");
    // @ts-expect-error testing invalid locale
    store.setLocale("invalid-xyz");
    assert.equal(useI18nStore.getState().locale, "en");
  });
});
