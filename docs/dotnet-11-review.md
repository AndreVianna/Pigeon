# .NET 11 and C# 14 review

Reviewed Microsoft's .NET 11 RC2 documentation on 2026-10-29. Pigeon targets `net11.0` and explicitly uses C# 14; this avoids depending on a preview language version merely because the SDK is a preview. The APIs below describe Microsoft documentation, not features already adopted or benchmarked in Pigeon.

## Relevant .NET 11 changes

- [Library updates](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/libraries) include process start and capture helpers, asynchronous line-oriented process output, parent-exit handling on Windows, stream adapters, JSON writer reuse, and other serialization improvements. Process APIs may simplify a future isolated runtime adapter or test harness; any child process still needs explicit authorization and bounded cleanup. JSON writer reuse could help a persistent IPC host if measurement justifies it; it does not change MCP's framing or acknowledgment rules.
- The same library updates include new cryptographic APIs. They are not a substitute for a reviewed authentication and credential-provisioning design. The existing synthetic-grant comparison uses the established fixed-time byte-sequence API; no new cryptographic scheme is adopted solely because an API exists.
- [Runtime updates](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/runtime) describe runtime-managed async as an opt-in preview. It could be relevant to the asynchronous pipe server, but it is not enabled; correctness, compatibility and performance would need separate testing.
- [SDK and tooling changes](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/sdk) may improve builds and publishing. Neither Native AOT nor a self-contained installer is enabled: first establish secure shared-service behavior and a repeatable build.

## C# 14

The [C# 14 feature guide](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14) covers extension members, field-backed properties, implicit span conversions, `nameof` on unbound generic types, lambda parameter modifiers, partial constructors and events, user-defined compound assignment, null-conditional assignment, and file-based app directives. The span conversions may be useful when refining bounded protocol parsing, and field-backed properties or null-conditional assignment can simplify ordinary code where appropriate. Pigeon does not need extension frameworks, custom operators, generated partial members, or file-based scripts merely to use these features.

## Local version boundary

Microsoft's [October security update](https://devblogs.microsoft.com/dotnet/dotnet-october-2026-oob-security-updates/) recommends that .NET 11 RC2 users move to RC3. The user installed RC3 during this work; the RC3 SDK, runtime and targeting pack are now visible. No machine-wide SDK or runtime installation was performed by this work. The solution selects RC3 and uses C# 14. After the user restored packages from their normal PowerShell, the full solution built with `--no-restore` and the xUnit and Gherkin tests passed on RC3. A sandboxed restore remains blocked while reading the user-level NuGet configuration.
