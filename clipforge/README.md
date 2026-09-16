# ClipForge

A Windows screen recorder for game capture, in the style of Medal.tv: it keeps a rolling
**instant replay buffer** in the background so you can save the last 30 seconds *after* something
worth keeping already happened, plus ordinary start/stop recording, global hotkeys, a tray icon and
a clip library.

![ClipForge](src/ClipForge/Assets/app-256.png)

## What it does

- **Instant replay buffer** — continuously encodes the last *N* seconds (10–600, default 60) into a
  ring of segments on disk. One hotkey writes them out as an mp4.
- **Manual recording** — start/stop with a hotkey or a button, unlimited length.
- **Automatic game detection** — arms the buffer on its own when a fullscreen, non-browser,
  non-editor app takes the foreground, and disarms a while after you leave it.
- **GPU capture and encoding** — D3D11 Desktop Duplication (`ddagrab`) for display capture, and
  NVENC / AMF / Quick Sync when the hardware supports it, so recording costs the game very little.
- **System audio + microphone**, mixed with independent volume levels.
- **Clip library** — thumbnails, duration, size, play / reveal in Explorer / rename / delete.
- **Global hotkeys** that work while a game has focus, a tray icon, run-at-sign-in, and on-screen
  notifications when a clip is saved.

Defaults: 1080p, 60 fps, 20 Mbps, H.264, 60-second replay buffer, clips in `Videos\ClipForge`.

| Action | Default hotkey |
| --- | --- |
| Save replay | `Ctrl+Shift+S` |
| Start / stop recording | `Ctrl+Shift+R` |
| Arm / disarm replay buffer | `Ctrl+Shift+B` |

## Install

Every build installs per-user into `%LOCALAPPDATA%\Programs\ClipForge`, so Windows never asks for
administrator rights, and registers a normal Add/Remove Programs entry. Optional checkboxes add a
desktop shortcut and start ClipForge minimised at sign-in.

There are two editions of each artifact:

| File | Size | Needs |
| --- | --- | --- |
| `ClipForge-1.0.0-Setup-compact.exe` | 436 KB | .NET 8 Desktop Runtime — the installer offers to fetch it |
| `ClipForge-1.0.0-Setup.exe` | 46 MB | nothing; the runtime is bundled |
| `ClipForge-1.0.0-portable-compact.exe` | 888 KB | .NET 8 Desktop Runtime; no install, just run it |
| `ClipForge-1.0.0-portable.exe` | 64 MB | nothing; no install, just run it |

The **compact** builds are the ones to prefer if you already have the .NET 8 Desktop Runtime (many
PCs do) or are happy for the installer to download it — about 55 MB, once, straight from Microsoft.
The **standalone** builds bundle everything and work on a clean Windows install with no network
beyond ffmpeg.

Requirements: Windows 10 1809 or later, 64-bit.

### First launch: ffmpeg

ClipForge drives **ffmpeg** for capture and encoding and does not bundle it, because the GPL build
that carries `ddagrab` and the hardware encoders has its own licence. On first launch the app
offers to download it (~80 MB, once) into `%LOCALAPPDATA%\ClipForge\tools`. If you already have a
build you like, point ClipForge at it instead under **Settings → Use my own build**. Nothing is
downloaded without you clicking the button.

## How it works

```
                    ┌─ ddagrab (D3D11 Desktop Duplication)  ─┐
  screen ───────────┤                                        ├──► ffmpeg ──► NVENC / AMF / QSV / x264
                    └─ gdigrab (per-window, or fallback)     ─┘       ▲
                                                                      │ stdin (s16le)
  WASAPI loopback ──┐                                                 │
                    ├─► mix @ 48 kHz stereo ─► wall-clock pacer ──────┘
  WASAPI microphone ┘
```

Three details worth knowing:

**Audio pacing.** WASAPI loopback delivers *nothing at all* while the system is silent. Piping that
straight to ffmpeg yields an audio track shorter than the video, and the two drift apart. ClipForge
instead reads the mixer on a stopwatch schedule and pads silence, emitting exactly one second of
PCM per second of wall clock (`Core/AudioEngine.cs`).

**Crash-safe capture.** Both pipelines write **mpegts**, which stays valid even if the process is
killed mid-write. The mp4 you keep is always produced afterwards by a fast stream-copy remux, so a
power cut costs you the last segment, not the whole recording.

**The ring buffer** is ffmpeg's segment muxer with `-segment_wrap`, writing 2-second chunks. Saving
a replay copies the newest segments out (with `FileShare.ReadWrite`, since ffmpeg still holds the
current one open), concatenates them without re-encoding, and trims to the exact requested length.
Saving is therefore near-instant and costs no extra encoding.

If `ddagrab` fails to start — it does on RDP sessions, some locked-down GPU drivers, and against
protected content — ClipForge automatically retries the same pipeline on `gdigrab` rather than
leaving you with nothing.

### Source layout

| Path | What's in it |
| --- | --- |
| `src/ClipForge/Core/AudioEngine.cs` | WASAPI loopback + mic capture, mixing, wall-clock pacing |
| `src/ClipForge/Core/RecorderEngine.cs` | ffmpeg process lifecycle, replay ring, clip extraction |
| `src/ClipForge/Core/CaptureCommand.cs` | ffmpeg argument construction per backend and encoder |
| `src/ClipForge/Core/FFmpegManager.cs` | Locating, downloading and probing ffmpeg |
| `src/ClipForge/Core/CaptureTargets.cs` | Monitor/window enumeration, game heuristics |
| `src/ClipForge/Core/HotkeyManager.cs` | Global hotkeys via a message-only window |
| `src/ClipForge/UI/` | Dark-themed WinForms UI, tray, toasts |
| `installer/ClipForge.nsi` | NSIS per-user installer |

## Building from source

```bash
./build.sh
```

Produces all four artifacts in `dist/`.

The build works on **Linux as well as Windows** — .NET cross-compiles to `win-x64` and `makensis`
builds the installer natively. On Linux you need Microsoft's .NET 8 SDK rather than the distro
package: Ubuntu's `dotnet-sdk-8.0` omits the WindowsDesktop targets and cannot build WinForms.

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir /opt/dotnet
sudo apt-get install -y nsis
DOTNET=/opt/dotnet/dotnet ./build.sh
```

## Known limitations

- **Not yet run on Windows hardware.** It was written and compile-verified cross-building from
  Linux; the binaries are real Windows executables, but no one has clicked through the UI or
  recorded an actual game with it yet. Expect to shake out rough edges on first use. The log at
  `%LOCALAPPDATA%\ClipForge\clipforge.log` records the exact ffmpeg command line and its stderr,
  which is the fastest way to diagnose a capture that will not start.
- **Exclusive-fullscreen games** will not show the on-screen toast, and Desktop Duplication may be
  blocked entirely. Borderless windowed mode works properly, as it does for most capture tools.
  ClipForge has no injected in-game overlay.
- **Recording while the buffer is armed runs two encoders.** Medal-style single-pipeline `tee`
  output is not implemented, so doing both at once roughly doubles encoding cost. On a GPU encoder
  this is cheap; on x264 it is not.
- `ddagrab` captures a whole display only. Single-window capture always uses the slower `gdigrab`.
- No clip editor, trimming or upload. Clips are plain mp4 files in a folder.
- The build is unsigned, so SmartScreen will warn on first run.

## Licence

Application source: MIT. ffmpeg is downloaded separately at runtime and carries its own licence
(the builds ClipForge fetches are GPL).
