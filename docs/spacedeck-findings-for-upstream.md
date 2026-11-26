# Findings for upstream (lewisgibson/FanControl.LianLi)

Measured on real hardware for SpaceDeck's fork, collected here so they can go back to the upstream maintainer, who
writes that he owns no Galahad II LCD and no wireless kit. Everything below is from one PC (Windows 11, FanControl
V286, net48 build, plugin running in both the app and the service) unless marked otherwise. Dates are 2026-11-26.

## The hardware

- **Lian Li GA II LCD TL WIRELESS 280** (model GA2ALCD28TL; Lian Li sells it as "GA II LCD", the Galahad II LCD):
  the AIO with its 2.88" 480×480 LCD pump head, sold with two **UNI FAN TL V2 wireless** radiator fans on an
  L-Wireless dongle pair (0416:8040 / 0416:8041). The radiator fans are not wired to the cooler.
- Cooler USB id **0416:7395**, product string `LianLi-GA_II-LCD_v1.6`.
- Firmware query (`0x86`) answer, two packets: `N9,01,HS,SQ,CA_II-Vision,V2.01.041,1.6` and `Oct 30 2025,17:01:18`.
- The plugin's catalog calls 0x7391/0x7395 "Galahad II Vision". "Vision" is the firmware codename; the retail name
  is "GA II LCD" (liquidctl also checks for `CA_II-Vision` in the firmware string). The plugin's numbered names
  ("Lian Li Galahad #2") read like a model name when the wireless group takes index 0; SpaceDeck's fork names this
  cooler "Lian Li GA II LCD …" instead (ids unchanged).

## 1. The cooler has one HID collection, on usage page 0xFF1A (so the plugin dropped it)

- Windows exposes **exactly one** HID collection for 0416:7395: usage page **0xFF1A**, usage 0x93, input report
  **512** bytes, output report **1024** bytes, no feature reports. There is no 0xFF1B interface.
- `CommandInterfaceFilter` keeps only 0xFF1B for vendor 0x0416, so plugin v1.1.38 and v1.1.44 never opened the
  cooler: FanControl showed no Galahad sensors at all.
- The one interface carries three report ids (as lian-li-linux's `hydroshift_lcd/protocol.rs` describes): 0x01 =
  the 64-byte command packets, 0x02 = 1024-byte and 0x03 = 512-byte screen data.
- On Windows the plugin's existing transport works unchanged on it: writes padded with zeros to the 1024-byte
  output length (as hidapi does) are accepted, and replies arrive as 512-byte input reports whose first 64 bytes are
  the command packet. Verified for 0x81 (status), 0x86 (firmware) and 0x8A (set pump).
- Fix in the fork: `5e2dccc` "fix: open the Galahad II Vision LCD's single HID collection" (keeps 0xFF1A for
  0x7391/0x7395 only; every other 0x0416 device keeps the 0xFF1B rule).

## 2. The status reply carries the coolant temperature

- The 0x81 reply has **payload length 7** on this cooler, every time. Payload: fan RPM (BE, 0–1), pump RPM (BE, 2–3),
  **coolant valid flag (4), whole °C (5), tenths (6)** — the layout liquidctl and lian-li-linux read.
- Captures (frame bytes 0–15):
  - `01 81 00 00 00 07 00 00 0b 5e 01 15 06 00 00 00` = fan 0 rpm, pump 2910 rpm, valid, **21.6 °C**
  - `01 81 00 00 00 07 00 00 0b 7c 01 15 05 00 00 00` = pump 2940 rpm, 21.5 °C
- The cooler's own screen agreed (room at 18 °C, PC idle). Later, under load, FanControl showed 25.4 °C.
- Fan RPM is 0 because the kit's fans are wireless; nothing is on the cooler's fan header.
- 26 replies to 27 queries at one query a second; the cooler sends nothing unasked.
- The plugin decoded only the first 4 payload bytes, so it never showed coolant.
- Fix in the fork: `84f4a27` "feat: report the Galahad II Vision LCD's coolant temperature" (gated to 0x7391/0x7395:
  on the Trinity those bytes are not a temperature, issue #30).

## 3. The pump follows the motherboard header when told to (source byte 1)

- `01 8A 00 00 00 02 01 64` (set pump, source 1 = follow the motherboard PWM on the cooler's pump lead) works on
  0x7395: with it sent, moving FanControl's motherboard "Pump Fan" control between 60 % and 100 % moved the cooler's
  pump RPM accordingly ("pump controlled properly", the owner).
- Before that, with the cooler not driven by any program, the pump ran at ~2,910–2,940 rpm on its own setting and
  ignored the header (the header only read its tach).
- Why SpaceDeck's build hands the pump to the header: docs/protocol.md notes a USB-set pump duty persists with no
  thermal failsafe (measured on the Trinity, #30). A header-driven pump keeps following the board, and the BIOS curve,
  if FanControl or the plugin stops. The fork re-sends that packet every 15 s and after reconnects, and registers no
  pump control for 0x7391/0x7395 (`368e8b5`). This is a policy choice, not offered upstream as is; an opt-in
  version might be worth considering.
- Pump range for 0x7395 per lian-li-linux (`lianli-shared/src/aio.rs`): 800–3,600 rpm, so the plugin's 50 % pump
  floor (sized for the Trinity) is ≈ 2,000 rpm on this cooler.

## 4. TL V2 wireless fans on real hardware

- Two UNI FAN TL V2 wireless fans, one group (`6232e1`), on the L-Wireless dongle.
- **v1.1.38**: speeds and curves worked, but the fans **jumped up and down instead of ramping smoothly**.
- **v1.1.44** (built unchanged apart from name and version as SpaceDeck build v2.0.1): **the jumping is gone**.
  Probably the "read TL and Galahad RPM only from the reply to the current poll" change (e1f98e9) or others between.
- So: TL V2 wireless is confirmed working on real hardware with v1.1.44 (speeds, curves, smooth control).

## 5. Things seen that upstream may want to know

- The hand-copied `FanControl.LianLi.Lighting.dll` (four-part name, v1.1.38) was the owner's install; FanControl's
  Install button rejects that name, as docs/deployment.md says.
- FanControl ran the plugin both as `FanControl.exe` (session 1) and `FanControl.Service` (session 0) on this PC at
  different times.
- Not tested yet: the LCD screen (left alone), the ring/fan lighting replay on this cooler, the HydroShift LCD ids
  (0x7398–0x739A; lian-li-linux reads their coolant the same way and marks them tested, so they likely need the same
  coolant and 0xFF1A handling).

## Offered upstream (when the owner approves the pull requests)

1. `5e2dccc` — open the single 0xFF1A collection for 0x7391/0x7395.
2. `84f4a27` — coolant temperature for 0x7391/0x7395.
3. This document's measurements, as an issue or in the PR descriptions.

Not offered: the pump-follows-header policy (`368e8b5`), the "GA II LCD" names (`7a15642`), the SpaceDeck publisher
and build workflow (SpaceDeck-specific).
