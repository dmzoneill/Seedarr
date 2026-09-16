import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { parsePeerFlags, maskIpAddress, getFlagBadgeColor } from "./peerUtils";

describe("peerUtils: parsePeerFlags", () => {
  it("should return an empty array for null, undefined, or empty/whitespace strings", () => {
    assert.deepEqual(parsePeerFlags(null), []);
    assert.deepEqual(parsePeerFlags(undefined), []);
    assert.deepEqual(parsePeerFlags(""), []);
    assert.deepEqual(parsePeerFlags("   "), []);
  });

  it("should parse standard BitTorrent flag string 'DuEHLX'", () => {
    const parsed = parsePeerFlags("DuEHLX");
    assert.equal(parsed.length, 6);

    assert.deepEqual(parsed[0], {
      flag: "D",
      label: "D",
      description: "Interested and Downloading",
    });
    assert.deepEqual(parsed[1], {
      flag: "u",
      label: "u",
      description: "Choked Peer (Peer is interested, but we are choking them)",
    });
    assert.deepEqual(parsed[2], {
      flag: "E",
      label: "E",
      description: "Encrypted Protocol Handshake (MSE/PE)",
    });
    assert.deepEqual(parsed[3], {
      flag: "H",
      label: "H",
      description: "uTP Transport Protocol",
    });
    assert.deepEqual(parsed[4], {
      flag: "L",
      label: "L",
      description: "Local Peer Discovered via LPD",
    });
    assert.deepEqual(parsed[5], {
      flag: "X",
      label: "X",
      description: "Peer Discovered via Peer Exchange (PEX)",
    });
  });

  it("should correctly map all defined standard flags", () => {
    const flags = "DdDuUuOSIEHXL";
    const parsed = parsePeerFlags(flags);

    const dUpper = parsed.find((f) => f.flag === "D");
    assert.equal(dUpper?.description, "Interested and Downloading");

    const dLower = parsed.find((f) => f.flag === "d");
    assert.equal(
      dLower?.description,
      "Peer Choked (We are interested, but peer is choking us)",
    );

    const uUpper = parsed.find((f) => f.flag === "U");
    assert.equal(uUpper?.description, "Interested and Uploading");

    const uLower = parsed.find((f) => f.flag === "u");
    assert.equal(
      uLower?.description,
      "Choked Peer (Peer is interested, but we are choking them)",
    );

    const oUpper = parsed.find((f) => f.flag === "O");
    assert.equal(oUpper?.description, "Optimistic Unchoke");

    const sUpper = parsed.find((f) => f.flag === "S");
    assert.equal(
      sUpper?.description,
      "Snubbed (Peer has not sent data in over 60 seconds)",
    );

    const iUpper = parsed.find((f) => f.flag === "I");
    assert.equal(iUpper?.description, "Incoming Connection");
  });

  it("should handle unknown flags gracefully", () => {
    const parsed = parsePeerFlags("D?Z");
    assert.equal(parsed.length, 3);
    assert.equal(parsed[0].flag, "D");
    assert.equal(parsed[0].description, "Interested and Downloading");
    assert.deepEqual(parsed[1], {
      flag: "?",
      label: "?",
      description: "Flag ?",
    });
    assert.deepEqual(parsed[2], {
      flag: "Z",
      label: "Z",
      description: "Flag Z",
    });
  });
});

describe("peerUtils: maskIpAddress", () => {
  it("should return '-' for null, undefined, or empty/whitespace inputs", () => {
    assert.equal(maskIpAddress(null), "-");
    assert.equal(maskIpAddress(undefined), "-");
    assert.equal(maskIpAddress(""), "-");
    assert.equal(maskIpAddress("   "), "-");
  });

  it("should mask IPv4 addresses by replacing the last two octets with ***.***", () => {
    assert.equal(maskIpAddress("192.168.1.5"), "192.168.***.***");
    assert.equal(maskIpAddress("10.0.4.12"), "10.0.***.***");
    assert.equal(maskIpAddress("172.16.254.1"), "172.16.***.***");
    assert.equal(maskIpAddress("8.8.8.8"), "8.8.***.***");
  });

  it("should mask IPv6 addresses by preserving first groups and masking trailing segments", () => {
    assert.equal(
      maskIpAddress("2001:0db8:85a3:0000:0000:8a2e:0370:7334"),
      "2001:0db8:****:****",
    );
    assert.equal(maskIpAddress("fe80::1"), "fe80::****:****");
    assert.equal(maskIpAddress("::1"), "::****:****");
  });

  it("should handle localhost properly", () => {
    assert.equal(maskIpAddress("localhost"), "localhost");
    assert.equal(maskIpAddress("127.0.0.1"), "127.0.***.***");
    assert.equal(maskIpAddress("::1"), "::****:****");
  });

  it("should return '-' for invalid non-IP inputs", () => {
    assert.equal(maskIpAddress("invalid-host"), "-");
    assert.equal(maskIpAddress("not-an-ip"), "-");
  });
});

describe("peerUtils: getFlagBadgeColor", () => {
  it("should return correct colors for known flags", () => {
    assert.equal(getFlagBadgeColor("D"), "#27ae60");
    assert.equal(getFlagBadgeColor("U"), "#2980b9");
    assert.equal(getFlagBadgeColor("S"), "#c0392b");
  });

  it("should return fallback color for unknown flags", () => {
    assert.equal(getFlagBadgeColor("?"), "#555555");
    assert.equal(getFlagBadgeColor("Z"), "#555555");
  });
});

