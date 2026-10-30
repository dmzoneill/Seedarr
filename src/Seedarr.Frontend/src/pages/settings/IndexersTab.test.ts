import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { formatIndexerTestError } from "./IndexersTab";

void describe("IndexersTab: Error Diagnostic Formatting (Issue #308)", () => {
  void it("formats string errors directly", () => {
    assert.equal(
      formatIndexerTestError("Connection timed out"),
      "Connection timed out",
    );
  });

  void it("extracts error message from API response data", () => {
    const errorWithResponse = {
      response: {
        data: {
          message: "HTTP 401: Invalid Torznab API Key",
        },
      },
      message: "Request failed with status code 401",
    };
    assert.equal(
      formatIndexerTestError(errorWithResponse),
      "HTTP 401: Invalid Torznab API Key",
    );
  });

  void it("falls back to error.message if no response data", () => {
    const errorWithMessage = new Error("Failed to fetch: NetworkError");
    assert.equal(
      formatIndexerTestError(errorWithMessage),
      "Failed to fetch: NetworkError",
    );
  });

  void it("falls back to default message if error is null or undefined", () => {
    assert.equal(formatIndexerTestError(null), "Connection failed");
    assert.equal(formatIndexerTestError(undefined), "Connection failed");
    assert.equal(formatIndexerTestError({}), "Connection failed");
  });

  void it("handles HTTP 502 Bad Gateway and proxy errors", () => {
    const error502 = {
      response: {
        data: {
          message: "HTTP 502: Bad Gateway from Jackett upstream proxy",
        },
      },
    };
    assert.equal(
      formatIndexerTestError(error502),
      "HTTP 502: Bad Gateway from Jackett upstream proxy",
    );
  });
});
