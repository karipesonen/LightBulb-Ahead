# LightBulb Ahead

A Windows fork of [Tyrrrz/LightBulb](https://github.com/Tyrrrz/LightBulb) with
independent morning/evening fades and exact-zero red-only gamma output.

## Changes From Upstream

- **Independent sunrise and sunset timing:** each fade has its own duration and
  target finish offset. Negative offsets finish before the solar event; positive
  offsets finish after it.
- **Long fades without the old limits:** requested durations are preserved. When
  fades would overlap, the effective duration is shortened to fit between target
  finishes, with the actual start/finish and shortening shown in settings.
- **Red-only at 500 K:** green and blue gamma ramps stay exactly zero, including
  driver refreshes. Naturally zero channels are preserved at every temperature;
  nonzero channels keep the upstream color mapping and refresh workaround.
- **Reliable small endpoint changes:** crossing zero/nonzero channels forces an
  update even below the old temperature/brightness significance thresholds.
- **Dated schedules:** runtime and cycle preview share resolved intervals across
  midnight and daylight-saving changes. Missing polar solar events use saved manual
  times with an explicit fallback notice.
- **Simpler fade settings:** separate morning/evening controls, clock-only timing
  summaries, no fade sliders, and no fractional-second noise in summaries.
- **Separate installation and settings:** the executable is still named
  `LightBulb.Fork.exe`; original settings are imported once without modifying them.
  Do not run Ahead and original LightBulb simultaneously.

This branch is based on upstream **2.7.2**. The latest locally tested Ahead package
is **2.7.2.6**; no Ahead binary release is published yet. The upstream download links
below install original LightBulb, not Ahead.

## Build And Update Ahead

See [the local build/update guide](scripts/README.md) for packaging, source updates
in isolated worktrees, verified installation, and settings-preserving rollback.
Vanilla upstream binary auto-updates are disabled so they cannot replace Ahead.

Duration examples: `02:30:00` is 2 hours 30 minutes; `1.06:00:00` is 30 hours.
Durations over 24 hours use an explicit day component. Dates are omitted from UI
timing summaries; a fade can start on the preceding day.

Verification: **80 tests passed** on Windows; installed checks covered gamma-ramp
readback, all refresh offsets, small zero-channel crossings, long-fade UI/preview,
pause/resume, normal exit, sleep/wake with polling on/off, and local update/rollback.
A non-no-op upstream merge was built and tested separately. SDR is the supported
target; HDR and external-monitor reconnection are not verified. Zero digital gamma
entries do not guarantee zero measured spectral emission.

## Original Project Documentation

The following documentation, credits, and download links are from upstream.

# LightBulb

[![Status](https://img.shields.io/badge/status-maintenance-ffd700.svg)](https://github.com/Tyrrrz/.github/blob/prime/docs/project-status.md)
[![Made in Ukraine](https://img.shields.io/badge/made_in-ukraine-ffd700.svg?labelColor=0057b7)](https://tyrrrz.me/ukraine)
[![Build](https://img.shields.io/github/actions/workflow/status/Tyrrrz/LightBulb/main.yml?branch=prime)](https://github.com/Tyrrrz/LightBulb/actions)
[![Coverage](https://img.shields.io/codecov/c/github/Tyrrrz/LightBulb/prime)](https://codecov.io/gh/Tyrrrz/LightBulb)
[![Release](https://img.shields.io/github/release/Tyrrrz/LightBulb.svg)](https://github.com/Tyrrrz/LightBulb/releases)
[![Downloads](https://img.shields.io/github/downloads/Tyrrrz/LightBulb/total.svg)](https://github.com/Tyrrrz/LightBulb/releases)
[![Discord](https://img.shields.io/discord/869237470565392384?label=discord)](https://discord.gg/2SUWKFnHSm)
[![Fuck Russia](https://img.shields.io/badge/fuck-russia-e4181c.svg?labelColor=000000)](https://twitter.com/tyrrrz/status/1495972128977571848)

<table>
    <tr>
        <td width="99999" align="center">Development of this project is entirely funded by the community. <b><a href="https://tyrrrz.me/donate">Consider donating to support!</a></b></td>
    </tr>
</table>

<p align="center">
    <img src="favicon.png" alt="Icon" />
</p>

**LightBulb** is an application that reduces eyestrain produced by staring at a computer screen when working late hours.
As the day goes on, it continuously adjusts gamma, transitioning the display color temperature from cold blue in the afternoon to warm yellow during the night.
Its primary objective is to match the color of the screen to the light sources of your surrounding environment — sunlight during the day and artificial light during the night.
**LightBulb** has minimal impact on performance and offers many customization options.

> ❔ If you have questions or issues, **please refer to the [wiki](https://github.com/Tyrrrz/LightBulb/wiki)**.

## Terms of use<sup>[[?]](https://github.com/Tyrrrz/.github/blob/prime/docs/why-so-political.md)</sup>

By using this project or its source code, for any purpose and in any shape or form, you grant your **implicit agreement** to all the following statements:

- You **condemn Russia and its military aggression against Ukraine**
- You **recognize that Russia is an occupant that unlawfully invaded a sovereign state**
- You **support Ukraine's territorial integrity, including its claims over temporarily occupied territories of Crimea and Donbas**
- You **reject false narratives perpetuated by Russian state propaganda**

To learn more about the war and how you can help, [click here](https://tyrrrz.me/ukraine). Glory to Ukraine! 🇺🇦

## Download

- 🟢 [**Stable release**](https://github.com/Tyrrrz/LightBulb/releases)
- 🟠 [CI build](https://github.com/Tyrrrz/LightBulb/actions/workflows/main.yml)
- 📦 [WinGet](https://winget.run/pkg/Tyrrrz/LightBulb): `winget install Tyrrrz.LightBulb` (community-maintained)
- 📦 [Scoop](https://scoop.sh/#/apps?q=LightBulb&p=1&id=9639ce8d7756b4c8a252368ef718c25b5a3b4ce0): `scoop install extras/lightbulb` (community-maintained)
- 📦 [Chocolatey](https://push.chocolatey.org/packages/lightbulb): `choco install lightbulb` (community-maintained)

> [!NOTE]
> Community-maintained packages are published independently from this repository and may not always be up to date with the latest release.

> [!NOTE]
> If you're unsure which build is right for your system, consult with [this page](https://useragent.cc) to determine your OS and CPU architecture.

## Features

- Extensive customization options
- Location-based sunrise and sunset times
- Manual sunrise and sunset times
- Whitelist for color-sensitive applications
- Global hotkeys for adjusting on the fly
- Smooth gamma transitions
- Minimal performance impact
- Works without internet connection

## Screenshots

![dashboard](.assets/dashboard.png)
![settings](.assets/settings.png)
