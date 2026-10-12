---
name: one-keyvalues-tokenizer
description: "Every reader of Valve KeyValues text goes through KeyValuesTokens (a port of KeyValues::ReadToken); conditionals are evaluated, never skipped or taken; material `name?$key` holds only for hdr and srgb (B524)."
metadata:
  type: project
---

**There is one tokenizer for Valve's KeyValues text: `KeyValuesTokens` (`Content/Assets`).** `KeyValuesTree`,
`KeyValuesReader`, `VmtMaterial.Parse`, `ScriptKeyValue` and `GameSearchPath` all use it. Do not write a fifth.

What the four old ones each got wrong, and what it changed (census: `kv-census` probe, numbers in RISKS B524):

- **An empty quoted value `""` ended the VMT parse** — 7 shipped materials lost 190 keys after it.
- **A `360?$key` twin overwrote the PC value** — 83 keys. The condition is evaluated in `materialsystem.dll`
  (`0x180008d20`): of `lowfill`, `hdr`, `srgb`, `ldr`, `360` only **`hdr` and `srgb` hold** on this client, and `!`
  negates. `srgb?$overbrightfactor 3` on `medicbeam_curl` is therefore applied.
- **`[$WIN32]`-style tags were read as values** by the stream reader (sound scripts).
- **A commented-out key was read as set** (B523, the class scripts).

**A reader that "ignores unknown things" silently is how each of these lived**: nothing threw. When a material or
script value looks wrong, run `vmt <path>` / `kv-census` and compare "as shipped" with "as this project reads it"
before touching the consumer.

**Not every Valve text file is KeyValues — ask which reader the ENGINE uses before picking ours (B532):**

- localisation: vgui2.dll's own, UTF-16 (`VguiLocalize`, ported from disassembly).
- HUD animation script: `ParseFile`, filesystem_helpers.cpp:29 (`VguiAnimationController`).
- `.cfg`: `GetNextCommandLength` then `CCommand::Tokenize`, break set `{}()':`, `""` IS an argument (`SourceConfig.Tokens`).
- `.phy` text and `surfaceproperties.txt`: vphysics' tokenizer, lowers key AND value, `/* */` comments (`VphysicsTokens`).

Census probes that stay: `kv-census`, `cfg-census` (old tokenizer frozen inside), `phy-census [nocase]` (digest per file;
run before and after a reader change and diff).
