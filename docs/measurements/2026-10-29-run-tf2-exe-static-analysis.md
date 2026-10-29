# 2026-10-29 — what the 2007 client's `RUN_TF2.exe` does (static analysis)

## Observation

The downloaded 2007 client at `F:\tf2-builds\tf2-2007\Team Fortress 2\` ships `RUN_TF2.exe` beside
`hl2.exe` and a `steamemu.ini`. `docs/memory/a-client-starts-from-its-shipped-launcher.md` records
that raw `hl2.exe` froze or failed on 2007-era clients, so the launcher was assumed to do something
needed. Before driving it, the question was what it actually does and whether it can carry
`-condebug -usercon +ip … +net_start`.

## Question

1. What does `RUN_TF2.exe` launch, with which arguments, and does it pass its own command line on?
2. Does it read or write registry/files, inject, hook, wait, or use the network?
3. How can extra launch arguments reach `hl2.exe`?
4. Is it benign?

## Method

Static only; the file was never executed. Copied (no-clobber) to `D:\ghidra-proj\run-tf2\in\`,
imported and auto-analysed with Ghidra 12.1.2 headless (JDK 21), a `GhidraScript` dumped blocks,
imports with xrefs, defined strings with xrefs, and the disassembly of every function to
`D:\ghidra-proj\run-tf2\out\dump.txt` (stays on D:, not in any tree). Byte reads with `od`/`grep -a`.

## Control

The instrument must find what any Delphi 7 binary has: it found the Delphi RTL's own registry read
(`SOFTWARE\Borland\Delphi\RTL` / `FPUMaskValue`, HKLM, at `0x00402884`) and the RTL runtime-error
string — so the import/xref dump is seeing the whole program, and an absence in it is real.

## Environment

`RUN_TF2.exe`: 37,376 bytes, dated 2007-09-25.
SHA-256 `f0f030db4f97578c9d59650cd65ba5703dbfe782a8c86e79b1b89a0f0d0f0306`,
MD5 `4d4e6a5108ee6ef070715278fd02eca9`. Delphi-compiled PE32 (sections `CODE/DATA/BSS/.idata/.tls/
.rdata/.reloc/.rsrc`), version resource "Compile by impulse666", company/comment `http://csmania.ru`,
FileVersion 6.6.6.0. Not a Valve binary: a third-party repack launcher.

## Result

Evidence class in brackets: **[dis]** read from disassembly, **[str]** read from strings/bytes,
**[guess]** not verified.

- **The whole program is entry `0x004036c4`, ~25 instructions** [dis]:
  `ParamStr(0)` (helper `0x004026c0`, index 0 takes the `GetModuleFileNameA` branch) → scan for the
  last `\` and copy the prefix (`0x004035f8`, i.e. `ExtractFilePath`) → `ChDir` to it
  (`0x00402568` → `SetCurrentDirectoryA` at `0x00402580`) →
  `ShellExecuteA(NULL, "open", "hl2.exe", "-game tf -steam -console", NULL, SW_SHOWNORMAL)` at
  `0x0040370c` (strings at `0x00403758`, `0x00403750`, `0x00403734`) → return, process exits.
- **It drops its own command line** [dis]: `GetCommandLineA` is only reachable from `ParamStr(n)`
  with n ≠ 0, and the only `ParamStr` call passes 0. The parameters string is a constant. Why it was
  written this way is not in the binary [guess: a one-line "double-click to play" shortcut].
- **No wait, no sleep, no watchdog** [dis]: `ShellExecuteA` returns and the launcher exits; it
  imports no `Sleep`/`WaitFor*`/`CreateProcess` at all, so it never holds a handle on `hl2.exe`.
- **Registry**: only the Delphi RTL's HKLM `FPUMaskValue` read [dis]. No `HKCU\Software\Valve\Steam`,
  `ActiveProcess`, `SteamClientDll` or `SteamPath` access; no `RegSet*`/`RegCreate*` imported [dis].
- **Files**: none read or written; no `CreateFile`/`ReadFile` imported (`WriteFile` is only the
  RTL's stderr error path) [dis]. No cfg, no ini, no patching.
- **No injection or hooks**: no `WriteProcessMemory`, `CreateRemoteThread`, `VirtualProtect`,
  `SetWindowsHookEx`, `LoadLibrary` imported [dis]. One `MZ`, no embedded PE, no manifest [str].
- **No network**: no ws2_32/wininet import; the only "steam" text is the `-steam` argument [str].
- **Environment variables**: none read [dis].
- **No ini or args file**: no `GetPrivateProfile*`, `CreateFile` or `ReadFile` imported [dis], so
  the empty `[SPK Game Launcher]` section in `steamemu.ini` is not read by this launcher [dis]; it
  belongs to the emulator or the repacker's tooling [guess].
- **`lpDirectory` is NULL**, so `hl2.exe` inherits the cwd just set — the client root, not `bin\`
  [dis]. A harness that starts `RUN_TF2.exe` and waits on that PID sees it exit within milliseconds
  while the game is still starting; that is not a failed launch [dis].

The folder also holds `steamemu.ini` (`[Emulator] Extracted=1`) and `bin\steam.dll`
(SHA-256 `33cf5b2e…683fb7`) whose strings include `STEAMemu`, `SteamEmu_GetGcfList`,
`SteamEmuPremount`, `steamemu.ini`, imports of `WS2_32`, and references to `engine.dll`, `Sleep`,
`VirtualProtect`, `connect` [str]. **`hl2.exe` loads that DLL, not the launcher.** It was not
disassembled here; it is the only candidate in this folder for a hang or a spinning core after
startup [guess].

## Verdict

`RUN_TF2.exe` is a benign one-call shortcut — `cd` to its own folder and
`ShellExecute hl2.exe -game tf -steam -console` — so it cannot carry extra arguments, and **the
minimal equivalent that does is to start `hl2.exe` directly, with the working directory set to the
client folder, passing `-game tf -steam -console` plus the extra arguments** (e.g.
`-condebug -usercon +ip … +net_start`). Anything a raw `hl2.exe` launch did differently from this
launcher must therefore be the working directory or the three arguments, not anything the launcher
sets up; a remaining hang belongs to the emulator `bin\steam.dll`, which is the next thing to read.
