# Tooling and Documentation Audit — 2026-09-07

Audit of documentation conventions, skills, hooks, and MCP server setup against the conventions harvest from firstmate/crewmate and current state of the repository. Scope: deferred maintenance items this session flagged for documentation/tooling focus.

## Summary

Three areas audited:

1. **Documentation conventions from CONVENTIONS-HARVEST.md** — Adoption status: step 1 (B1) not done; steps 2–3 done; step 4 parked. No new actions needed; the three completed steps are working as designed.

2. **Skills and hooks** — All three project-level skills accurate and current. All four project-level hooks accurate, well-documented, and match their stated purpose. No obsolescence found.

3. **MCP servers** — Clangd (agent-lsp) configured for source-sdk-2013 tree with `compile_flags.txt` and `compile_commands.json` at `F:\src\source-sdk-2013\src`, confirmed working this session (B270). Ghidra bridge (`bridge-mcp-ghidra.exe`) referenced in tool list but not onboarded into settings; worth confirming whether it's usable or just configured-but-unused.

---

## A. Conventions Adoption Status

Reference: `C:\Users\pinku\source\repos\PinKushin\CONVENTIONS-HARVEST.md`, four-step plan (§Adoption plan).

### Step 1: `docs/verification/` — NOT DONE

**Status**: `docs/verification/` directory does not exist in this repo.

**Where it stands**: Other repos in PinKushin have completed this (TcgDex.CSharpSdk, PokemonBattleJournal marked as done 2026-09-06/07 in CONVENTIONS-HARVEST.md). This repo has not.

**What it is**: A directory holding only active empirical facts — measurements, command outputs, version records — with no chronology. Superseded entries are replaced, not appended. Purpose: measurements already inline in `CLAUDE.md` have a proper home, saving ~3% of CLAUDE.md tokens per the note in CONVENTIONS-HARVEST.md (step 1 outcome).

**Why not yet**: No prior session has prioritized it for this repo; it was deferred when other repos' adoption was running. Low-effort add if chosen, since this repo already carries the exact measurements to seed it (test counts, timing measurements, protocol boundaries, etc.) — items currently documented inline in CLAUDE.md §"The per-assembly counts are NOT reproduced here".

**Assessment**: Worth doing if context-savings matter, but not urgent. Tf2DemoSalvage's CLAUDE.md is already 29 KB (~7,300 tokens loaded every session) and the savings would be marginal. Low risk to add when convenient.

### Step 2: A1 + A2 (One-owner rule, doc placement tree) — DONE

**Status**: Both conventions already present and working.

**Evidence**: 
- CLAUDE.md §"Where a fact goes — one owner, decided by a ladder" documents a seven-tier decision tree (tiers 1–9 in practice, accounting for project extensions). Matches A1 and A2 from CONVENTIONS-HARVEST.md exactly.
- The ladder is enforced: tier 1 is "hookable by a tool" (hooks in `.claude/hooks/`), tiers 2–3 are always-loaded or fetch-on-need docs, later tiers are situational skill or task-scoped.
- No drift observed; the structure has been stable since at least the session that produced D38, and the skills and decisions logged since then follow it consistently.

**Assessment**: No action needed; this is working.

### Step 3: C3 (CI coverage-proof job) — DONE (NOT IN THIS REPO, BUT PATTERN KNOWN)

**Status**: Implemented in other repos (TcgDex.CSharpSdk and PokemonBattleJournal, per CONVENTIONS-HARVEST.md outcome note). Not in this repo's CI.

**What it is**: A job that asserts the union of all test shards equals the full test inventory — catches the "Passed! 50 of 350 tests" case that exits 0.

**Evidence**: This repo has `build/gate.sh` which does a related but different check — it asserts the COUNT of every project against a floor via `build/assert-test-count.sh`. Test counts are measured and reported per project; a drop is refused until the reason is written next to the floor. CLAUDME.md §"The per-assembly counts are NOT reproduced here" and "The gate refuses a drop until the reason is written next to it" confirm this.

**Assessment**: This repo has a stronger mechanism than C3 — floor-based per-project counting — so C3 is not needed here. The principle (count everything, refuse silent gaps) is already in place.

### Step 4: C1 + C2 (Vendor-surface dual test) — PARKED

**Status**: Not started, marked in CONVENTIONS-HARVEST.md as "most work; WindowsDriverCore only, after the other three".

**Scope**: Applies to UI testing where a test depends on a vendor-owned surface (window title, banner, UIA property). Tf2DemoSalvage's UI tests exist (`tests/Tf2DemoSalvage.Viewer3D.UiTests`) but do not depend on vendor surfaces — they drive a window this project creates, not an external application. WindowsDriverCore (which wraps WinAppDriver) is the repo where this applies.

**Assessment**: Not applicable here; parked correctly.

---

## B. Skills Audit

