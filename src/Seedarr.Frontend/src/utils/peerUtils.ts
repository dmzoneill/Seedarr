export interface PeerFlagInfo {
  flag: string;
  label: string;
  description: string;
}

const PEER_FLAGS_MAP: Record<string, { label: string; description: string }> = {
  D: { label: "D", description: "Interested and Downloading" },
  d: {
    label: "d",
    description: "Peer Choked (We are interested, but peer is choking us)",
  },
  U: { label: "U", description: "Interested and Uploading" },
  u: {
    label: "u",
    description: "Choked Peer (Peer is interested, but we are choking them)",
  },
  O: { label: "O", description: "Optimistic Unchoke" },
  S: {
    label: "S",
    description: "Snubbed (Peer has not sent data in over 60 seconds)",
  },
  I: { label: "I", description: "Incoming Connection" },
  E: {
    label: "E",
    description: "Encrypted Protocol Handshake (MSE/PE)",
  },
  H: { label: "H", description: "uTP Transport Protocol" },
  X: {
    label: "X",
    description: "Peer Discovered via Peer Exchange (PEX)",
  },
  L: { label: "L", description: "Local Peer Discovered via LPD" },
};

/**
 * Returns a distinguishing badge background color for a BitTorrent protocol flag.
 */
export function getFlagBadgeColor(flag: string): string {
  switch (flag) {
    case "D":
      return "#27ae60"; // Downloading (green)
    case "d":
      return "#e67e22"; // Peer Choked (orange)
    case "U":
      return "#2980b9"; // Uploading (blue)
    case "u":
      return "#7f8c8d"; // Choking Peer (gray)
    case "O":
      return "#8e44ad"; // Optimistic unchoke (purple)
    case "S":
      return "#c0392b"; // Snubbed (red)
    case "I":
      return "#16a085"; // Incoming (teal)
    case "E":
      return "#d35400"; // Encrypted (amber)
    case "H":
      return "#34495e"; // uTP (slate)
    case "X":
      return "#9b59b6"; // PEX (violet)
    case "L":
      return "#1abc9c"; // LPD (cyan)
    default:
      return "#555555";
  }
}

/**
 * Parses standard BitTorrent peer flag characters into structured flag objects.
 */
export function parsePeerFlags(
  flags: string | null | undefined,
): PeerFlagInfo[] {
  if (!flags || typeof flags !== "string") return [];
  const trimmed = flags.trim();
  if (!trimmed) return [];

  const result: PeerFlagInfo[] = [];
  for (const char of trimmed) {
    if (!char.trim()) continue;
    const mapped = PEER_FLAGS_MAP[char];
    if (mapped) {
      result.push({
        flag: char,
        label: mapped.label,
        description: mapped.description,
      });
    } else {
      result.push({
        flag: char,
        label: char,
        description: `Flag ${char}`,
      });
    }
  }

  return result;
}

/**
 * Masks an IP address for privacy.
 * - IPv4: replaces the last two octets with ***.*** (e.g. 192.168.1.5 -> 192.168.***.***)
 * - IPv6: keeps first two groups, replaces remaining with ****:**** (e.g. 2001:0db8:... -> 2001:0db8:****:****)
 * - Localhost: returns "localhost" or masks if 127.0.0.1 / ::1
 * - Null/empty/invalid: returns "-"
 */
export function maskIpAddress(ip: string | null | undefined): string {
  if (!ip || typeof ip !== "string" || !ip.trim()) return "-";
  const trimmed = ip.trim();

  if (trimmed.toLowerCase() === "localhost") {
    return "localhost";
  }

  if (trimmed.includes(".")) {
    const parts = trimmed.split(".");
    if (parts.length >= 2) {
      return `${parts[0]}.${parts[1]}.***.***`;
    }
    return `${parts[0]}.***.***`;
  }

  if (trimmed.includes(":")) {
    if (trimmed.startsWith("::")) {
      return "::****:****";
    }
    const parts = trimmed.split(":");
    const first = parts[0] || "";
    const second = parts[1] || "";
    return `${first}:${second}:****:****`;
  }

  return "-";
}
