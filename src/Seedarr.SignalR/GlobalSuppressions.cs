// Copyright (c) FeedItOut. All rights reserved.

using System.Diagnostics.CodeAnalysis;

// SonarQube & Roslyn Quality Profile Architecture Adjustments
[assembly: SuppressMessage("csharpsquid", "S6444", Justification = "Global AppDomain regex default timeout is configured at process startup")]
[assembly: SuppressMessage("csharpsquid", "S8949", Justification = "SignalR hub client push tokens.")]
