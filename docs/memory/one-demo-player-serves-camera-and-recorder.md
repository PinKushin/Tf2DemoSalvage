---
name: One demo player serves the camera and the recorder
description: InterpolateViewpoint is one owner per open demo (DemoPlayer); camera and recorder body read it at the fractional tick
type: project
---

`CDemoPlayer::InterpolateViewpoint` is ported as `Core/Scene/DemoPlayer.cs` (B56, B442; finding 68). The
engine has one `demoplayer`, and its single per-frame call feeds the camera, the recorder's origin and his
eye angles. So `DemoSystems.Open` makes ONE `DemoPlayer` and hands it to both `TimelineEyes` and
`TimelineMoments.Player`. It holds state (last target tick, reset flag, parse-ahead list), so it must not
live on the shared `DemoTimeline`.

**How to apply:** a POV camera or recorder question takes the FRACTIONAL tick (`_shownTick`), never
`_transport.CurrentTick`. Don't add a second interpolation route. Seeks do not reset it — the engine's
`SkipToTick` does not either; a backward move restarts the reader. Remaining divergences: B450.
