# Priscilu_Origins (v2)

Server-side trader mod for SPT 4.0.11.

## Overview
Priscilu_Origins adds a custom trader with a large assortment and custom avatar. This repository contains the C# source code used to build the server mod for SPT 4.x.

## Requirements
- SPT 4.0.11 (server)
- .NET 9 SDK (for building)
- Windows (recommended; matches SPT server environment)

## Build (Release)
1) Open a terminal in the repo folder.
2) Build the mod:

```powershell
# from repo root

dotnet build -c Release
```

The build output will be placed in:

```
bin\Release\Priscilu_Origins\
```

## Install
1) Close the SPT server.
2) Copy the build output folder into your SPT user mods folder:

```
<SPT>\user\mods\Priscilu_Origins
```

After copy, the folder should contain:

- Priscilu_Origins.dll
- Priscilu_Origins.deps.json
- Data\base.json
- Data\assort.json
- Data\Priscilu_Origins.jpg

3) Start the SPT server.

## Packaged layout (Priscilu_Origins_v2)
The release ZIP uses the full SPT path so you can extract it directly into your game root:

```
SPT\user\mods\Priscilu_Origins_v2\
```

Contents are the same as the normal install, just nested under the full SPT path.

## Troubleshooting
- If the server logs show missing soft armor inserts, ensure the assort.json uses the exact slot names from the SPT item templates (case-sensitive):
  - Soft_armor_front
  - Soft_armor_back
  - Soft_armor_left
  - soft_armor_right
note for later

## Credits
- Original author: Reis
- Update/Maintenance: CyberByteCraft

## Special Thanks
And a special thank you to Reis for the foundational work.

## Version
- Mod: 6.1.1
- Targets: SPT ~4.0.11
