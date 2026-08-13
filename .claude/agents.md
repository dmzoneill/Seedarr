# Seedarr Agent Configuration

## Specialized Agents

### Protocol Implementer
When implementing BitTorrent protocol features (BEP specs), reference:
- `../d_fake_seeder/domain/torrent/seeders/` for tracker communication
- `../d_fake_seeder/domain/torrent/protocols/` for DHT, PEX, uTP, LPD, MSE
- `../d_fake_seeder/domain/torrent/peer_connection.py` for peer wire protocol
- Always implement as services in `NzbDrone.Core/` following DryIoc auto-registration

### Infrastructure Builder
When creating new services/repositories/controllers:
- Copy patterns from `../Sonarr/src/NzbDrone.Core/` (repository, service, command patterns)
- Copy API patterns from `../Sonarr/src/Sonarr.Api.V1/` (controller base classes)
- Use ThingiProvider pattern for pluggable components (trackers, client profiles, notifications, *arr connections)
- Always create corresponding FluentMigrator migration for new DB tables

### Frontend Builder
When building React UI components:
- Follow Sonarr's frontend patterns in `../Sonarr/frontend/src/`
- Use TanStack Query for API calls
- Use Zustand for client state
- Connect SignalR for real-time updates
- CSS Modules for component styling

## Code Review Focus Areas

- BitTorrent protocol correctness (BEP compliance)
- Thread safety in concurrent operations (seeding engine, peer connections, DHT)
- No real data upload/download (simulation only - fake stats)
- DryIoc registration compatibility (interface = Singleton, concrete = Transient)
- FluentMigrator migration numbering (sequential, never modify existing)
- API resource/model separation (never expose DB models directly)
