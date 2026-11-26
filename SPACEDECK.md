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
