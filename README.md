# ApocaGearbox 0.1.19

Tune engine gear ratios and final drive with the Part Adjuster. Aim at a loose or installed engine body and press **E** to open the editor. Manual tuning and the optional automatic gear calculator are both available.

## Features

- Per-engine forward/reverse ratios, final drive and named presets.
- Optional gearing calculator using the selected engine, fitted boost, tires and vehicle readings; loose engines can link to a nearby vehicle.
- Adjustable engine rev limit, gradual throttle response, engine braking and powered speed limit.
- Optional high/low range and 2WD/4WD selectors, with boost-aware clutch support.
- Independent **Brake strength** and **Handbrake strength** controls in **DRIVING SUPPORT**, from 0.1x to 5x; 1x is normal. Settings save with the engine tune and presets.
- An in-game MODS menu icon.

**0.1.19 removes traction control completely.** Old traction-control preferences are ignored while existing gearing, brake and rev-limit settings remain readable. The mod name and plugin identity remain ApocaGearbox.

## Installation

Requires **BepInEx 5** and **Apocasetter**. ChaseCamera and DustStorm are not required.

Download `ApocaGearbox-0.1.19.zip` from [Releases](https://github.com/Jaime-Wolf/ApocaGearbox/releases/latest). Close the game and copy the ZIP's `ApocaGearbox` folder into `BepInEx/plugins`, replacing the existing mod. Keep `ApocaGearbox.dll`, `icon.png` and `README.md` together, and avoid duplicate DLL copies.

Open the engine editor, make changes, then choose **Apply and close**. Save the game to retain the applied tune. Cancel discards pending edits. **Reset to original** restores captured native settings. Tire grip and native vehicle physics still affect acceleration and stopping distance.

## Source and checks

This repository contains original mod source and standalone verification code. Game and third-party DLLs are not redistributed. Building requires your installed Apocalypter, BepInEx and Apocasetter libraries.

```powershell
.\Source\build.ps1 -GameDir 'D:\Steam\steamapps\common\Apocalypter'
.\Source\verification\run-tests.ps1
.\Source\verification\run-brake-tests.ps1
.\Verification\inspect-build.ps1 -Dll '.\Source\build\ApocaGearbox.dll'
```

The 0.1.19 production build passed 1,275 automated checks/scenarios across 20 suites and 25 compiled/native-library inspections. In-game testing of this build remains; automated checks do not prove every vehicle or mod combination.

Separate compatibility patches are permitted. Copying, adapting or reuploading this mod requires the author's permission; see [LICENSE.md](LICENSE.md).
