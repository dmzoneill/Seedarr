# BitTorrent Protocol Implementations

## Protocol Stack

```mermaid
graph TB
    subgraph "Application Layer"
        SEED[Seeding Engine]
        SIM[Client Simulation]
    end

    subgraph "BitTorrent Protocol Layer"
        TRACK[Tracker Communication<br/>BEP 3, 12, 15]
        PEER[Peer Wire Protocol<br/>BEP 3]
        EXT[Extensions<br/>BEP 6, 9, 10, 11]
        DHT_P[DHT<br/>BEP 5]
        LPD_P[Local Peer Discovery<br/>BEP 14]
    end

    subgraph "Security Layer"
        MSE[MSE/PE Encryption<br/>DH + RC4]
    end

    subgraph "Transport Layer"
        TCP[TCP]
        UTP_P[uTP<br/>BEP 29]
        UDP[UDP]
        MCAST[Multicast]
    end

    SEED --> TRACK
    SEED --> PEER
    SIM --> PEER
    PEER --> EXT
    PEER --> MSE
    MSE --> TCP
    MSE --> UTP_P
    TRACK --> TCP
    TRACK --> UDP
    DHT_P --> UDP
    LPD_P --> MCAST
    UTP_P --> UDP
```

## BEP Implementations

### BEP 3: BitTorrent Protocol (Peer Wire + HTTP Tracker)

**Peer Wire Protocol** (`NzbDrone.Core/Peers/PeerConnection.cs`):

- 68-byte handshake: pstrlen (1) + "BitTorrent protocol" (19) + reserved (8) + info_hash (20) + peer_id (20)
- All standard message types: Choke (0), Unchoke (1), Interested (2), NotInterested (3), Have (4), Bitfield (5), Request (6), Piece (7), Cancel (8), Port (9), Extended (20)
- `PeerServer` runs as BackgroundService, accepts incoming TCP connections, serves fake piece data (zeros)
- `ConnectionManager` manages peer connections with LRU eviction, per-torrent limits, dropout simulation

**HTTP Tracker** (`NzbDrone.Core/Trackers/Http/HttpTrackerProvider.cs`):

```mermaid
sequenceDiagram
    participant S as Seedarr
    participant T as HTTP Tracker

    S->>T: GET /announce?info_hash=...&peer_id=...&port=...&uploaded=...&downloaded=...&left=0&event=started&compact=1
    T-->>S: Bencoded response (interval, peers)

    loop Every interval seconds
        S->>T: GET /announce?...&uploaded=N&event=
        T-->>S: Updated peer list
    end

    S->>T: GET /announce?...&event=stopped
    T-->>S: OK

    S->>T: GET /scrape?info_hash=...
    T-->>S: Bencoded (complete, incomplete, downloaded)
```

### BEP 15: UDP Tracker Protocol

`NzbDrone.Core/Trackers/Udp/UdpTrackerProvider.cs`

```mermaid
sequenceDiagram
    participant S as Seedarr
    participant T as UDP Tracker

    Note over S,T: Connection (magic: 0x41727101980)
    S->>T: connect_request (action=0, transaction_id)
    T-->>S: connect_response (connection_id)

    Note over S,T: Announce (98-byte packet)
    S->>T: announce_request (connection_id, action=1, info_hash, peer_id, downloaded, left=0, uploaded, event, port)
    T-->>S: announce_response (interval, leechers, seeders, peers)

    Note over S,T: Scrape
    S->>T: scrape_request (connection_id, action=2, info_hash)
    T-->>S: scrape_response (complete, downloaded, incomplete)
```

### BEP 5: DHT (Distributed Hash Table)

`NzbDrone.Core/Dht/DhtService.cs` + `RoutingTable.cs` + `DhtPeerStore.cs`

- All four KRPC queries: `ping`, `find_node`, `get_peers`, `announce_peer`
- Compact node encoding (26 bytes: 20-byte node ID + 4-byte IP + 2-byte port)
- Token generation/validation with secret rotation (10-minute intervals, accepts current + previous)
- `implied_port` support
- K-bucket routing table (configurable bucket size, default K=8), XOR distance metric, bad-node eviction
- In-memory peer store with 30-minute TTL
- Bootstrap: `router.bittorrent.com:6881`, `dht.transmissionbt.com:6881`

```mermaid
graph TD
    subgraph "DHT Node"
        RT[Routing Table<br/>K=8, 160-bit space]
        KV[Token Store]
    end

    subgraph "KRPC Messages"
        PING[ping] --> RT
        FN[find_node] --> RT
        GP[get_peers] --> KV
        AP[announce_peer] --> KV
    end

    subgraph "Bucket Management"
        RT --> B1[Bucket 0<br/>8 nodes max]
        RT --> B2[Bucket 1]
        RT --> BN[Bucket N]
    end

    subgraph "Bootstrap"
        BOOT[Bootstrap nodes<br/>router.bittorrent.com:6881<br/>dht.transmissionbt.com:6881] --> RT
    end
```

### BEP 6: Fast Extension

`NzbDrone.Core/Peers/Extensions/FastExtension.cs`

- All five message types: SuggestPiece (0x0D), HaveAll (0x0E), HaveNone (0x0F), RejectRequest (0x10), AllowedFast (0x11)
- BEP 6 Allowed Fast Set algorithm: SHA-1 of (/24-masked IP + infohash), iterating 4-byte chunks
- Per-peer fast set tracking

### BEP 9: Metadata Exchange (ut_metadata)

`NzbDrone.Core/Peers/Extensions/MetadataExchange.cs`

- Request/response messages using bencoded dictionaries (msg_type, piece, total_size)
- BencodeNET serialization

### BEP 10: Extension Protocol

