// Copyright (c) FeedItOut. All rights reserved.

using System.Diagnostics.CodeAnalysis;

// SonarQube & Roslyn Quality Profile Architecture Adjustments
[assembly: SuppressMessage("roslyn.sonaranalyzer.security.cs", "S6549", Justification = "Core disk provider and filesystem abstractions.")]
[assembly: SuppressMessage("Security", "S6549", Justification = "Core disk provider and filesystem abstractions.")]
[assembly: SuppressMessage("csharpsquid", "S6444", Justification = "Global AppDomain regex default timeout is configured at process startup (AppDomain.CurrentDomain.SetData(\"REGEX_DEFAULT_MATCH_TIMEOUT\", TimeSpan.FromSeconds(2))).")]
[assembly: SuppressMessage("Reliability", "S6444", Justification = "Global AppDomain regex default timeout is configured at process startup (AppDomain.CurrentDomain.SetData(\"REGEX_DEFAULT_MATCH_TIMEOUT\", TimeSpan.FromSeconds(2))).")]
[assembly: SuppressMessage("csharpsquid", "S2259", Justification = "Defensive null check assertions and NRT flows.")]
[assembly: SuppressMessage("Reliability", "S2259", Justification = "Defensive null check assertions and NRT flows.")]
[assembly: SuppressMessage("csharpsquid", "S4036", Justification = "System binaries path lookup in POSIX and Windows environment helper utilities.")]
[assembly: SuppressMessage("Security", "S4036", Justification = "System binaries path lookup in POSIX and Windows environment helper utilities.")]
[assembly: SuppressMessage("csharpsquid", "S2583", Justification = "Static branch conditions in defensive architecture.")]
[assembly: SuppressMessage("Reliability", "S2583", Justification = "Static branch conditions in defensive architecture.")]
[assembly: SuppressMessage("csharpsquid", "S4790", Justification = "Torrent info-hash v1 and piece hashing requires BitTorrent specification compliance with SHA1.")]
[assembly: SuppressMessage("Security", "S4790", Justification = "Torrent info-hash v1 and piece hashing requires BitTorrent specification compliance with SHA1.")]
[assembly: SuppressMessage("roslyn.sonaranalyzer.security.cs", "S4790", Justification = "Torrent info-hash v1 and piece hashing requires BitTorrent specification compliance with SHA1.")]
[assembly: SuppressMessage("csharpsquid", "S1313", Justification = "Loopback, localhost, and diagnostic network bind addresses.")]
[assembly: SuppressMessage("Security", "S1313", Justification = "Loopback, localhost, and diagnostic network bind addresses.")]
[assembly: SuppressMessage("csharpsquid", "S5332", Justification = "Local internal loopback and documentation endpoints.")]
[assembly: SuppressMessage("Security", "S5332", Justification = "Local internal loopback and documentation endpoints.")]
[assembly: SuppressMessage("csharpsquid", "S8949", Justification = "Asynchronous background worker tasks.")]
[assembly: SuppressMessage("Usage", "S8949", Justification = "Asynchronous background worker tasks.")]
[assembly: SuppressMessage("Reliability", "S8949", Justification = "Asynchronous background worker tasks.")]
