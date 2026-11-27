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
  more than 30 messages a second on average (a burst of 60). A message that breaks a rule drops the connection; a frame
  for a device without a screen is ignored. Each kind of failure is logged once per plugin object.
- **How a frame reaches the screen.** The client puts the frame in the cooler's slot (`CoolerHandoff`, one per cooler
  for the life of the process): a newer frame replaces the one waiting, nothing queues, and one that has waited five
  seconds is dropped. Handing it over wakes that cooler's loop alone, which sends it after the duties and before the
  status poll, as 0x0E SendJpeg in 1024-byte B packets of 1013 payload bytes each (`Galahad2ScreenEncoder`), at most
  thirty frames a second, and never twice. A frame is sent between two ticks' duties, so it delays a fan or pump
  write by at most one frame's transfer. Since a frame wakes the loop, the cooler's status is asked for at most every
  half second however often it is woken. The cooler answers the last packet with an echo, which nothing reads: every
  write the transport makes first discards the input queued for the handle, so the next write drops an echo that has
  arrived, and the status poll reads past one that arrives later (it takes only a reply that echoes 0x81). Reading the
  echo would cost the loop a read timeout whenever the cooler sends none. A frame is never the write that reopens a
  cooler that is off the bus: it waits (and expires) instead.
- **What it costs.** Once a minute while frames are sent, `plugin.log` has one line per cooler (`ScreenFrameTally`):
  `G0: screen frames in the last minute: 1740 sent, 12 dropped before sending (replaced by a newer one or expired),
  21.4 ms average and 48.0 ms longest to write one, 38211 bytes average`. A screen nobody draws on logs nothing.
- **Failsafe.** The cooler shows its own screen again about 20 seconds after the last frame, so when SpaceDeck stops
  drawing - or FanControl, the plugin or the collector stops - the screen goes back by itself; nothing is undone. A
  frame write that fails costs only that frame (the next complete one puts the screen right) and is logged once until
  one is sent again; it never touches the duties or the status poll. Commands that could change the cooler for good
  (0x0F upload, 0x8E reset, 0x0D streaming, 0x0C test mode) are never sent.

In the code: the reading thread is another thread the plugin starts itself, and the frame write is guarded in
`Galahad2Controller` like the opt-in lighting path, a failure costing only the frame. The read-only pipe connection is
the `WindowsNamedPipeApi` adapter's, behind `INamedPipeApi.TryConnectForReading`.

## Lighting through OpenRGB

This build (the Lighting variant) also serves the Galahad II Vision LCD's screen ring - the twelve LEDs round the
screen - to [OpenRGB](https://openrgb.org) through OpenRGB's own SDK network protocol, so OpenRGB can set it beside
everything else it drives. The plugin stays the only program that opens the cooler.

**Connecting OpenRGB.** In OpenRGB, open **Settings → SDK Client**, add the server `127.0.0.1` port `6743`, and
connect. The cooler appears as a device named `Lian Li GA II LCD` (type Cooler) with one zone, `Screen ring`, of 12
LEDs. The port is 6743, not OpenRGB's own 6742, so OpenRGB's own SDK server can run beside it. The plugin log says
`OpenRGB: SDK server listening on 127.0.0.1:6743` once it is up.

- **Modes.** Direct (one colour per LED, sent as 0x14 SetLEDColors on the screen path), Off, Static, Breathing,
  Rainbow, Rainbow Morph, Runway and Meteor (the cooler's own effects, sent as 0x83 SetPumpLighting). Speed and
  brightness are 0 to 4, the cooler's own scale; Static, Breathing, Runway and Meteor take 1 to 4 colours of their
  own; Rainbow, Runway and Meteor go left (counter-clockwise) or right (clockwise). Every 0x83 is sent with the scope
  byte at outer (1): on the Vision only outer or all light the ring, and inner, which L-Connect writes, shows nothing.
- **How a command reaches the ring.** Nothing a client sends is written to the cooler as it is: a command only
  changes the server's view of the ring, which hands the resulting look to the cooler's own worker thread through the
  same slot as the screen frames (`CoolerHandoff`, newest wins), and the worker builds the packets itself
  (`Galahad2RingEncoder`). An effect is written at least a second after the last one (several in quick succession
  can leave the ring on an older one), a Direct update at most ten times a second; one that is not due yet waits,
  replaced by any newer one. A write that fails costs only that look, and is logged once until one is sent again.
- **Saving.** OpenRGB's "Save to device" (SAVEMODE) keeps the look in `openrgb-looks.json` beside
  `remembered-controllers.json` in `%LOCALAPPDATA%\FanControl.LianLi` (or the SYSTEM profile's, when FanControl
  runs as its service). A saved look - and, until FanControl restarts, the last look set through OpenRGB - is shown
  again at each start and after every reconnect instead of L-Connect's saved ring; L-Connect's fan light is still
  replayed. A file that is missing, locked or corrupt is logged and L-Connect's ring is replayed as before.
- **Protocol.** The server speaks SDK protocol versions 0 to 6, agreeing on the lower of the client's and 6, and
  describes the controller in the layout of the agreed version (checked field by field against lian-li-linux's
  server). It answers REQUEST_CONTROLLER_COUNT, REQUEST_CONTROLLER_DATA (in the version the request asks for),
  REQUEST_PROTOCOL_VERSION, SET_CLIENT_NAME and the profile and plugin list requests (empty), and acts on
  UPDATELEDS, UPDATEZONELEDS, UPDATESINGLELED, SETCUSTOMMODE, UPDATEMODE and SAVEMODE; any other packet is
  ignored, as OpenRGB's own server ignores one. The controller list is fixed for the life of a server: the plugin
  starts one per plugin object (Load) and stops it at Close, so a FanControl refresh disconnects the clients and
  OpenRGB reconnects to the new one by itself; DEVICE_LIST_UPDATED therefore never falls due.
- **Treated as hostile.** The listener is on the loopback address only, but it may run as LocalSystem (inside
  `FanControl.Service`) and any program on this PC can connect. So: at most 4 clients (another is closed as it
  connects), a packet is at most 64 KiB and must arrive whole within 10 seconds of its first byte, and every packet
  must decode exactly - its size fields, no bytes left over, a controller, zone, LED and mode the ring has, the
  mode's own name, colour mode, colour count and direction. Anything else closes that client; each kind of failure
  is logged once per server. An idle client stays connected, as OpenRGB's clients do.

In the code: the OpenRGB types are in `src/FanControl.LianLi/OpenRgb/` (namespace `FanControl.LianLi.OpenRgb`,
Plugin layer); the sockets are the Transport adapter `WindowsLoopbackListenerApi`, behind `ILoopbackListenerApi`.
The server's threads are threads the plugin starts itself, and the ring write is guarded in `Galahad2Controller`
like the opt-in lighting path, a failure costing only that look.

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
