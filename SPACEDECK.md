# SpaceDeck.FanControl.LianLi — SpaceDeck's FanControl plugin for Lian Li

This repository (`SpaceisbigSoft/SpaceDeck.FanControl.LianLi`, renamed from `SpaceisbigSoft/FanControl.LianLi` on
2026-11-26) is a fork of [lewisgibson/FanControl.LianLi](https://github.com/lewisgibson/FanControl.LianLi)
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
- It also sends its readings to SpaceDeck's collector when one is installed (see "Readings into SpaceDeck").
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

## Readings into SpaceDeck

This build also sends what it has registered with FanControl to SpaceDeck's collector, so SpaceDeck shows the
coolant temperatures, fan and pump speeds and fan duties next to its own sensors. Readings only: nothing on the pipe
is ever a command, nothing the collector could send is read (the pipe is write-only from the plugin's side), and the
plugin never reaches a device for it. FanControl, through the plugin, does all fan control, exactly as without it.

- **What is sent.** Every fan sensor (RPM), temperature sensor (degrees C) and control (the duty FanControl is
  applying, as the control last recorded it) the plugin registered, under its FanControl id (without the plugin's
  Name), grouped into one device per controller, cooler or wireless fan group: the id up to its second slash
  (`LianLi/1` for `LianLi/1/coolant/temp`, `LianLi/wa1b2c3d4e5f6` for a wireless group), named for what its readings'
  names share (`Lian Li GA II LCD`, `Lian Li Galahad #2`, `Lian Li UNI FAN TL V2 Wireless d4e5f6`), a `cooler` for a
  Galahad or HydroShift and `fans` otherwise. A `hello` line on connecting, the catalog after it and again whenever
  the set changes, and the values once a second, nulls for values FanControl has not updated for five seconds.
- **Where.** The local pipe `\\.\pipe\SpaceDeck.Plugins`, hosted by the collector's Windows service
  (`SpaceDeckCollector`), in the protocol SpaceDeck documents in its `docs/PLUGIN-PIPE.md`. The plugin connects from a
  thread of its own (`SpaceDeckPublisher`), never from FanControl's calls: 250 ms to connect, then a gap doubling
  from one second to thirty while the collector is away; a write that blocks for five seconds drops the connection.
  It logs the first failure of each run and each connection, not every try.
- **How each end checks the other.** The plugin connects as a write-only client at the identification level, so the
  collector can see who it is but never act as it, and before writing anything checks that the pipe's server
  (`GetNamedPipeServerProcessId`) is the `SpaceDeckCollector` service's own process (`QueryServiceStatusEx`, asking
  only for the query-status right); any other server is disconnected without a word. The collector, for its part,
  checks that the client is `FanControl.exe` or `FanControl.Service.exe` in a folder only administrators can write
  to, and disconnects anything that breaks its rules, which the plugin keeps to in one place each (`SpaceDeckText`
  for ids and names, `SpaceDeckDeviceGrouping` for the devices, `SpaceDeckPipeEncoder` for the lines).
- **Off switch.** On a PC without SpaceDeck's collector (no `SpaceDeckCollector` service) the plugin logs one line
  and does nothing more until the next refresh. Whatever fails here - the catalog, a capture, the pipe - is logged
  and costs only the readings.

In the code: the SpaceDeck-only Plugin-layer types are in `src/FanControl.LianLi/SpaceDeck/` (namespace
`FanControl.LianLi.SpaceDeck`); the two native calls are the Transport adapters `WindowsNamedPipeApi` and
`WindowsServiceControlApi`, behind `INamedPipeApi` and `IServiceControlApi`, and every write is bounded by the same
`IDeviceCallRunner` the device transfers use. Beyond upstream's permitted exception swallows this adds the
publisher's own thread (a thread the plugin starts itself) and two guards at the composition root, around preparing
the catalog in `Load` and capturing the values in `Update`, in the same spirit as the opt-in lighting path. FanControl
creates a new plugin object on every refresh, so each object is one connection with its own `instance` id; the
collector keeps its catalog across them.

## The GA II LCD's screen

SpaceDeck can show one of its dashboards on a Galahad II Vision LCD's own 480x480 screen (`0x0416:0x7391` and
`0x7395`). The app draws the frame, the collector passes it on, and this build sends it to the cooler, because the
plugin is the only program that opens the cooler. Only pictures: nothing on the screen pipe is a fan, pump or
lighting command, and the plugin builds every USB packet itself; the only bytes from the pipe that reach the cooler
are a JPEG's.

- **The catalog.** The cooler's hardware entry in the `catalog` line carries
  `"screen":{"width":480,"height":480,"format":"jpeg"}`, so SpaceDeck's Settings can list it under "Cooler screens".
  No other device has one.
- **Where.** The local pipe `\\.\pipe\SpaceDeck.Screen`, hosted by the collector's service, in the binary framing of
  SpaceDeck's `docs/SCREEN-PIPE.md`. The plugin connects from a thread of its own (`SpaceDeckScreenClient`) as a
  read-only client at the identification level, checks that the server is the `SpaceDeckCollector` service's process
  before reading anything, and connects with the publisher's timing (250 ms, then a gap doubling from one second to
  thirty). Nothing is ever written to it. It starts only when the readings are sent and a Vision LCD is registered,
  and it has the publisher's off switch: no `SpaceDeckCollector` service, one log line, nothing more.
- **What is checked.** Every message (`SpaceDeckScreenDecoder`): the `SDSF` magic, version 1, kind 1, an id of 1 to
  128 characters by the catalog's id rule, a payload of 1 byte to 256 KiB that starts `FF D8` and ends `FF D9`, and no
  more than 4 messages a second on average (a burst of 8). A message that breaks a rule drops the connection; a frame
  for a device without a screen is ignored. Each kind of failure is logged once per plugin object.
- **How a frame reaches the screen.** The client puts the frame in the cooler's slot (`CoolerHandoff`, one per cooler
  for the life of the process): a newer frame replaces the one waiting, nothing queues, and one that has waited five
  seconds is dropped. Handing it over wakes that cooler's loop alone, which sends it after the duties and before the
  status poll, as 0x0E SendJpeg in 1024-byte B packets of 1013 payload bytes each (`Galahad2ScreenEncoder`), at most
  two frames a second, and never twice. Since a frame wakes the loop, the cooler's status is asked for at most every
  half second however often it is woken. The cooler answers the last packet with an echo, which nothing reads: every
  write the transport makes first discards the input queued for the handle, so the next write drops an echo that has
  arrived, and the status poll reads past one that arrives later (it takes only a reply that echoes 0x81). Reading the
  echo would cost the loop a read timeout whenever the cooler sends none. A frame is never the write that reopens a
  cooler that is off the bus: it waits (and expires) instead.
- **Failsafe.** The cooler shows its own screen again about 20 seconds after the last frame, so when SpaceDeck stops
  drawing - or FanControl, the plugin or the collector stops - the screen goes back by itself; nothing is undone. A
  frame write that fails costs only that frame (the next complete one puts the screen right) and is logged once until
  one is sent again; it never touches the duties or the status poll. Commands that could change the cooler for good
  (0x0F upload, 0x8E reset, 0x0D streaming, 0x0C test mode) are never sent.

In the code: the reading thread is another thread the plugin starts itself, and the frame write is guarded in
`Galahad2Controller` like the opt-in lighting path, a failure costing only the frame. The read-only pipe connection is
the `WindowsNamedPipeApi` adapter's, behind `INamedPipeApi.TryConnectForReading`.

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
