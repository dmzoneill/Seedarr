import { describe, it } from "node:test";
import assert from "node:assert/strict";
import {
  STORAGE_KEY_HIDE_GUIDE,
  isValidHttpUrl,
  isValidAbsolutePath,
  validateClientConfig,
  validateIndexerConfig,
  validateArrConfig,
  validateStorageConfig,
} from "./GettingStartedModal";

describe("GettingStartedModal: Storage Constants & URL Validation", () => {
  it("exports correct storage key for suppressing wizard on startup", () => {
    assert.equal(STORAGE_KEY_HIDE_GUIDE, "seedarr_hide_getting_started");
  });

  it("validates HTTP and HTTPS URLs correctly", () => {
    assert.equal(isValidHttpUrl("http://localhost:8080"), true);
    assert.equal(isValidHttpUrl("https://prowlarr.local:9696/api"), true);
    assert.equal(isValidHttpUrl("http://192.168.1.100:8989"), true);

    // Invalid URLs
    assert.equal(isValidHttpUrl(""), false);
    assert.equal(isValidHttpUrl("   "), false);
    assert.equal(isValidHttpUrl("ftp://localhost:21"), false);
    assert.equal(isValidHttpUrl("not-a-valid-url"), false);
    assert.equal(isValidHttpUrl("javascript:alert(1)"), false);
  });

  it("validates absolute filesystem paths correctly", () => {
    // POSIX absolute
    assert.equal(isValidAbsolutePath("/downloads"), true);
    assert.equal(isValidAbsolutePath("/mnt/storage/torrents"), true);
    assert.equal(isValidAbsolutePath("/"), true);

    // Windows absolute
    assert.equal(isValidAbsolutePath("C:\\Downloads"), true);
    assert.equal(isValidAbsolutePath("D:/Torrents/Movies"), true);
    assert.equal(isValidAbsolutePath("\\\\nas\\downloads"), true);

    // Invalid relative or dangerous paths
    assert.equal(isValidAbsolutePath(""), false);
    assert.equal(isValidAbsolutePath("   "), false);
    assert.equal(isValidAbsolutePath("downloads"), false);
    assert.equal(isValidAbsolutePath("./downloads"), false);
    assert.equal(isValidAbsolutePath("../downloads"), false);
    assert.equal(isValidAbsolutePath("/downloads/../etc/passwd"), false);
    assert.equal(isValidAbsolutePath("/downloads\0malicious"), false);
  });
});

