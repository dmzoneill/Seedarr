// Copyright (c) FeedItOut. All rights reserved.

using System.Diagnostics.CodeAnalysis;

// SonarQube & Roslyn Quality Profile Architecture Adjustments
[assembly: SuppressMessage("csharpsquid", "S5332", Justification = "Localhost and internal loopback bind endpoints.")]
[assembly: SuppressMessage("Security", "S5332", Justification = "Localhost and internal loopback bind endpoints.")]
[assembly: SuppressMessage("csharpsquid", "S1313", Justification = "Loopback and diagnostic network bind addresses.")]
[assembly: SuppressMessage("csharpsquid", "S6444", Justification = "Global AppDomain regex default timeout is configured at process startup")]
