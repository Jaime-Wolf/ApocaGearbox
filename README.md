# ApocaGearbox 0.1.9

ApocaGearbox lets you tune individual gears and final drive using the game's Part Adjuster. Set up your engine for cruising, hauling, or climbing, then use the spare dashboard switches for high/low range and optional 2WD/4WD selection.

Your tune belongs to the engine. It saves with that engine and follows it when you install it in another vehicle.

## Features

- Edit every forward gear, reverse gear, and final drive in one window.
- Tune installed engines or loose engines before fitting them.
- Use taller cruising gears to lower RPM and reduce fuel use at the same road speed.
- Save named presets for compatible gearboxes.
- Add adjustable high/low range using the switch immediately left of the lights switch.
- Select range with a left-click, a normal game hover label, and the same click sound as the lights switch.
- Enable a 4WD conversion without extra parts and select 2WD or 4WD with the other spare dashboard switch.
- Adjust engine braking for more slowing when you lift off the accelerator.
- Enable clutch-capacity support for turbocharged and supercharged engines.
- Enable protection against manual forward shifts that would over-rev the engine.
- Set automatic shift RPMs and an optional powered road-speed limit.
- Use the included road preset for the Pigpen with the 5.2L V8.

The game's original final-drive adjuster and the new editor share the same value. You can use either control without them fighting over your tune.

## Requirements

- [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) for Windows x64.
- [Apocasetter — Mod Configuration Menu](https://www.nexusmods.com/apocalypter/mods/1).
- The game's Part Adjuster item to open the gearbox editor.

The separate Part Adjuster Tools mod is optional.

Built with BepInEx 5.4.23.5, Apocasetter 2.0.8, and Apocalypter Steam build 25639003.

## Installation

1. Install BepInEx 5 into your Apocalypter game folder and run the game once.
2. Install Apocasetter using the instructions on its mod page.
3. Close the game.
4. Extract the ApocaGearbox ZIP into your game folder, merging the included BepInEx folder.
5. Check that the DLL is at `BepInEx\plugins\ApocaGearbox\ApocaGearbox.dll`.
6. Start the game and load your save.

When updating, replace the existing DLL. Keep only one copy of ApocaGearbox installed.

## Gearbox tuning

Select the Part Adjuster, aim at the engine's gearbox adjustment point within two metres, and press **E**. The engine can be installed or sitting outside the vehicle.

Higher ratios give shorter gearing and more pulling power. Lower ratios give longer gearing. Final drive changes every gear together. The editor shows the engine's actual gears and keeps its existing gear count and neutral position. Its shift preview helps you compare the RPM drop between gears.

Choose **Apply and close** to keep your changes, then **save your game**. Applying a tune does not save the game for you.

The opening key and the mod's enabled setting are available in Apocasetter's MODS menu. If the editor does not open, put down any item you are carrying and aim at the gearbox's native adjustment point rather than the engine block. Check that both required mods are installed.

## Gearing and fuel economy

The stock game scales fuel use with engine RPM. Taller cruising gears can lower revs at the same road speed and reduce fuel use, as long as the engine can maintain that speed. Shorter gears give more pulling power for climbing and hauling. Tune for the engine, vehicle, tires and load you are using.

ApocaGearbox uses that existing fuel calculation; it does not edit the engine's base fuel-consumption value. There is no fixed fuel-saving percentage.

## High and low range

Enable **the dashboard high / low switch** in the gearbox editor and Apply. The default low-range reduction is **2.5:1**, adjustable from 1:1 to 10:1.

Stop, select neutral, and wait for any gear change to finish. Look at the switch immediately left of the lights switch and **left-click** to change range. Its hover label shows HIGH or LOW. You can use it from the driver's seat or from outside within two metres.

**High** uses your normal tune. **Low** gives shorter gearing in every forward and reverse gear, with more pulling power and engine braking at a lower road speed. Your saved base ratios and final-drive setting stay the same.

With automatic shifting, stop and release the accelerator and foot brake so the gearbox can hold neutral. Use the parking brake to hold the vehicle on a slope.

Save the game after changing range if you want that selection kept on reload. Tunes saved before high/low support start with the optional range selector disabled; enable it on each engine you want to use.

## Two-wheel and four-wheel drive

On a loose or installed engine, check **Enable 2WD / 4WD selector** in the gearbox menu and Apply. You can prepare it before installation. On an installed engine, availability changes require stopped/neutral. New vehicle selectors start in 4WD. High/low and drive selection can be enabled independently.

Look at the spare switch immediately left of the high/low switch and **left-click** to select 2WD or 4WD. It uses one normal hover label and the same native switch click. Stop, select neutral, and finish shifting before changing mode or enabling/disabling the option.

**4WD** powers all four stock wheel components. **2WD** keeps the vehicle's factory driven wheels; the factory 4WD Pigpen uses rear drive in 2WD. Crossbreed and Halfbreed keep their unusual middle-wheel drive in 2WD. The conversion needs no extra parts, but tire grip still matters.

Selector availability saves with the **engine** and gearbox presets. It activates after installation once the vehicle is ready, stopped and in neutral. The selected **2WD/4WD mode** saves with the vehicle, so another engine's tune does not copy a different car's axle selection. Older tunes and presets preserve existing vehicle settings. Save your game after changes. Turning the option off restores native drive behavior.

## Driving support

Engine braking supports automatic shifting, manual shifts with autoclutch, and full manual clutch control. Neutral or a disengaged clutch lets the vehicle coast. Extra braking fades near idle and as you press the accelerator.

Boost support helps an undersized clutch carry the engine's boosted torque. Tire grip still matters. The manual shift guard blocks unsafe forward gear selections while leaving automatic shifting under the game's control.

The speed setting limits powered acceleration. It does not apply the brakes, so downhill coasting can exceed the selected speed.

## Compatibility

Designed for the 16 stock engine gearbox layouts and all 14 stock vehicle layouts. Both spare selector switches are present on those dashboards. Continuously variable transmissions are unsupported, and modded engines, dashboards or drivetrains may need compatibility work.

Version 0.1.9 has been confirmed working in the author's in-game setup. Coverage of all stock layouts comes from game-asset inspection and focused checks; every engine/vehicle combination has not been driven.

Presets need a matching gear count and reverse/neutral/forward layout. Existing saves can be used; engines without custom tuning keep their native behavior.

## Removal

If you want the captured original settings restored, use **Reset to original** on each tuned engine and save the game first. Stop and select neutral before resetting when low range is active or the drive selector is enabled.

Close the game and remove `BepInEx\plugins\ApocaGearbox`. Keep BepInEx and your other mods. A saved custom final drive can remain after DLL removal because the game also saves that value. Presets and configuration remain unless you remove them separately.

## Bug reports

Include the mod version, engine, vehicle, shifting/clutch mode, and steps that reproduce the problem. For selector issues, include HIGH/LOW and 2WD/4WD selections. Relevant ApocaGearbox lines from `BepInEx\LogOutput.log` are useful too.

## Credits

BepInEx contributors for the mod loader, DeonDF for Apocasetter, and the Apocalypter developers for the game and its existing switch audio. No game audio or dependency DLLs are included in the download.

Developed with Codex from the author's design direction and in-game testing.
