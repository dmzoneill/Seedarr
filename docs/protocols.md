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
        EXT[Extensions<br/>BEP 6, 9, 11]
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

### BEP 3: HTTP Tracker Protocol

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

```mermaid
sequenceDiagram
    participant S as Seedarr
    participant T as UDP Tracker

    Note over S,T: Connection (magic: 0x41727101980)
    S->>T: connect_request (action=0, transaction_id)
    T-->>S: connect_response (connection_id)

    Note over S,T: Announce
    S->>T: announce_request (connection_id, action=1, info_hash, peer_id, downloaded, left=0, uploaded, event, port)
    T-->>S: announce_response (interval, leechers, seeders, peers)

    Note over S,T: Scrape
    S->>T: scrape_request (connection_id, action=2, info_hash)
    T-->>S: scrape_response (complete, downloaded, incomplete)
```

### BEP 5: DHT (Distributed Hash Table)

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

### BEP 11: Peer Exchange (PEX)

```mermaid
sequenceDiagram
    participant A as Seedarr
    participant B as Peer

    Note over A,B: Extended handshake
    A->>B: {m: {ut_pex: 1}}
    B-->>A: {m: {ut_pex: 2}}

    Note over A,B: PEX messages (every 60s)
    A->>B: ut_pex {added: <compact peers>, added.f: <flags>}
    B-->>A: ut_pex {added: <compact peers>, added.f: <flags>}
```

### BEP 29: uTP (Micro Transport Protocol)

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

| Profile | Peer ID | User Agent | Behavior |
|---------|---------|------------|----------|
| qBittorrent 4.4.2 | `-qB4420-` | `qBittorrent/4.4.2` | Aggressive seeding, fast piece selection |
| Deluge 2.0.3 | `-DE2030-` | `Deluge 2.0.3` | Balanced, moderate announce frequency |
| Transmission 3.0 | `-TR3000-` | `Transmission/3.00` | Conservative, strict rate limiting |
| uTorrent 3.5.5 | `-UT3550-` | `uTorrent/3550` | Chatty announces, PEX heavy |
| BiglyBT | `-BG` prefix | `BiglyBT` | DHT focused, large swarm handling |

## Built-in Tracker Server

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
