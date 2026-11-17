# Hermes Desktop

A personal desktop client (built with .NET 8 and Avalonia UI) for connecting directly to a self-hosted Hermes gateway.

## Structure

- `src/HermesDesktop.Core` — Gateway auth, REST/WebSocket protocol client, and domain models
- `src/HermesDesktop.Platform` — Local credential storage (DPAPI on Windows)
- `src/HermesDesktop.App` — Avalonia desktop UI
- `tests/HermesDesktop.Core.Tests` — Core unit tests
- `installer/windows` & `scripts/` — Windows NSIS installer packaging scripts

## Build & Run

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```bash
dotnet restore HermesDesktop.sln
dotnet build HermesDesktop.sln
dotnet test HermesDesktop.sln
dotnet run --project src/HermesDesktop.App/HermesDesktop.App.csproj
```

To build the Windows x64 installer (requires `makensis`):

```bash
./scripts/build-win-installer.sh
```

## Code Signing & Privacy

- Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/). Release binaries and Windows installers are built from source via GitHub Actions.
- **Privacy Policy**: This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it. All connections go directly to the self-hosted Hermes gateway URL configured by the user.

## License

Released under the [MIT License](LICENSE).  
Inspired by [kaishi00/hermes-conduit](https://github.com/kaishi00/hermes-conduit) (MIT).
