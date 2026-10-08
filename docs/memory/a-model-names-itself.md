---
name: a-model-names-itself
description: Files the engine derives from a model are named from studiohdr pszName, not the load path
type: project
---

**A file the engine derives from a model is named from the header's own `pszName`** (offset 12, e.g. `player/scout.mdl`),
not from the path it was loaded by (`models/player/scout.mdl`). `C_TFPlayer::InitPhonemeMappings` builds
`<pszName stem>/phonemes/phonemes` (c_tf_player.cpp:5276); building it from the load path found no file, fell back to
plain `phonemes`, and lip sync moved nothing while every unit test passed (B513). `StudioFlexData.ModelName` carries it.

**How to apply:** any lookup the SDK builds from `GetModelPtr()->pszName()` reads the header, never `ModelPath`.
Also from B513: a TF player's face rests at zero *in range* (ResetFlexWeights), not at each controller's minimum.