`NzbDrone.Core/Peers/Extensions/ExtensionManager.cs`

- Extension handshake construction (bencoded `m` dictionary mapping names to IDs)
- Supported extensions (configurable): `ut_pex`, `ut_metadata`, `lt_donthave`

### BEP 11: Peer Exchange (PEX)

`NzbDrone.Core/Peers/Extensions/PeerExchange.cs`

```mermaid
sequenceDiagram
    participant A as Seedarr
    participant B as Peer

    Note over A,B: Extended handshake
    A->>B: {m: {ut_pex: 1}}
    B-->>A: {m: {ut_pex: 2}}

    Note over A,B: PEX messages (configurable interval)
    A->>B: ut_pex {added: <compact peers>, added.f: <flags>}
    B-->>A: ut_pex {added: <compact peers>, added.f: <flags>}
```

Compact peer encoding: 6 bytes per peer (4-byte IP + 2-byte port).

### BEP 12: Multi-Tracker

`NzbDrone.Core/Trackers/MultiTracker/MultiTrackerManager.cs`

- Tiered announce list processing
- Configurable `AnnounceToAllTiers` / `AnnounceToAllInTier`
- Exponential backoff failover with consecutive failure tracking

### BEP 14: Local Peer Discovery

`NzbDrone.Core/Peers/Lpd/LocalPeerDiscovery.cs`

- Multicast on `239.192.152.143:6771`
- HTTP-style `BT-SEARCH` announce messages
- BackgroundService with 300-second announce interval

### BEP 29: uTP (Micro Transport Protocol)

`NzbDrone.Core/Transport/UtpConnection.cs` + `UtpManager.cs`

- 20-byte header: type, version, extension, connection ID, timestamps, window, sequence/ack numbers
- Packet types: Data (0), Fin (1), State (2), Reset (3), Syn (4)
- SYN/State connection establishment, data send/receive with ACK, FIN teardown
- `UtpManager` as BackgroundService with TCP fallback, max 100 connections

```mermaid
stateDiagram-v2
    [*] --> CS_IDLE
    CS_IDLE --> CS_SYN_SENT: Send ST_SYN
    CS_SYN_SENT --> CS_CONNECTED: Receive ST_STATE
    CS_IDLE --> CS_SYN_RECV: Receive ST_SYN
    CS_SYN_RECV --> CS_CONNECTED: Send ST_STATE
    CS_CONNECTED --> CS_FIN_SENT: Send ST_FIN
    CS_FIN_SENT --> CS_DESTROY: Receive ST_STATE
    CS_CONNECTED --> CS_GOT_FIN: Receive ST_FIN
    CS_GOT_FIN --> CS_DESTROY: Send ST_STATE
    CS_DESTROY --> [*]

    note right of CS_CONNECTED
        LEDBAT congestion control
        Window-based flow control
        Selective ACK
    end note
```

### MSE/PE Encryption

`NzbDrone.Core/Peers/Encryption/` (MseHandshake, MseKeyDerivation, Rc4StreamCipher, EncryptedStream)

- DH key exchange (768-bit MODP prime) via BouncyCastle
- SHA-1 key derivation with prefixes (keyA, keyB, req1, req2, req3)
- RC4 stream cipher with 1024-byte initial keystream discard
- Three modes: PreferPlainText, PreferEncrypted, RequireEncrypted
- Both outgoing and incoming negotiation

```mermaid
sequenceDiagram
    participant A as Seedarr (Initiator)
    participant B as Peer (Receiver)

    Note over A,B: DH Key Exchange (768-bit MODP)
    A->>B: Ya = g^Xa mod p + random padding
    B-->>A: Yb = g^Xb mod p + random padding
    Note over A,B: Both compute S = Y^X mod p

    Note over A,B: Stream Selection
    A->>B: HASH('req1', S) + HASH('req2', SKEY) XOR HASH('req3', S)
    A->>B: VC + crypto_provide + len(PadC) + PadC + len(IA) + IA
    B-->>A: VC + crypto_select + len(PadD) + PadD

    Note over A,B: RC4 Stream (discard first 1024 bytes)
    A->>B: Encrypted BitTorrent traffic
    B-->>A: Encrypted BitTorrent traffic
```

## Client Behavior Profiles

All profiles in `NzbDrone.Core/Simulation/ClientBehavior/Profiles/`. Each uses Azureus-style peer ID: 8-char prefix + 12 random decimal digits = 20 bytes.

| Profile | Peer ID Prefix | User Agent | Version | Default Port |
|---------|---------------|------------|---------|-------------|
| qBittorrent | `-qB4420-` | `qBittorrent/4.4.2` | 4.4.2 | 6881 |
| Deluge | `-DE2030-` | `Deluge/2.0.3` | 2.0.3 | 6881 |
| Transmission | `-TR3000-` | `Transmission/3.00` | 3.00 | 51413 |
| uTorrent | `-UT3550-` | `uTorrent/3.5.5` | 3.5.5 | 6881 |
| BiglyBT | `-BG2700-` | `BiglyBT/2.7.0.0` | 2.7.0.0 | 6881 |

All profiles support encryption, DHT, and PEX.

## Built-in Tracker Server

`NzbDrone.Core/TrackerServer/`

```mermaid
graph TD
    subgraph "Tracker Server"
        HTTP_S[HTTP Handler<br/>/announce, /scrape]
        UDP_S[UDP Handler<br/>Binary protocol]
        PDB[Peer Database<br/>ConcurrentDictionary<br/>TTL expiry]
        SEC[Security<br/>Rate limiting<br/>IP filtering]
    end

    EXT_C[External BitTorrent Clients] --> HTTP_S
    EXT_C --> UDP_S
    HTTP_S --> SEC
    UDP_S --> SEC
    SEC --> PDB
```
