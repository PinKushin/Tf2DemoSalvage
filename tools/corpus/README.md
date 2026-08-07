# tools/corpus

Reference demos used for development and regression testing. Every entry needs an entry in `manifest.json` and, once Phase 1 exists, a matching fixture in `../../tests/`.

Corpus growth is opportunistic, not a blocker — see `../../docs/DECISIONS.md` D5. Currently one confirmed demo (`demos/z1800.dem`).

Every entry's `sha256` is computed at the time it's added, so the corpus can detect if a reference file is ever silently altered — compute it (`sha256sum`) for any new demo before writing its manifest entry.

If this folder grows large, migrate to Git LFS (`git lfs install && git lfs migrate import --include="*.dem"`) — not set up yet since it wasn't available in the scaffolding environment.
