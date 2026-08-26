# 37 — The engine's demo vocabulary, read out of the binary

**Written 2026-08-26.** Valve ships no source for demo playback: `CDemoPlayer` and the `demo_*`
commands are engine-side, and `source-sdk-2013` has none of them. But the *declarations* survive in
`bin/engine.dll` as ConVar and ConCommand constructor calls, and a constructor call is twenty bytes
of x86 that can be read by hand.

**Written up at the owner's instruction**, which is the reason this chapter exists at all:

> *"i just very much dislike whenever you leave reaserch available, so dont leave any available, it
> is always useful, even if not immedietly, so findings and engine quirks should be written down,
> the decomp rule is for disk space, so its fine to put all the knowledge about the engine you find
> to be put into memory files, even including small code snippets if code examples would help
> explain the finding"*

The immediate question was one convar's default. Answering only that would have left everything
below on the floor — including a correction to a decision made hours earlier.

**Evidence class: measured**, all of it, from the shipped `bin/engine.dll` (x86, image base
`0x10000000`). Nothing here is interpolated.

---

## 1. How to read a declaration

Source registers console entities as global constructors. Arguments push right-to-left, so at the
call site the *last* push before the `mov ecx` is the first argument — the name.

```asm
push 0x80              ; flags
push 0x102eb2f8        ; default value, as a STRING pointer
push 0x1032e4b8        ; name
mov  ecx, 0x1066f840   ; the object being constructed
call ConVar::ConVar
```

Finding it needs no Ghidra project. Get the name string's address, then byte-scan for a `push imm32`
of it — **there is exactly one hit**, because a name string is mentioned only by its own
initialiser:

```bash
grep -aboP '\x68\xb8\xe4\x32\x10' bin/engine.dll     # push 0x1032e4b8
```

**Count the pushes.** It is the fastest way to tell what you are looking at:

| pushes | shape |
|---|---|
| 3 | `ConVar(name, default, flags)` — **no help string** |
| 4 | `ConVar(name, default, flags, help)` |
| 5, with a `.text` pointer in slot 2 | `ConCommand(name, callback, help, flags, completion)` |

That last row is the one that catches people: a `ConCommand`'s second argument is a **function
pointer**, not a default. If slot 2 points into code rather than `.rdata`, it is a command.

**The trap that cost the most time: do not look for a default near the name.** Short literals are
string-pooled. `"50"` lives here —

```
34 30 30 00 34 35 30 00 35 31 32 00 31 30 38 30    >400.450.512.1080<
```

— in a shared block of numeric strings beside `dsp_speaker` and `voice_steal`, nowhere near any
convar that uses one. Reading bytes around a name finds its *neighbours'* help text and never its
own default. Only the pointer in the initialiser is authoritative.

---

## 2. What the demo commands actually are

**Most of them are commands, not variables** — which is not what this project assumed.

| name | kind | default | help |
|---|---|---|---|
| `demo_timescale` | **ConCommand** | — | "Sets demo replay speed." |
| `demo_gototick` | **ConCommand** | — | "Skips to a tick in demo." |
| `demo_setendtick` | **ConCommand** | — | "Sets end demo playback tick. Set to 0 to disable." |
| `demo_resume` | **ConCommand** | — | "Resumes demo playback." |
| `demo_togglepause` | **ConCommand** | — | "Toggles demo playback." |
| `demo_pause` | ConVar | `""` | "Pauses demo playback at server tick" |
| `demo_fastforwardstartspeed` | ConVar | `2` | "Go this fast when starting to hold FF button." |

**`demo_pause` being a ConVar with an empty default is the odd one**, and its help explains it: it
pauses *at a server tick*, so the value is a tick number and the empty default means "not armed".
Pausing right now is `demo_togglepause`, which is a command. Two spellings of "pause" that do
different things.

**`demo_fastforwardstartspeed` = 2** is the engine's own answer to a question this viewer has been
circling: what speed does fast-forward start at. Two.

---

