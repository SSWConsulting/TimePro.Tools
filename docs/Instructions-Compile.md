# Instructions - Compile

This document explains how to build and run the TimePro CLI locally.

## Prerequisites

- .NET 11 SDK
- Git
- Optional: GitHub CLI for commands that enrich timesheets with issue and PR context

No TimePro credentials are required to build or run unit/integration tests. Live TimePro commands require `tp login` or an existing tenant config.

## Restore and Build

```bash
dotnet restore SSW.TimePro.Timesheets.Cli.slnx
dotnet build SSW.TimePro.Timesheets.Cli.slnx
```

## F5 Experience

Run commands directly from the project:

```bash
dotnet run --project src/SSW.TimePro.Cli -- --help
dotnet run --project src/SSW.TimePro.Cli -- ts get --week
dotnet run --project src/SSW.TimePro.Cli -- tenant info --json
```

Run the MCP host locally:

```bash
dotnet run --project src/SSW.TimePro.Cli -- mcp
```

## Local Tool Install

Package and install the CLI as `tp`:

```bash
dotnet pack src/SSW.TimePro.Cli/ -c Release -o src/SSW.TimePro.Cli/nupkg
dotnet tool install -g --add-source src/SSW.TimePro.Cli/nupkg SSW.TimePro.Cli
```

Update an existing local install:

```bash
dotnet tool uninstall -g SSW.TimePro.Cli
dotnet tool install -g --add-source src/SSW.TimePro.Cli/nupkg SSW.TimePro.Cli
```

## Tests

```bash
dotnet test tests/SSW.TimePro.Cli.Tests/
dotnet test tests/SSW.TimePro.Cli.Integration/
```

The integration tests use WireMock.Net and do not call the live TimePro API.