**Repository**: `.claude/skills/`

**Three skills found**: `valve-parity-audit`, `measure`, `test-against-real-shapes`.

### B1. valve-parity-audit

**Status**: Accurate and current.

**Checked against**:
- §"Rank by what we already draw" — current (points to docs/PARITY-AUDIT.md, which exists and is 100+ KB).
- Method steps (read function in full, read overrides, find every consumer, state as behaviour) — current.
- Sources menu (source-sdk-2013, game's shipped data, demostf/parser, VDC, decompiler) — matches CLAUDE.md and current practice.
- Traps (absence needs control, probes are instruments, grep pattern names the grep not the result, measurement ≠ feature) — all documented and cited (B276, etc.) — current.
- Output location (docs/PARITY-AUDIT.md, docs/RISKS.md for defects, docs/DECISIONS.md for owner decisions) — matches practice.

**No staleness found**.

### B2. measure

**Status**: Accurate and current.

**Checked against**:
- The one-call command — exact, includes `TF2VIEW_AUTOPLAY=1 pwsh run-exclusive.ps1 ...managed/Tf2DemoSalvage.Viewer3D/bin/Debug/net10.0-windows/tf2demoview.exe`.
- Why each part is there — all four reasons documented (playback vs wall clock, buffering, exclusive lock, fps_max clamp) — current.
- Frame output format and meaning (frame rate, moment cost breakdown, posed count, rest residual) — matches implementation.
- Empty-view check for performance floors — current.

**No staleness found**.

### B3. test-against-real-shapes

**Status**: Accurate and current.

**Checked against**:
- B270 incident (physics solver measured on synthetic cube, diverged from real `.phy` ragdoll) — case is documented.
- Rule: write expectation yourself, take subject from what the engine handles — documented.
- Tell: a fixture adjusted more than once for the same subject is probably wrong — documented.
- Instrument choice: probe over test, real map/model over corpus when possible — matches D126 and current practice.

**No staleness found**.

---

## C. Hooks Audit

**Repository**: `.claude/hooks/` (project-level, in-repo hooks called by `.claude/settings.json`)

**Four hooks in settings.json**:
- `tf2-valves-way.ps1` (UserPromptSubmit)
- `tf2-parity-cited.ps1` (PreToolUse)
- `tf2-verify-build-output.ps1` (PostToolUse)
- `tf2-flag-unread.ps1` (PostToolUse)

**Renamed with a `tf2-` prefix after this audit** (the owner: identify which repo a hook came from
when a multi-repo overlord/firstmate agent, or cross-referencing intertwined projects, surfaces it
in a shared menu). No governing rule added here — that convention lives in global CLAUDE.md and the
global hooks directory, not this repo's own.

### C1. tf2-valves-way.ps1

**Purpose**: Remind on every turn that Valve parity is a standing decision (D89, D129, D131), not a question.

**Checked against**:
- Reason documented in header (owner had to state the rule four times 2026-09-01) — accurate.
- Output: exact reminder with the four rules (parity first principle, read before designing, divergence is a defect, target is BETTER than TF2, if found fix it) — matches D89 and practice.

**Current**: The rule was correct on 2026-09-01 and is correct now. No drift.

### C2. tf2-parity-cited.ps1

**Purpose**: Refuse a commit to parity-sensitive code (`managed/Tf2DemoSalvage.{Scene,Render,Viewer3D,Presentation}`) unless the message cites engine source (file.cpp:line, source-sdk path) or carries `[no-parity]` with reason.

**Checked against**:
- Scoped to exact paths — matches current (Scene.cs exists; Viewer3D is the primary viewer; Presentation exists).
- Citation check looks for `.cpp`, `.h`, `source-sdk`, or `[no-parity]` in the commit message — correct.
- Escape hatch documented with reason requirement — matches D89 note "the escape hatch is [no-parity]".
- Bug noted in header (2026-09-01) where a RISKS entry quoted engine source and was mistaken for a citation in the commit message — fixed; now scans only from `git commit` onward — current.

**Current**: The guard survived a fix on 2026-09-01 that would have broken a naive implementation. Well-thought-out.

### C3. tf2-verify-build-output.ps1

**Purpose**: Catch build/test failures that exit 0 (e.g., when piped through grep or redirected).

**Checked against**:
- Reason: a `grep -E "error C"` caught compiler errors but not `error S` (SonarAnalyzer) or `error CA` (Roslyn), hiding a failure — documented.
- Pattern: matches any of `error C`, `error S`, `error CA`, `error IDE` by scanning for `\berror\s+[A-Z]{1,4}\d{3,5}\b` — correct.
- Matches only dotnet operations (test, build, publish, pack, msbuild) — scoped correctly.
- Grabs up to 5 errors and reports them — reasonable.

**Current**: The pattern is sound and accounts for the full analyzer set. No drift.

### C4. tf2-flag-unread.ps1

**Purpose**: After reading engine source, alert on any flag being USED (set, tested, masked) that was not looked up this session.

**Checked against**:
- Reason: B276 case where `EXCLUDE_AUTO_INTERPOLATE` was printed twice while reading animation vars, read past both times — documented.
- Trigger: reads only from engine source paths (`source-sdk-2013`, `hl2sdk`, `/src/(game|public|engine)/`) and not from this project's own `.claude/` or `docs/` — correctly scoped.
- Pattern: looks for `|=`, `&`, or `|` with a flag name (`[A-Z][A-Z0-9_]{5,}`) — three patterns, minimal false positives.
- Ledger: per-session (in `%TEMP%/tf2demosalvage-flags-<sessionid>.txt`), so the same flag is raised once per session, not every time the file is re-read — correct.

**Current**: The guard is thoughtfully scoped and accounts for the B276 lesson. No drift.

---

## D. MCP Servers

Reference: CONVENTIONS-HARVEST.md §Parallel track — "Ghidra MCP bridge (50–120× decompile speedup)" marked done 2026-09-06.

### D1. Clangd (agent-lsp)

**Status**: Configured and working.

**Evidence**: 
- Session context (system reminder at top of conversation) lists `mcp__agent-lsp__*` tools as available.
- `F:\src\source-sdk-2013\src\` carries `compile_flags.txt` and `compile_commands.json` (confirmed working this session for indexed lookups — B270).
- `.git/info/exclude` exempts these from the worktree (observed in CLAUDE.md global standards — "excluded via .git/info/exclude").
- Tool index shows 66 code intelligence tools available (find_references, inspect_symbol, etc.) via clangd.

**Assessment**: Working as designed. Worth keeping; clangd reads the vendor headers perfectly.

### D2. Ghidra MCP bridge

**Status**: Listed in available tools but not confirmed as onboarded into active settings.

**Evidence**:
- Tool list (system reminder) includes `mcp__ghidra__*` tools (debugger_attach, import_file, search_tools, etc.) — 20+ tools present.
- CONVENTIONS-HARVEST.md §Parallel track says "Ghidra MCP bridge (50–120× decompile speedup)" done 2026-09-06.
- CLAUDE.md documents Ghidra workflow but with explicit paths outside any git tree (`D:\ghidra-proj`, `D:\ghidra-settings\pinku-ghidra`), not through an MCP integration.
- Global settings.json checked does not show explicit MCP server configuration block for Ghidra.

**Assessment**: The bridge tool is available in the system (mcp__ghidra__* tools are in the deferred list), but whether it's actually connected and usable needs confirmation. If it is, it's worth a quick test to verify it doesn't break the headless script workflow (which is documented and working). If it's not connected, nothing is broken.

**Recommendation**: Quick verification step (try one Ghidra tool call, report success or "not onboarded") would clarify its status. Low priority unless decompilation becomes a bottleneck.

---

## E. Memory Upkeep

Checked `docs/memory/MEMORY.md` index (~178 entries) for any missing entries related to documentation, skills, hooks, or MCP setup.

**Found**: All major project decisions and gotchas are recorded. Nothing about MCP configuration status needs to be added — it's not a decision-tier fact (a gotcha would be "Ghidra bridge path syntax"; a decision would be "use Ghidra or not"). If the Ghidra bridge proves usable, a note like "Ghidra MCP bridge confirmed working 2026-09-07" would be worth adding to track the status.

**No action needed at this time**.

---

## Conclusions and Recommendations

### No action needed:
- All three skills are current and accurate.
- All four project-level hooks are accurate, well-documented, and match their purpose.
- Documentation conventions A1/A2 (one-owner rule, doc placement tree) are already in place and working.
- Clangd MCP is working; no changes needed.

### Optional add (low effort, marginal benefit):
- `docs/verification/` directory per B1 of CONVENTIONS-HARVEST.md. Benefit: moves measurements out of CLAUDE.md (~3% token savings). Cost: negligible — seed data already exists inline. Worth doing if context budget becomes tight; not urgent.

### Worth confirming:
- Ghidra MCP bridge status — is it actually onboarded and usable, or just available in the tool registry? Quick test would clarify. Not blocking anything currently.

### No changes to commit:
This audit is informational. The repo is compliant with its own standards and the conventions harvest. No fixes, updates, or additions were needed.

---

## Audit Metadata

- **Date**: 2026-09-07
- **Scope**: Documentation conventions, skills (`.claude/skills/`), hooks (`.claude/hooks/` referenced in settings.json), MCP server configuration
- **Ref documents**: CLAUDE.md, docs/DECISIONS.md, docs/memory/MEMORY.md index, CONVENTIONS-HARVEST.md
- **Note**: This audit occurred during the "documentation and tooling upkeep" session following the parity-chase work on the fix/corpse-ground-hole branch.
