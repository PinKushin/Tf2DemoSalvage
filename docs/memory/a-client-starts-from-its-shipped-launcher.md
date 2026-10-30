---
name: a-client-starts-from-its-shipped-launcher
description: "A period TF2 client is started from the .bat/.lnk/loader it ships with, found FIRST; a raw hl2.exe start is the last resort and froze or failed on 2007, 2008, 2009 and 2011."
metadata:
  type: feedback
---

Owner, 2026-10-29: *"the agent should always look for a bat or shortcut first"*.

**Why:** raw `hl2.exe` starts (Tf2Mcp's own `UseShellExecute=false` launch, or `Start-Process hl2.exe`) failed or froze on
four period builds in one session: 2008 died with `CFileSystem_Steam::Init() failed: failed to find steam interface` (the
build loads `steam.dll`/`Steam006`; the shipped `Start Team Fortress 2.lnk` works), 2007 froze silently after the config
banner (the owner's own manual start worked), 2011's Tf2Mcp defaults made it shut down (its `.bat` blanks
`HKCU\Software\Valve\Steam\ActiveProcess\SteamClientDll` first), and 1.1.0.0 errors without `gameinfo.txt` unless started
through its `.bat` -> `SmartSteamLoader.exe` (`SmartSteamEmu.ini` `CommandLine = -steam -game tf`).

**How to apply:** before any launch, list the client folder for `.bat`/`.cmd`/`.lnk`/loader exes and ini files, read them,
and start the client the way it ships; put the Tf2Mcp's RCON arguments (`-condebug -usercon +ip 127.0.0.1 +hostport P
+rcon_password PW +net_start`) into that launcher's own command line (hash the original first, restore it after), then
attach. A launcher that needs the shell or the owner's desktop context is a human-click step: prepare it and ask. Related:
[[tf2-is-a-shared-fixture]], [[where-the-game-and-clients-live]].
