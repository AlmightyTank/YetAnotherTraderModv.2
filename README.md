# Tony Trader

Server-side trader mod for SPT 4.0.11.

## Overview
Tony adds a custom trader based on the existing Priscilu Origins trader framework, rebranded as Tony: an ex-BEAR operator with Russian underworld connections.

This pass renames/rebrands the trader identity, DLL/project metadata, avatar route, source namespaces, logger text, and install/package naming.

## Current trader identity
- Name: Tony
- Surname: Volkov
- Nickname: Tony
- Location: A back room somewhere under Tarkov.
- Avatar: `data/Tony.jpg`
- Trader ID: `66a0f6b2c4d8e90123456789`

The trader ID was intentionally kept the same for now so existing assort/profile references do not break during the rebrand pass.

## Requirements
- SPT 4.0.11 server
- .NET 9 SDK for building
- Windows recommended

## Build
```powershell
dotnet build -c Release
```

The build output will be placed in:

```text
bin\Release\
```

## Install
1. Close the SPT server.
2. Build the project.
3. Copy the build output folder into:

```text
<SPT>\user\mods\Tony
```

The installed folder should contain:

```text
Tony.dll
package.json
data\base.json
data\assort.json
data\Tony.jpg
config\settings.json
config\items.json
```

## Notes
This is only the rename/rebrand pass. Quest unlocks, four loyalty levels, betrayal branches, and loyalty-gated assort splitting should be added in the next passes.

## Credits
- Original Priscilu Origins foundation: Reis
- Update/contributor foundation: Anigx
- Tony concept/rebrand: AlMightyTank