## 3. `demo_timescale` is a command, and D97 said otherwise

**This corrects a decision made the same day.** D97 chose our playback-speed model after comparing
against Valve, and described the engine's console equivalent as *"`demo_timescale` — a float convar
whose help string reads 'Sets demo replay speed.'"*

The help string is right. **The kind is wrong**: five pushes with a `.text` pointer in slot two —

```asm
push 0                 ; completion callback
push 0                 ; flags
push 0x102f2588        ; help  -> "Sets demo replay speed."
push 0x100911e0        ; callback  <- a FUNCTION, so this is a ConCommand
push 0x102f25a0        ; name  -> "demo_timescale"
mov  ecx, 0x10467810
call ConCommand::ConCommand
```

**It has no default and no flags, because a command has neither.** It is invoked with an argument
and forgotten; nothing persists it, nothing clamps it at registration, and there is no value to read
back.

**Does this change D97's decision? No — and that is worth stating rather than leaving implied.** D97
chose continuous 0.01–8 with reverse, and its actual parity reference was the *slider* in
`CReplayPerformanceEditorPanel`, whose `TIMESCALE_MIN`/`TIMESCALE_MAX`/`SLIDER_RANGE_MAX` are read
from published source and are unaffected. What changes is the supporting sentence about the console
equivalent, which claimed a kind it never verified.

**The lesson is the one the owner named.** The convar/command distinction was available at the same
moment the help string was, from the same twenty bytes, and taking only the string left a wrong
claim in a decision document. Research not taken is not neutral — it is where the errors hide.

---

## 4. `engine_no_focus_sleep`, and why nobody knows it exists

```cpp
ConVar engine_no_focus_sleep( "engine_no_focus_sleep", "50", FCVAR_ARCHIVE );
```

Three pushes, so **no help string**. `FCVAR_ARCHIVE` is `(1<<7)`, *"set to cause it to be saved to
vars.rc"* (`public/tier1/iconvar.h:48`).

**It is archived — Valve persists it to the user's config — and it is undocumented.** Those two facts
together are why searching for it returns advice about lowering graphics settings, which is a
different mechanism entirely. The owner searched first and found nothing, which is exactly the
expected result: an undocumented convar and an internal one look identical from outside, and only
the flag separates them.

**50 milliseconds of sleep per frame while the engine lacks focus.** That is the behaviour this
viewer does not have (B209): our `OnDeactivate` releases held keys and nothing else, so the window
renders at full rate while alt-tabbed.

---

## 5. `fps_max` is not archived, which was not expected

```cpp
ConVar fps_max( "fps_max", "400", 0,
    "Frame rate limiter, cannot be set while connected to a server", <callback> );
```

Four arguments plus a change callback, and **flags `0` — `FCVAR_NONE`**. So `fps_max` is *not*
saved to config, while the undocumented `engine_no_focus_sleep` beside it *is*.

**Default 400**, read from the pooled block above rather than guessed.

**The help string is the one D99 declines to follow** — *"cannot be set while connected to a
server"* — on the owner's grounds that a demo viewer is never connected to one. Nothing here
disturbs that; it adds only that the restriction is enforced by the change callback at
`0x101da060` rather than by a flag, which is why no `FCVAR_` bit expresses it.

---

## 6. Where to go next with this

Everything above came from one region of `.rdata` and cost a handful of `dd` calls. The same recipe
answers any closed console entity, and two threads are worth pulling when they matter:

- **The callback addresses are the implementations.** `demo_timescale`'s is `0x100911e0`, and
  `demo_gototick`'s `0x100910b0` — these are consecutive, so the whole `demo_*` command family sits
  together around `0x10091000`. That is where `CDemoPlayer`'s command handling lives, and it is the
  part Valve ships no source for at all.
- **`0x10467810`, `0x104677c8`, `0x104677ec`** and neighbours are the ConCommand objects for
  timescale, gototick and setendtick. Following code references to those finds the dispatcher.

Neither is needed yet. Both are written down so they do not have to be found twice.
