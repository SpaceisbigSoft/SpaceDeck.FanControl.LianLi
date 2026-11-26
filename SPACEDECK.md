# SpaceDeck's build of FanControl.LianLi

This repository is a fork of [lewisgibson/FanControl.LianLi](https://github.com/lewisgibson/FanControl.LianLi)
(MIT, Copyright (c) 2026 Lewis Gibson) maintained for [SpaceDeck](https://github.com/SpaceisbigSoft/SpaceDeck), a
Windows hardware monitor. Upstream does all the hard work; this fork adds what SpaceDeck needs and offers every
general fix back upstream.

## Branches

- `main` mirrors upstream's `main`. Nothing of ours is committed to it.
- `upstream-pr/*`: one small fix each, started from upstream's `main`, offered upstream as a pull request.
- `spacedeck`: what SpaceDeck ships, upstream plus SpaceDeck's changes. Each upstream release tag is merged in
  (merged, not rebased, so released builds stay reachable).

## What SpaceDeck's build is

- The upstream **Lighting** variant, built as `FanControl.LianLi-SpaceDeck.dll`, version 2.0.x.
- The plugin's Name stays `Lian Li Uni (Lighting)` and every sensor id stays upstream's, so FanControl curves bound
  to the upstream Lighting build stay bound (FanControl keys a binding as `<plugin Name>/<sensor id>`).
- The plugin type stays `FanControl.LianLi.Plugin.LianLiPlugin`, so the plugin's copy guard sees this build and an
  upstream build as copies of one plugin and lets only one of them drive the devices (the higher version: this one).

## The Galahad II LCD's pump follows the motherboard header

On a Galahad II Vision LCD (`0x0416:0x7391` and `0x7395`) this build hands the pump to the motherboard's PWM
header instead of driving it over USB. It sends the cooler's set-pump command with the source byte at "follow the
motherboard" (`01 8A 00 00 00 02 01 64`) on the first tick after the cooler opens, again after every reconnect, and
again every 15 seconds on the keepalive. The duty in that packet is 100%, so a cooler that ignored the source byte
would run its pump at full speed, never slow. The plugin registers no pump control for it; the pump RPM and the
coolant temperature sensors stay, and the cooler's fan channel is a control as upstream. Every other cooler, the
Galahad II Trinity included, is driven exactly as upstream.

Why: a pump duty set over USB persists in the cooler across a full power cut, and the cooler has no thermal failsafe
of its own (measured on the Trinity, upstream issue #30, see `docs/protocol.md`). If FanControl or the plugin stops,
a USB-driven pump holds its last duty however hot the CPU gets. A pump that follows the header keeps following the
board, and its BIOS curve, whatever happens to FanControl.

What to set:

- In FanControl, put a curve on the motherboard's **Pump Fan** header control (the one the cooler's pump PWM lead is
  plugged into), with a minimum of 60%. The `Lian Li Galahad #1 Pump` control that an upstream build showed is gone;
  a curve bound to it no longer drives anything and can be deleted.
- In the BIOS, leave that header on a safe value (a fixed high duty, or a curve that reaches full speed well before
  the CPU's limit). It is what the pump follows when FanControl is not running.
- If the pump ever runs too slow, set the motherboard Pump Fan header to 100% (in FanControl, or in the BIOS).

## Building

The `SpaceDeck build` workflow (`.github/workflows/spacedeck.yaml`) runs on every push to `spacedeck`: format check,
the standard and Lighting test suites with upstream's full-coverage gate, then the shipped DLL as a zip with this
file, the licence and the third-party notices. It never publishes a release.

## Installing

1. Close FanControl.
2. Move any other copy of the plugin (for example `Plugins\FanControl.LianLi.Lighting.dll`) **out of FanControl's
   Plugins folder**, to a backup folder elsewhere: FanControl loads every `FanControl*.dll` anywhere under Plugins.
3. Unblock the downloaded DLL (right-click → Properties → Unblock).
4. Start FanControl, open the menu, **Install plugin**, and pick `FanControl.LianLi-SpaceDeck.dll`.
5. Check `%LOCALAPPDATA%\FanControl.LianLi\plugin.log` (or, when FanControl runs as its service,
   `C:\Windows\System32\config\systemprofile\AppData\Local\FanControl.LianLi\plugin.log`).

To go back: close FanControl, remove `Plugins\LianLi-SpaceDeck\`, put the backup copy back.

## Licence

MIT, as upstream: see `LICENSE`. SpaceDeck's additions are MIT too.
