// Copyright (c) FeedItOut. All rights reserved.

using System.Diagnostics.CodeAnalysis;

// SonarQube & Roslyn Quality Profile Architecture Adjustments
[assembly: SuppressMessage("roslyn.sonaranalyzer.security.cs", "S6549", Justification = "Terminal PTY and process session path checks.")]
[assembly: SuppressMessage("Security", "S6549", Justification = "Terminal PTY and process session path checks.")]
[assembly: SuppressMessage("csharpsquid", "S8949", Justification = "Terminal and HTTP stream writers use WebSocket cancellation tokens.")]
[assembly: SuppressMessage("Usage", "S8949", Justification = "Terminal and HTTP stream writers use WebSocket cancellation tokens.")]
[assembly: SuppressMessage("csharpsquid", "S6444", Justification = "Global AppDomain regex default timeout is configured at process startup")]
[assembly: SuppressMessage("csharpsquid", "S5332", Justification = "Internal loopback and local documentation URLs.")]
[assembly: SuppressMessage("Security", "S5332", Justification = "Internal loopback and local documentation URLs.")]
[assembly: SuppressMessage("csharpsquid", "S1313", Justification = "Loopback and diagnostic network bind addresses.")]
