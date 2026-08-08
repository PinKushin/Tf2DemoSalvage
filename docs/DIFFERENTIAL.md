# Cross-parser differential testing

Comparing this parser's output against an independent implementation.

## Why it exists, when everything else already passes

Every other check in this project is internal. Decoders agree with each other, or with values
the demo states about itself: the map name in the header against the one in `svc_ServerInfo`,
the class count against `MaxClasses`, the packet count against the declared frame count.

Those are good checks and they caught real bugs. But they share a blind spot — **they cannot
catch a self-consistent misunderstanding.** If the format were read wrongly in a way that
stayed internally coherent, nothing here would notice.

That matters most for entity property values, which is precisely where a wrong answer is a
plausible number rather than a broken structure (`RISKS.md` B4). A player standing three feet
to the left looks exactly like a player standing in the right place.

## The oracle

[`tf-demo-parser`](https://codeberg.org/demostf/parser) — the Rust parser behind demos.tf, MIT
OR Apache-2.0. It handles the same multi-year corpus this project targets, which makes it the
strongest available second opinion for TF2 specifically.

[`UntitledParser`](https://github.com/UncraftedName/UntitledParser) (C#, MIT) was considered
and would need no toolchain, but targets HL2 and Portal rather than TF2.

## Setting it up

Requires a Rust toolchain. **Install rustup natively on Windows, not in WSL** — WSL pays a
filesystem translation penalty on `/mnt/c` and buys nothing here.

```bash
winget install --id Rustlang.Rustup
git clone --depth 1 https://codeberg.org/demostf/parser.git tf-demo-parser
cd tf-demo-parser
cargo build --release --bin parse_demo --bin gamestate
```

MSVC build tools are needed for the default `x86_64-pc-windows-msvc` toolchain; Visual Studio
or the standalone Build Tools both provide them. A cold build takes around 90 seconds.

Then point the tests at the binary:

```bash
setx TF2DEMOSALVAGE_ORACLE "<path>\tf-demo-parser\target\release\parse_demo.exe"
```

**Note that `cargo` is not on PATH in a fresh non-login shell** — it lives in
`%USERPROFILE%\.cargo\bin`.

## What is compared today

Ten header fields, across every corpus demo: demo protocol, network protocol, server name,
client name, map, game directory, playback time, ticks, frames, and signon length.

That is a modest overlap, and it is all both parsers currently produce in comparable form. It
is still worth having: it is the only assertion in the suite that could catch this project
misreading the container in a way its own cross-checks would accept.

## What comes next, in order of value

1. **Entity property values.** The reason the harness exists. `parse_demo` reports a match
   summary; the `gamestate` binary in the same repository reports per-tick player state, which
   is the closer comparison. This lands with property decoding, not after it.
2. **Chat.** `parse_demo` already extracts it, and chat arrives as a `SayText2` user message —
   the one part of the format that is *not* self-describing (`RISKS.md` B1). Having an oracle
   for it makes reverse-engineering that layout far cheaper.
3. **Game events.** Both sides can enumerate them once ours are reachable.

## Skipping is reported, never silent

The oracle is optional and most machines will not have it. Tests that need it return early
rather than failing — but they print that they skipped, because a differential test that
quietly passes without comparing anything is worse than one that fails.