describe("GettingStartedModal: Pre-Mutation Input Validation", () => {
  describe("validateClientConfig", () => {
    it("rejects missing or empty host", () => {
      const res1 = validateClientConfig({ host: "", port: 8080 });
      assert.equal(res1.valid, false);
      assert.ok(res1.error?.includes("Host is required"));

      const res2 = validateClientConfig({ host: "   ", port: 8080 });
      assert.equal(res2.valid, false);
      assert.ok(res2.error?.includes("Host is required"));

      const res3 = validateClientConfig({ port: 8080 });
      assert.equal(res3.valid, false);
      assert.ok(res3.error?.includes("Host is required"));
    });

    it("rejects out-of-range or non-integer port numbers", () => {
      const invalidPorts = [0, -1, 65536, 70000, 8080.5, NaN];
      for (const port of invalidPorts) {
        const res = validateClientConfig({ host: "localhost", port });
        assert.equal(
          res.valid,
          false,
          `Expected port ${port} to fail validation`,
        );
        assert.ok(
          res.error?.includes("Port must be an integer between 1 and 65535"),
        );
      }
    });

    it("accepts valid client configuration", () => {
      const validCases = [
        { host: "localhost", port: 8080 },
        { host: "192.168.1.50", port: 9091 },
        { host: "deluge", port: 8112 },
        { host: "client.local", port: 1 },
        { host: "client.local", port: 65535 },
      ];
      for (const client of validCases) {
        const res = validateClientConfig(client);
        assert.equal(res.valid, true);
        assert.equal(res.error, undefined);
      }
    });
  });

  describe("validateIndexerConfig", () => {
    it("rejects empty name or URL", () => {
      const resName = validateIndexerConfig({
        name: "",
        url: "http://prowlarr:9696",
        apiKey: "secret",
      });
      assert.equal(resName.valid, false);
      assert.ok(resName.error?.includes("name is required"));

      const resUrl = validateIndexerConfig({
        name: "Prowlarr",
        url: "",
        apiKey: "secret",
      });
      assert.equal(resUrl.valid, false);
      assert.ok(resUrl.error?.includes("URL is required"));
    });

    it("rejects malformed URLs", () => {
      const res = validateIndexerConfig({
        name: "Prowlarr",
        url: "ftp://prowlarr:9696",
        apiKey: "secret",
      });
      assert.equal(res.valid, false);
      assert.ok(res.error?.includes("valid HTTP or HTTPS address"));
    });

    it("requires API key in live setup mode", () => {
      const res = validateIndexerConfig(
        { name: "Prowlarr", url: "http://prowlarr:9696", apiKey: "" },
        true, // isLive
      );
      assert.equal(res.valid, false);
      assert.ok(res.error?.includes("API Key is required"));
    });

    it("allows empty API key in tour / read-only mode", () => {
      const res = validateIndexerConfig(
        { name: "Prowlarr", url: "http://prowlarr:9696", apiKey: "" },
        false, // tour mode
      );
      assert.equal(res.valid, true);
    });

    it("accepts valid indexer config", () => {
      const res = validateIndexerConfig(
        {
          name: "Prowlarr",
          url: "http://prowlarr:9696",
          apiKey: "test_key_123",
        },
        true,
      );
      assert.equal(res.valid, true);
    });
  });

  describe("validateArrConfig", () => {
    it("rejects empty name or URL", () => {
      const resName = validateArrConfig({
        arrType: "Sonarr",
        name: "",
        url: "http://localhost:8989",
        apiKey: "key",
      });
      assert.equal(resName.valid, false);
      assert.ok(resName.error?.includes("name is required"));

      const resUrl = validateArrConfig({
        arrType: "Sonarr",
        name: "Sonarr",
        url: "",
        apiKey: "key",
      });
      assert.equal(resUrl.valid, false);
      assert.ok(resUrl.error?.includes("URL is required"));
    });

    it("requires API key in live setup mode", () => {
      const res = validateArrConfig(
        {
          arrType: "Sonarr",
          name: "Sonarr",
          url: "http://localhost:8989",
          apiKey: "",
        },
        true,
      );
      assert.equal(res.valid, false);
      assert.ok(res.error?.includes("API Key is required"));
    });

    it("allows empty API key in tour mode", () => {
      const res = validateArrConfig(
        {
          arrType: "Radarr",
          name: "Radarr",
          url: "http://localhost:7878",
          apiKey: "",
        },
        false,
      );
      assert.equal(res.valid, true);
    });

    it("accepts valid Arr configuration", () => {
      const res = validateArrConfig(
        {
          arrType: "Lidarr",
          name: "Lidarr",
          url: "http://localhost:8686",
          apiKey: "valid_lidarr_key",
        },
        true,
      );
      assert.equal(res.valid, true);
    });
  });

  describe("validateStorageConfig", () => {
    it("rejects missing or empty default download path", () => {
      const res1 = validateStorageConfig({ defaultDownloadPath: "" });
      assert.equal(res1.valid, false);
      assert.ok(res1.error?.includes("Default download directory is required"));

      const res2 = validateStorageConfig({});
      assert.equal(res2.valid, false);
      assert.ok(res2.error?.includes("Default download directory is required"));
    });

    it("rejects non-absolute default download path", () => {
      const res = validateStorageConfig({
        defaultDownloadPath: "downloads/data",
      });
      assert.equal(res.valid, false);
      assert.ok(res.error?.includes("must be an absolute path"));
    });

    it("rejects directory traversal sequences in download or completed path", () => {
      const res1 = validateStorageConfig({
        defaultDownloadPath: "/downloads/../etc",
      });
      assert.equal(res1.valid, false);
      assert.ok(res1.error?.includes("cannot contain '..'"));

      const res2 = validateStorageConfig({
        defaultDownloadPath: "/downloads",
        completedPath: "/downloads/completed/../../secret",
      });
      assert.equal(res2.valid, false);
      assert.ok(res2.error?.includes("cannot contain '..'"));
    });

    it("accepts valid storage paths", () => {
      const res = validateStorageConfig({
        defaultDownloadPath: "/downloads",
        completedPath: "/downloads/completed",
      });
      assert.equal(res.valid, true);
      assert.equal(res.error, undefined);
    });
  });
});
