---
name: silence-about-a-missing-feature-is-not-a-preference
description: Importing another program's config, a key it binds elsewhere is not a statement about a feature that program does not have.
metadata:
  type: project
---

**Loading a real TF2 config disabled three viewer controls, and honouring it faithfully was the
cause.** Two viewer actions have no TF2 equivalent, so no TF2 config can ever bind them — the config
just reuses those keys for its own purposes, and the viewer's action loses its key with nothing put
back.

**Rule: a key whose imported binding does nothing in this program keeps whatever this program had on
it.**

**The wrong-sounding-principled argument:** overriding a key the config explicitly binds elsewhere
(e.g. duck) would be "the viewer claiming to know better than the file". Right about an actually-bound
key, wrong in general — **a config cannot express a preference about a feature the game doesn't
have.** Reading silence as a preference invents intent.

**It must yield when the config REHOMES the action** — moving a viewer action to a different key
alongside its existing binding means the old key must stop doing it, or two keys answer to one action.

**Found by a diagnostic pointed at real data** (report actions no key reaches), not a test — no
synthetic fixture binds the keys involved, since nobody writing one has a reason to unless reading a
file from someone unaware of this program.

See [[output-level-assertion-or-it-is-not-done]], [[a-config-is-a-program]].
