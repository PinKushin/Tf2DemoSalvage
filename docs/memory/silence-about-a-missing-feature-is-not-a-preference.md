---
name: silence-about-a-missing-feature-is-not-a-preference
description: Importing another program's config, a key it binds elsewhere is not a statement about a feature that program does not have.
metadata:
  type: project
---

**Loading a real TF2 config disabled three viewer controls, and honouring the config faithfully was
the cause.**

`ResetCamera` and `PlayPause` carry command names this project invented (`resetcamera`,
`playpause`), because TF2 has no equivalent for either. So **no TF2 config can ever bind them** — it
simply uses `f` and `k` for its own purposes, and the keys those actions lived on are taken away
with nothing put back. `+speed` behaves the same in practice: TF2 has no sprint, so the command
appears in essentially no config, while `bind "SHIFT" "+duck"` is completely ordinary.

**The rule: a key whose imported binding does nothing in this program keeps whatever this program
had on it.**

**The argument that was wrong, and it sounded principled:** the player said Shift is duck, so
overriding them is the viewer claiming to know better than the file it was told to obey. That is
right about `+duck` and wrong in general, because **a config cannot express a preference about a
feature the game does not have.** Reading its silence as one invents intent.

**Nothing is lost by falling back**, which is what makes it safe rather than a guess: the fallback
applies only when the imported command does nothing here, so the key was inert either way.

**But it must yield when the config rehomes the action.** `CTRL` = `+speed` alongside `SHIFT` =
`+duck` is a player *moving* fly-fast, not losing it — so Shift must stop doing it, or two keys
answer to one action and a settings screen picks arbitrarily. A conformance test caught that as a
wrong key rather than as a crash.

**How it was found, which is the reusable part.** Not by a test — by a *diagnostic* added for a
different purpose (report actions no key reaches) pointed at real data. It printed
`no key reaches: ResetCamera, PlayPause, FlyFast` on the first run against a real install. No
synthetic fixture could have shown it: every fixture was written by whoever wrote the parser, and
none binds `f` or `k`, because there is no reason to unless you are reading a file written by
somebody who had never heard of this program.

See [[output-level-assertion-or-it-is-not-done]] and [[a-config-is-a-program]].
