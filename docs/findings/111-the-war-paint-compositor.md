# TF2 texture compositor (materialsystem.dll x64, ctexturecompositor.cpp) - hand-written description

Source: Ghidra project tf2materialsystem.gpr, program materialsystem.dll. Every item cites the
function address (FUN_xxxxxxxxx = 0x1xxxxxxxx) it was read from. Nothing here is pasted decompiler
output. Evidence classes: [D] read from decompile/disassembly of this binary, [T] read from data
tables in this binary, [S] Source SDK 2013 header read locally (F:/src/source-sdk-2013), [R] recalled
from the public SDK, NOT re-read, [I] inference, [?] could not be settled from this binary.

Index of sections
 0. Object model, flag word, field layouts
 1. Parsing (template expansion, $variables, $[x] substitution, stage grammar, key tables, ranges)
 2. Randomness (seed split, two streams, traversal order, every draw, sticker weights)
 3. Stage resolve (texture_lookup, UV matrix, levels, combine_*, select, apply_sticker, root copy, the shared draw)
 4. Output (render target, final texture, name, mips, state machines, per-frame driver)
 5. Machine dependence
 6. Unsettled / not in this binary

---------------------------------------------------------------------------------------------------
## 0. Object model, flag word, field layouts

Entry point CreateTextureCompositor = FUN_180043530. Arguments in order [D]: width, height, name
(C string), team (int), seed (64-bit), KeyValues* (the definition), flags (uint). It returns a
CTextureCompositor (0x70 bytes, built by FUN_180042180) or NULL.

Compositor object (FUN_180042180 ctor, vtable 0x1800bf3b0) [D]:
 +0x0c width, +0x10 height, +0x14 team, +0x18 64-bit seed, +0x20 root stage (CTCCopyStage),
 +0x28 compositor state (1 scheduled, 2 resolving, 4 failed, 5 complete), +0x2c error flag,
 +0x2d "fatal" flag, +0x30/+0x40 pool of free render targets (array of {w,h,ITexture*}, count at +0x40),
 +0x50 count of render targets created, +0x54 telemetry id, +0x58 name string, +0x68 flags (the same
 flag word that was passed in).

Flag word (single uint, used both as parse flags and stored at +0x68) [D]:
 0x01  ScheduleResolve skips the "does a texture with this name already exist" shortcut (FUN_180047770).
 0x02  final copy uses image format 0 (RGBA8888) instead of 0x28 (see 4.3).
 0x04  final copy does NOT generate mips (bGenMips = NOT bit 2) (see 4.3).
 0x08  parse only: stage parsers validate and do not allocate stage objects (FUN_180040b00 etc).
 0x10  validate/"template" mode: key values are not parsed or stored, `$[x]` with a missing variable
       substitutes "" instead of failing (FUN_180047a00), tree is not built; used to check templates.
 0x20  verbose dump: prints the KeyValues before and after expansion and returns NULL (FUN_180043530).
Flag bits 0x01/0x02/0x04 meanings are exact for their uses; their names are mine.

Base stage (every stage derives from it; ctor inline in FUN_180040b00 / FUN_180041ec0 / FUN_180041fd0 /
FUN_180041c90, base vtable 0x1800bf3f8) [D]:
 +0x08 refcount (starts 1), +0x10 first child, +0x18 next sibling, +0x20 ITexture* "output texture",
 +0x28 ITexture* "output render target", +0x30 black (default 0), +0x34 white (default 1),
 +0x38 gamma (default 1), +0x3c 12 floats = rows 0..2 of a 4x4 matrix (default identity),
 +0x70 stage state: 1 needs-resolve, 2 resolving/waiting, 3 copy in flight (root only), 4 failed, 5 done.
Children are kept in file order (the parser builds the list back to front so order is preserved).

Stage vtable slots (same layout for all five concrete stages, checked on 0x1800bf5e8 lookup,
0x1800bf0e0 combine, 0x1800bf7b0 select, 0x1800bf8d8 sticker, 0x1800bf978 root) [T]:
 slot 0 dtor, 1 Release (+8), 2 OnAsyncCreateComplete (+0x10; only root implements it, FUN_1800449a0),
 3 OnAsyncFindComplete(texture, index) (+0x18), 7 destructor body, 8 returns true (FUN_180008330,
 not used by anything I read), 9 BeginLoad (+0x48: async texture requests), 10 ResolveThis(compositor)
 (+0x50), 11 "IsTeamSpecific" (+0x58, lookup returns true when texture_blue is non-empty, FUN_180044450;
 everything else false), 12 Randomize(rng) (+0x60; returns true if it consumed random numbers).
 Slot 12 of select and of root is FUN_180007a20 = "return false".

---------------------------------------------------------------------------------------------------
## 1. Parsing

### 1.1 What CreateTextureCompositor does, in order (FUN_180043530) [D]
 1. If flag 0x20 set: print the name and the raw KeyValues (DevMsg).
 2. FUN_180044ec0(name, KV, flags, &expandedFlag) expands templates and variables (1.2, 1.3). NULL => fail.
 3. If flag 0x20: print the expansion and return NULL.
 4. Non-verbose: scan the expanded root's subkeys. If the root has an "implements" key or a `$` key AND
    flag 0x10 is set, return a bare compositor without a tree (template verification path); the
    "implements" string is stored on it (FUN_180076c70 into +0x60).
 5. Otherwise take the FIRST SUBTREE of the root: FUN_180079b60 = GetFirstTrueSubKey (first subkey whose
    data type is NONE, i.e. that has children). Only that one subkey is parsed as the stage tree. Any
    further subtrees of the root are ignored. (The wear_level_N key is the CLIENT's choice of which
    KeyValues to pass; this binary never looks at that key name. [D] it never compares against it.)
 6. FUN_180044bf0 parses that subkey list (1.4). On failure all partial stages are released, NULL.
 7. Build root CTCCopyStage (0x90 bytes, vtable 0x1800bf978) whose first child (+0x10) is the parsed
    stage, build the compositor object, link root into compositor +0x20.
 8. Seed streams and randomize the tree (section 2), return the compositor.

### 1.2 Templates ("implements") FUN_180046620 [D]
 - Scans the root's direct subkeys. A key named "implements" (case-insensitive) may appear once; a second
   one fails with "implements field can only appear once". Any non-`$` key besides it makes
   "if using 'implements', can only have variable definitions--other fields not allowed" fail.
 - The template is fetched from the global template manager (PTR_DAT_1800e7850, vtable +0x1a8 =
   FindTemplate(name)); not found => "Couldn't find template named '%s'".
 - Template record: +0x00 KeyValues*, +0x10 string "implements" (its own parent, may be empty),
   +0x18 bool visited flag (cycle detection).
 - If the template itself implements another template, recurse; otherwise copy the template's
   KeyValues (MakeCopy) and rename the copy to the compositor name, set expandedFlag=1.
 - Then every `$`-key of the implementing KeyValues is appended to the copy (FUN_180078c60). The later
   definition of a variable wins (1.3), so the implementer overrides the template's defaults.
 - Cycle check FUN_1800441b0 (CTextureCompositorTemplate::HasDependencyCycles): two-pointer walk down the
   implements chain with the visited flag; message "Detected cycle in paintkit template dependency chain".
   "AddTextureCompositorTemplate" itself (the registration function) was NOT located; only the
   template record layout above and its consumers are known. [?]

### 1.3 `$` variables and `$[name]` substitution FUN_180044ec0, FUN_1800463d0, FUN_180047a00 [D]
 - Variable definitions are direct children of the (expanded) root whose key name starts with '$'. The
   value must be a plain string; a subtree gives "All variable definitions must be simple strings".
 - Stored in a string-keyed tree map. Key compare = FUN_180075640, an ASCII case-insensitive compare
   (so $Color and $color are the same variable). A second definition of an existing name overwrites the
   stored value (override semantics).
 - If any variable exists (and flag 0x10 is clear) and the tree was not already copied, the KeyValues is
   MakeCopy'd, then FUN_1800463d0 walks every NON-`$` key recursively. Only LEAF VALUES are substituted,
   never key names.
 - Substitution syntax: the text "$[" starts a reference, the next "]" ends it; the name between is looked
   up in the variable map and replaced by its value. Text outside references is copied verbatim; any
   number of references per value. The replacement value is itself substituted recursively (variables can
   reference variables; no cycle guard seen). Two passes: pass 1 measures the length, pass 2 writes.
   Errors: unknown variable => "Couldn't find variable named $%s that was requested to be substituted"
   and creation fails (flag 0x10: "" is used); missing "]" => "Variable $[%s missing closing bracket ].".
 - After substitution the `$` keys are deleted from the copy.

### 1.4 Stage grammar FUN_180044bf0 (ParseNodes) [D]/[T]
 Table at 0x1800e74a0: {name, parser}; matching is case-insensitive (FUN_180075640):
   texture_lookup -> FUN_180040b00        combine_add -> FUN_180041480 (mode 1)
   combine_lerp   -> FUN_180041710 (mode 2)   combine_multiply -> FUN_1800419a0 (mode 0)
   select         -> FUN_180040eb0        apply_sticker -> FUN_180041170
 A subkey with any other name => "Compositor Error: Unexpected key '%s' while parsing definition." and
 the parse fails (but parsing continues over the other siblings so more errors are printed).
 Each stage parser walks its own subkeys; a subkey whose name is in that stage's key table is a PARAMETER,
 every other subkey is collected into a list and handed to FUN_180044bf0 as a CHILD stage list (so an
 unknown parameter name surfaces as the "Unexpected key" error). Children are linked in file order.
 Child usage: combine_* use all children as inputs; apply_sticker accepts at most ONE child (more =>
 parse failure); texture_lookup and select children are parsed and randomized but IGNORED at resolve.

### 1.5 Key tables ({name, parser, byte offset in the parse struct})
Parse-struct offsets below; the stage object copies the parse struct at object offset +0x78 (so
object offset = struct offset + 0x78) for lookup/combine/sticker. [T]+[D]

texture_lookup, table 0x1800bec70, parse FUN_180040b00. Struct layout / defaults:
  0x00 texture      string (FUN_1800402a0)            ""
  0x08 texture_red  string                            ""
  0x10 texture_blue string                            ""
  0x18 adjust_black range, each value x (1/255)       (0,0)
  0x20 adjust_offset range, each value x (1/255)      (1,1)   <- default is NOT divided by 255
  0x28 adjust_gamma range, each value replaced by 1/value (0 is a fatal Error)   (1,1)
  0x30 rotation     range (degrees)                   (0,0)
  0x38 translate_u  range                             (0,0)
  0x40 translate_v  range                             (0,0)
  0x48 scale_uv     range (ONE scale applied to both axes)   (1,1)
  0x50 flip_u       range (0 or 1 values; see 2.3)    (0,0)
  0x58 flip_v       range                             (0,0)
  0x60 "evaluate?"  bool (FUN_180040130), default 1; stored (+0xd8 in the object), not read by any
                    function I decompiled (Randomize/Resolve/ctor). [?] purpose unknown.
  Object offsets: texture 0x78, red 0x80, blue 0x88, black 0x90, offset 0x98, gamma 0xa0, rotation 0xa8,
  translate_u 0xb0, translate_v 0xb8, scale 0xc0, flip_u 0xc8, flip_v 0xd0, evaluate 0xd8.
  Object also holds loaded ITexture* at 0xe0 (texture), 0xe8 (red), 0xf0 (blue); randomized results at
  0xf8 black, 0xfc white, 0x100 gamma, and the 4x4 UV matrix at 0x104..0x143 (object size 0x148).

combine_add / combine_lerp / combine_multiply (shared table 0x1800befd0). Struct: 0x00 int mode;
  0x04 adjust_black, 0x0c adjust_offset, 0x14 adjust_gamma, 0x1c rotation, 0x24 translate_u,
  0x2c translate_v, 0x34 scale_uv, 0x3c flip_u, 0x44 flip_v, 0x4c "evaluate?"; same parsers and the
  same defaults as texture_lookup. Object offsets = +0x78: black 0x7c/0x80, offset 0x84/0x88,
  gamma 0x8c/0x90, rotation 0x94, translate_u 0x9c, translate_v 0xa4, scale 0xac, flip_u 0xb4,
  flip_v 0xbc, evaluate 0xc4. Object randomized results: 0xd0 black, 0xd4 white, 0xd8 gamma.
  Material pointer at +0xc8, object size 0xe0 (ctor FUN_180041ec0). NOTE: rotation, translate_u/v,
  scale_uv, flip_u/v are PARSED and STORED for combine stages but Randomize/Resolve never read them
  (FUN_1800405d0, FUN_180040310). They have no effect. [D]

select, table 0x1800bf170, parse FUN_180040eb0 (ctor FUN_180041fd0, object 0xc0 bytes):
  groups   (struct 0x00, string) -> texture path, object +0x78.
  select   (struct 0x08, repeatable) -> each occurrence appends one INT (GetInt) to a list; max 16
           entries, a 17th is dropped with a warning "Too many entries (>%d)". Object list at +0x80
           (data ptr +0x88, count +0x98). FUN_180040700.
  evaluate? (struct 0x30, bool, default 1) object +0xa8, unused.
  Material pointer +0xb0, loaded groups texture +0xb8.

apply_sticker, table 0x1800bf1e0, parse FUN_180041170 (ctor FUN_180041c90, object 0x148 bytes):
  sticker   (repeatable subtree; FUN_1800407f0): keys "weight" (float, default 1.0), "base" (string,
            default ""), "spec" (string, optional). If "spec" is absent it is DERIVED from base:
            result = StripExtension(base) + "_s" + FileExtensionWithoutDot(base). For an extensionless
            base that is simply base + "_s". For a base that has an extension it produces e.g.
            "name_s" + "vtf" with no dot, i.e. it only does something sensible for extensionless
            names. [D] FUN_180076f10/FUN_180077120 helper pair, string "_s" at 0x1800bf894.
            Max 0x7fffffff entries. Stored as 0x18-byte records {float weight, string base, string spec}.
  dest_bl, dest_tl, dest_tr : exactly two floats "%f %f" (FUN_180040a90), else fatal Error; each also
            sets a "was specified" byte. All THREE must be specified or the stage fails to parse; and
            there must be fewer than 2 child stages.
  adjust_black/adjust_offset/adjust_gamma (struct 0x4c/0x54/0x5c, same parsers as above),
  evaluate? (struct 0x64).
  Object offsets: sticker list data ptr +0x80 / count +0x90, dest_bl 0xa0/0xa4, dest_tl 0xac/0xb0,
  dest_tr 0xb8/0xbc (each followed by its flag byte), adjust black 0xc4/0xc8, offset 0xcc/0xd0,
  gamma 0xd4/0xd8, evaluate 0xdc, material +0xe0, loaded base texture 0xe8, loaded spec texture 0xf0,
  chosen sticker index 0xf8, results: black 0xfc, white 0x100, gamma 0x104, 4x4 matrix 0x108..0x147.

### 1.6 Range parsers [D]
 ParseRangeFromKV = FUN_180040150: formats the KV value with the CRT sscanf-family using "%f %f"
  (format string at 0x1800b60d0). Result 2 => (lo, hi) as given. Result 1 (single number) => (v, v).
  Anything else (0 or -1, e.g. empty / non-numeric) => Warning "Failed to ParseRangeFromKV: key name
  [%s], key val [%s]" then a fatal Error. lo>hi is accepted (RandomFloat handles it).
 ParseRangeThenDivideBy255 = FUN_1800402d0: ParseRangeFromKV then each of the two floats times 0.003921569
  (1/255). Used by adjust_black and adjust_offset.
 ParseInverseRangeFromKV = FUN_180040220: ParseRangeFromKV then each value becomes 1/value. A value of
  exactly 0 is a fatal Error (message says "Substituting 0.0.." but control reaches a breakpoint/Error
  and does not continue). Used by adjust_gamma. So "adjust_gamma 2" yields gamma 0.5 stored, i.e. the
  user value is the divisor and the shader receives its reciprocal.
 Single-value form: a plain number is a degenerate range (no randomness but still one draw, 2.3).

---------------------------------------------------------------------------------------------------
## 2. Randomness

### 2.1 Seed split GetSeed = FUN_180043dd0 [D]
 The compositor stores a 64-bit seed at +0x18. It is de-interleaved into two 32-bit seeds:
   seedA (named "seedhi" in the texture name) = bits 0,2,4,...,62 of the 64-bit value, compacted to bits 0..31
   seedB (named "seedlo") = bits 1,3,5,...,63, compacted to bits 0..31.
 (Loop i=0,2,...,62; bit i goes to position i/2, bit i+1 goes to position i/2.) Verified by the stack
 argument order in FUN_180043ee0 (seedA is printed first).

### 2.2 Streams and traversal [D] FUN_180043530 tail, FUN_180042db0
 Two vstdlib CUniformRandomStream objects (imported EXTERNAL, ctor and SetSeed are not in this binary),
 contiguous in memory, 0xa0 bytes each: stream 0 SetSeed(seedA), stream 1 SetSeed(seedB).
 Start index 0. Procedure FUN_180042db0(node, &index, streams, 2) (recursive, PRE-ORDER, children before
 siblings):
   for node in sibling chain:
       consumed = node.Randomize(streams[index])            // vtable slot 12
       if consumed: index = (index + 1) % 2                 // toggles the stream
       if node.firstChild: recurse(node.firstChild)
       node = node.nextSibling
 Start: root.Randomize (returns false, no draw), then recurse on root's first child. So the first
 stage that draws (in document order, depth first) uses stream 0 (seedA), the second stream 1 (seedB),
 the third stream 0 again, and so on. Stages that return false (select, root) neither draw nor toggle.
 texture_lookup, combine_*, apply_sticker all return true. A texture_lookup or select child stage that
 is ignored at resolve time still takes part in this traversal and still draws/toggles.
 The IUniformRandomStream layout is [S] (random.h): +0 SetSeed(int), +8 RandomFloat(lo,hi),
 +0x10 RandomInt(lo,hi), +0x18 RandomFloatExp. The generator algorithm itself (Park-Miller with 32-entry
 shuffle table, RandomFloat = clamp(AM*n, RNMX) * (hi-lo) + lo, IA=16807, IM=2147483647, NTAB=32) is
 vstdlib code that is NOT in this DLL and whose source (vstdlib/random.cpp) is not in the local SDK copy;
 [R] only. Per-stage exact consumption of draws below is [D].

### 2.3 What each Randomize draws, in exact call order [D] (verified in disassembly of FUN_1800431a0)
 Every "range" draw is RandomFloat(lo, hi) on the current stream, even when lo==hi (the draw is still
 consumed and returns lo).

 texture_lookup (FUN_1800431a0), 9 call sites in this order:
   1. flip_u: if lo==hi then (int)lo (trunc) WITHOUT a draw, else RandomInt(0,1)
   2. flip_v: same rule
   3. translate_u = RandomFloat(lo,hi)      4. translate_v = RandomFloat
   5. rotation    = RandomFloat             6. scale_uv    = RandomFloat
   7. black       = RandomFloat             8. offset      = RandomFloat
   9. gamma       = RandomFloat
   Outputs: +0xf8 = black, +0xfc = black + offset (so "white"), +0x100 = gamma, then the matrix (3.2).
   So the number of draws is 7 plus up to 2 RandomInt (0 to 2 extra), and a flip is applied when its
   integer is nonzero. flip "0 1" is a coin flip; "1" is always flipped; "0" never.

 combine_* (FUN_1800405d0), 3 draws in order: black, offset, gamma. Outputs +0xd0 black,
   +0xd4 black+offset, +0xd8 gamma. Always returns true.

 apply_sticker (FUN_180042e30):
   a. total = sum of all sticker weights; r = RandomFloat(0, total); iterate stickers in file order:
      if r < weight_i choose i, else r -= weight_i. Chosen index stored at +0xf8. (Strict "<".)
      This draw happens even with one sticker.
   b. black, offset, gamma draws (three more RandomFloat, same order and meaning as combine).
      Outputs +0xfc black, +0x100 black+offset, +0x104 gamma.
   c. no further draws; the geometry matrix is deterministic (3.4).
 select: no draws, returns false. root: returns false.

---------------------------------------------------------------------------------------------------
## 3. Stage resolve

### 3.0 Common behaviour [D]
 ResolveThis(compositor) of every stage returns immediately if state is 4 or 5. State 1 becomes 2.
 It then checks its prerequisites and returns without doing anything if they are not met (so it is
 retried each frame, see 4.4). Stages that finish set state 5, store their output description in the
 base fields (+0x20 texture or +0x28 RT, +0x30 black, +0x34 white, +0x38 gamma, +0x3c matrix) and call
 FUN_180042cb0, which returns every child's render target to the compositor's free pool (keyed by the
 RT's mapping width/height, vtable +8/+0x10) and resets the child's output fields to identity.
 Errors go through FUN_180043af0(compositor, recoverable=0, fmt, ...): it sets compositor +0x2c=1,
 +0x2d=1 and prints a Warning. All error sites I read pass recoverable=0, so every error is fatal
 (compositor ends in state 4).

 A stage's output is the tuple {texture or RT, black, white, gamma, 3x4 matrix}; its PARENT consumes it.
 A stage's own levels and matrix are therefore applied when the parent draws, NOT when the stage itself
 renders. Consequence: the top-level stage's levels/matrix are never applied (root copy, 3.6, uses only the RT).

### 3.1 texture_lookup (FUN_180047420 ResolveThis, FUN_180046140 BeginLoad, FUN_180044b30 OnAsyncFind) [D]
 - BeginLoad requests up to 3 textures with IMaterialSystem vtable +0x400 (AsyncFindTexture [R]) using the
   texture group "Runtime Composite", extraArgs = index, complain=false, additional creation flags
   0x100000: index 0 = texture, index 2 = texture_red, index 3 = texture_blue. The callback stores the
   ITexture* (AddRef'd) at +0xe0 / +0xe8 / +0xf0 respectively.
 - Resolve waits until every non-empty name has its pointer. Failure rules: if +0xe0 is null and neither
   red nor blue was loaded => "Invalid texture_lookup node, must specify at least texture (or
   texture_red and texture_blue) or all of them."; any loaded texture whose IsError() (ITexture slot 15
   [S]) is true => "Failed to load texture '%s' (_red/_blue), this is non-recoverable".
 - TEAM CHOICE: compositor team == 2 and a red texture exists => texture_red; team == 3 and a blue texture
   exists => texture_blue; otherwise "texture". (TF2 team numbers: 2 = RED, 3 = BLU.)
 - Output: +0x20 = chosen texture, +0x28 = 0 (no render target), levels = (+0xf8, +0xfc, +0x100), matrix =
   rows 0-2 of the randomized matrix at +0x104. No draw happens in a lookup stage.
 - A lookup directly under the root has no render target, so it cannot be copied (see 3.6). [I]

### 3.2 The UV transform matrix (FUN_1800431a0, helpers FUN_180070440, FUN_180071440, FUN_180070510, FUN_180070ed0) [D]
 Row-major 4x4, product C = A*B with C[i][j] = sum_k A[i][k]*B[k][j] (FUN_180070ed0).
 Let fu = -1 if flip_u else +1, fv = -1 if flip_v else +1, s = scale_uv draw, th = rotation draw in DEGREES,
 (tu,tv) = translation draws.
  1. R = rotation about Z: angle = th * 0.017453292 (pi/180) via sincos. R = [[c,-s,0,0],[s,c,0,0],[0,0,1,0],[0,0,0,1]]
     with c = cos(angle), s = sin(angle). Rotation CENTRE is the UV origin (0,0), not (0.5,0.5).
  2. S = R post-multiplied by diag(fu*s, fv*s, 1) (FUN_180071440 scales COLUMNS 0,1,2 of R; element [3][3] =1):
     M = R * diag(fu*s, fv*s, 1).
  3. T = identity with elements [0][3] = tu, [1][3] = tv, [2][3] = 0.
  4. M = M * T. Because the last column of R*S is (0,0,0,1) this just sets M's translation column to
     (R*S)*(tu,tv): the translation is rotated and scaled too. Net effect: uv' = R*S*(uv + (tu,tv)).
  5. Repack to a 2D affine in a 3x3-style layout: elements [0][2] := [0][3] (=tx'), [1][2] := [1][3] (=ty'),
     [2][2] := 1. Rows 0-2 (12 floats) are stored: row0 = [m00 m01 tx' (old m03)], row1 = [m10 m11 ty' ...],
     row2 = [0 0 1 ...].
 So the stage stores a column-vector 2D affine A with A*(u,v,1) = R*diag(fu*s, fv*s)*((u,v)+(tu,tv)).
 Whether the shader multiplies it as M*(u,v,1) or its transpose, and whether it maps output-UV to
 source-UV, is determined by the shader (not in this DLL) [?]; the matrix is uploaded unmodified (3.5).

### 3.3 Levels ("adjust_*" -> TEXADJUSTLEVELS) [D]
 Three floats per stage output: black b, white w = b + offset, gamma g (already reciprocal, see 1.6).
 They travel in the stage's output (+0x30/+0x34/+0x38) and are uploaded per INPUT at draw time as one
 vec3 material variable "$texadjustlevels<i>" = (b, w, g) [D FUN_180045c20]. The formula the shader applies
 is in the shader (not in this DLL) [?]. Defaults (0,1,1) are identity. Values are in 0..1 (the 1/255
 conversion happened at parse time).

### 3.4 combine_multiply / combine_add / combine_lerp (FUN_180040310) [D]
 - Material by mode index into the string array at 0x1800e7450 (ctor FUN_180041ec0 passes
   array[mode] with texture group "Runtime Composite", complain=true, to IMaterialSystem vtable +0x238
   (FindMaterial [R])):  mode 0 combine_multiply = "dev/CompositorMultiply", 1 combine_add =
   "dev/CompositorAdd", 2 combine_lerp = "dev/CompositorLerp". Full array: [0] CompositorMultiply,
   [1] CompositorAdd, [2] CompositorLerp, [3] CompositorSelect, [4] " ECO_Legacy_Lerp_FirstPass",
   [5] " ECO_Legacy_Lerp_SecondPass", [6] CompositorBlend, [7] " ECO_LastPrecacheMaterial",
   [8] CompositorError (leading space is in the string). [4],[5],[7],[8] are not referenced by the stage ctors.
 - Waits until every child has state 5. Allocates a render target (4.1). Collects each child's output
   record {texture, RT, black/white/gamma, matrix} IN CHILD ORDER, and renders with the shared draw
   (3.5) with clear = TRUE. The number of inputs is the number of children (no limit enforced here).
 - Its own output: +0x20 = 0, +0x28 = the new RT, levels = (+0xd0, +0xd4, +0xd8), matrix = identity,
   state 5, then children's RTs go back to the pool.
 - The meaning of input order for lerp (which input is the blend factor) is in the shader. [?]

### 3.5 The shared draw FUN_180045c20 (CTCStage::Render) [D]; names of render-context slots [I]
 Inputs: output RT, material, vector of input records (stride 0x50: +0 texture, +8 RT, +0x10 black,
 +0x14 white, +0x18 gamma, +0x1c..+0x4b twelve matrix floats), clearFlag.
 For input i (0-based, in array order):
   "$srctexture<i>"      <- record.texture if non-null else record.RT   (texture var set, material var slot +0x70)
   "$texadjustlevels<i>" <- vec3 (black, white, gamma)                  (var slot +0x50)
   "$textransform<i>"    <- 4x4 matrix: the 12 stored floats as rows 0-2 and the fourth row (0,0,0,1)  (var slot +0x98)
   (string literals at 0x1800bfc08 / 0x1800bfc18 / 0x1800bfc30; variables are looked up with the
   IMaterial vtable +0x58 = FindVar.)
 "$textureinputcount" <- int count of inputs (var slot +0x20).
 Then through the IMatRenderContext (slot numbers from vtable offsets; names [I] from SDK slot order):
   GetRenderContext (IMaterialSystem +0x310), BeginRender (+0x10),
   PushRenderTargetAndViewport(rt, 0, 0, rt.ActualWidth, rt.ActualHeight) (+0x350),
   if clearFlag: ClearColor4ub(0,0,0,255) (+0x248) then ClearBuffers(true,false,false) (+0x60),
   draw material full screen (+0x258 with the material), PopRenderTargetAndViewport (+0x368),
   release the per-input variable references (+0x90), EndRender (+0x18), Release (+8).
 One full-screen pass per stage. The clear colour is opaque black (alpha 255).

### 3.6 select (FUN_180047100 ResolveThis; FUN_1800460e0 BeginLoad; FUN_180044ae0 OnAsyncFind) [D]
 - BeginLoad requests the groups texture (index 0) with the same async call/flags as lookup; stored at +0xb8.
   Resolve: waits for +0xb8; if it IsError => "Failed to load texture %s, this is non-recoverable".
 - Allocates an RT. For i = 0..15 sets the int material variable "$selector<i>" (format at 0x1800bf870) to
   select[i] if i < count else 0. Exactly 16 selectors are always set.
 - Draw with material "dev/CompositorSelect" (FUN_180041fd0), ONE input: the groups texture with identity
   levels (0,1,1) and identity matrix, clearFlag = TRUE. Output: +0x20 = 0, +0x28 = RT, identity levels
   and matrix (no adjust_* keys exist on select). Children of select are not consulted. [D]

### 3.7 apply_sticker (FUN_1800467e0 ResolveThis, FUN_180045ff0 BeginLoad, FUN_180044a20 OnAsyncFind, FUN_180042e30 Randomize) [D]
 - BeginLoad (after Randomize chose index k) requests sticker[k].base with extraArgs 0 (stored +0xe8) and
   sticker[k].spec with extraArgs 1 (stored +0xf0); an empty name is not requested. If the spec load comes
   back IsError, it is replaced by the engine texture "black" (FindTexture, group "Runtime Composite").
   If the child (+0x10) exists it must be state 5. Resolve waits for any non-empty name whose pointer is null.
 - Inputs to the draw, in order: [child output record if there IS a child] , sticker base, sticker spec.
   The sticker base and spec records both carry the sticker matrix (+0x108) and identity levels (0,1,1).
   clearFlag = (no child). Material "dev/CompositorBlend". "$textureinputcount" is therefore 2 (no child)
   or 3 (child).
 - Output: +0x28 = new RT, levels = (+0xfc, +0x100, +0x104), identity matrix.
 - Sticker matrix (FUN_180042e30), with BL=dest_bl, TL=dest_tl, TR=dest_tr, all 2D points as given in the KV:
     dBLx = BL.x - TL.x, dBLy = BL.y - TL.y, h = sqrt(dBLx^2 + dBLy^2)  (length of the TL->BL edge)
     dTRx = TR.x - TL.x, dTRy = TR.y - TL.y, w = sqrt(dTRx^2 + dTRy^2)  (length of the TL->TR edge)
     angle = acos(dBLy / h) in degrees (acosf, 57.29578 deg/rad), multiplied by +1 if dBLx < 0 else -1
     sgn = +1 if (TL.y - TR.y)*dBLx + dTRx*dBLy > 0 else -1  (that expression equals the cross product of
           (TR-TL) and (BL-TL); it detects mirrored winding)
   M = Translate(TL.x, TL.y, 0) * RotateZ(angle) * Scale(sgn*w, h, 1)     (all via the same 4x4 helpers;
   RotateZ = FUN_1800704b0 = axis-angle rotation about (0,0,1), degrees, same convention as 3.2)
   stored matrix = INVERSE of M (FUN_1800705d0: general 4x4 Gauss-Jordan with partial pivoting, epsilon 1e-5),
   then the same repack as 3.2 step 5 (element [0][2]:=[0][3], [1][2]:=[1][3], [2][2]:=1).
   Geometry meaning: sticker space has (0,0) at TL, (1,0) at TR (when sgn=+1), (0,1) at BL; M maps sticker
   space to the destination space of the dest_* coordinates and the inverse maps destination -> sticker.
   Whether the dest_* numbers are normalized 0..1 UVs or pixels is not decidable here [?] (the arithmetic
   works for either).

### 3.8 Root CTCCopyStage (FUN_180046e40 ResolveThis, FUN_1800449a0 OnAsyncCreateComplete) [D]
 - Requires its single child (+0x10) to be state 5. If +0x78 (result texture) is already set and not
   IsError: output = that texture, state 5. Otherwise, if state != 3, starts one async copy of the CHILD'S
   RENDER TARGET (child +0x28) into a new texture, state becomes 3; when OnAsyncCreateComplete fires it
   stores the texture at +0x78 (AddRef) and the next ResolveThis completes (state 5).
 - IMatRenderContext vtable +0x620 call (matches IMaterialSystem-style AsyncCreateTextureFromRenderTarget
   [R]): (srcRT, name, imageFormat, bGenMips, additionalFlags, receiver=root stage, extra=0) with
     name           = the compositor texture name (4.2)
     imageFormat    = 0 (RGBA8888 [S]) if compositor flag 0x02 else 0x28 (= IMAGE_FORMAT_DXT5_RUNTIME in the
                      SDK 2013 PC enum counted from imageformat.h [S]/[I], i.e. runtime-compressed DXT5)
     bGenMips       = NOT(compositor flag 0x04)   => mips ARE generated by default
     additional creation flags = 0x100012 = 0x100000 | 0x10 | 0x2 (0x2 = TRILINEAR, 0x10 = ANISOTROPIC
                      in the VTF flag set [R]; 0x100000 not named in this analysis)
 - The child's own black/white/gamma/matrix are DROPPED here: the copy takes only the RT. [D]

---------------------------------------------------------------------------------------------------
## 4. Output

### 4.1 Render targets (FUN_180042a50 AllocateCompositorRenderTarget) [D]
 Pool lookup: first entry whose stored {w,h} equals compositor width/height (+0xc/+0x10) is removed from
 the pool and returned. Else IMaterialSystem vtable +0x2a8 (CreateNamedRenderTargetTextureEx2 [R]) with
 arguments (name "", width, height, sizeMode 9, imageFormat 0, depth 2, textureFlags 0x100000, rtFlags 0),
 bracketed by vtable +0x3f0(true) ... +0x3f0(false). In the SDK enums: sizeMode 9 = RT_SIZE_LITERAL_PICMIP
 [R], format 0 = RGBA8888 [S], depth 2 = MATERIAL_RT_DEPTH_NONE [R]. The textureFlags 0x100000 sets NO
 sRGB (0x40), NO clamp (0x4/0x8), NO nomip: the RT is plain RGBA8888 with no explicit sRGB flag. Every
 stage that renders gets its own RT of the full compositor size (all intermediate RTs are w x h).
 Compositor +0x50 counts RTs created. Returned RTs go back to the pool in FUN_180042cb0 keyed by
 GetMappingWidth()/GetMappingHeight() (ITexture slots +8/+0x10).

### 4.2 Resulting texture name FUN_180043ee0 [D]
 If ANY stage in the tree reports IsTeamSpecific (slot 11; lookup stage with a non-empty texture_blue;
 walk = FUN_180044400 over children/siblings/self):
   "proc/texcomp/<name>_flags<%08x flags>_seedhi<%08x seedA>_seedlo<%08x seedB>_team<%d>_w<%d>_h<%d>"
 else (no team dependence):
   "proc/texcomp/<name>_flags<%08x>_seedhi<%08x>_seedlo<%08x>_w<%d>_h<%d>"
 (format strings at 0x1800bfb58 and 0x1800bfb98; arguments name(+0x58), flags(+0x68), seedA, seedB,
 team(+0x14), width(+0xc), height(+0x10)). The name is the cache key: equal name => the same texture.
 NOTE: only texture_blue triggers the team component; a definition that has texture_red but no texture_blue
 is treated as team-independent for naming. [D]

### 4.3 Mips / sRGB / format summary
 Final texture: DXT5_RUNTIME (0x28) or RGBA8888 (flag 0x02), mips generated unless flag 0x04, trilinear +
 anisotropic additional flags, no sRGB flag anywhere in the compositor code. Whether the shaders
 linearize/gamma-correct is in the shaders [?].

### 4.4 State machines and the per-frame driver [D]
 Per stage state (+0x70): 1 = needs resolve, 2 = resolving, 3 = copy in flight (root), 4 = failed, 5 = done.
 Compositor state (+0x28): 1 scheduled, 2 resolving, 4 failed, 5 complete.
 ScheduleResolve (FUN_180047770): if flag 0x01 is clear, build the name (4.2); if IMaterialSystem
 +0x280 (IsTextureLoaded-style) says it exists and +0x278 FindTexture returns a non-error texture, then
 give it to the root (root slot 2), set root output/state 5 and the compositor state 5 immediately
 (no GPU work). Otherwise allocate a unique telemetry id (global counter DAT_180106d60), state 1, and
 append the compositor to the global pending list (FUN_18001c1e0 on 0x1800f2950).
 Resolve (FUN_1800462c0): only if compositor state == 1. Walk the tree (FUN_180046250 with reset=1:
 children-first recursion, siblings last): for every stage set state 1 then slot 9 (BeginLoad: async
 texture requests), then slot 10 (ResolveThis). Order: root's children subtree, then root itself, then
 root's siblings. Compositor state becomes 2.
 Update (FUN_180047c00): if the error flag (+0x2c) is clear: if the root is not state 5, run
 FUN_180046250 with reset=0 (just ResolveThis, children before parents) then the root's ResolveThis;
 if the root is now state 5 set the compositor state 5. If the error flag is set and the fatal flag
 (+0x2d) is set: state 4. The branch for "error but not fatal" is a restart path that dereferences a null
 pointer in the binary (dead or buggy; nothing in what I read sets recoverable=1). [D]
 Driver: CMaterialSystem::EndFrame FUN_180014bc0 (main thread only) [D]:
   1. pending list: while non-empty: head with refcount 1 (nobody else holds it) is released and dropped;
      otherwise Resolve(head), move it to the "active" list, and STOP (so at most ONE new compositor is
      started per frame).
   2. active list in order: refcount 1 => release and drop; else Update(it); if its state is 5 or 4 it is
      released and removed and the loop BREAKS (at most one completion handled per frame; later
      compositors in the list are not Updated that frame).
 So a composite that needs async texture loads takes at least a few frames and ResolveThis polls for them.

---------------------------------------------------------------------------------------------------
## 5. Machine dependence

 - Render target size mode LITERAL_PICMIP (9) [R]: the RT dimensions follow the user's texture-detail
   (picmip) setting; I did not find any explicit mat_picmip / dx-level / "low res" test in the compositor
   functions I read. The compositor pool match uses the RT's mapping dimensions vs the nominal width/height.
 - Final texture format 0x28 relies on runtime DXT5 compression support in the shader API; fallback only
   via compositor flag 0x02 chosen by the caller. [I]
 - The compositor textures are named per (name, flags, seeds, team, w, h), so different sizes or flags give
   different cache entries. No GPU-vendor or DX-level branch in these functions.
 - Everything else (RNG, parse, matrices) is deterministic given the 64-bit seed, team and KeyValues.
   BUT the RandomFloat/RandomInt generator and float-to-int conversion are in vstdlib and the CRT; the
   draws are deterministic only if those match the reference build.

---------------------------------------------------------------------------------------------------
## 6. Unsettled / not in this binary

 1. The shaders dev/Compositor{Multiply,Add,Lerp,Select,Blend}: the actual blend math, how
    $texadjustlevels (b,w,g) is applied, how $textransform is multiplied (row vs column, transposed or
    not), what "lerp" input order means, how $selector<i> indexes the groups texture, and how Blend
    composites sticker base/spec over the child. [?] These are .vcs shader code in the stdshaders DLL /
    game VPK, not materialsystem.dll.
 2. AddTextureCompositorTemplate registration function and VerifyTextureCompositorTemplates internals.
 3. The unit of dest_tl/dest_tr/dest_bl (normalized UV or pixel). [?]
 4. The "evaluate?" key (parsed everywhere, read nowhere I looked).
 5. The exact meaning of vtable +0x3f0 around render target creation, and the unnamed flag 0x100000.
 6. Writers of compositor +0x2c/+0x2d other than FUN_180043af0 (not searched).
 7. RNG algorithm bit-exactness: vstdlib, imported, recalled only.
 8. Slot names for IMatRenderContext and IMaterialSystem offsets are inferred from SDK slot order, except
    ITexture which was read from itexture.h.

## 7. Closed since, from published source and earlier work

- Item 1, the shader math: the per-stage shader is published (`materialsystem/stdshaders/compositor.cpp:1-448`,
  `compositor_ps2x.fxc:1-341`, `compositor_vs20.fxc:1-42`): multiply, add, lerp, select (the groups value
  `round( v * 255 / 16 )` against `$selector`s held as 1/16 steps in constant 7), blend
  (`(1 - c1.a) * c0 + c1.a * c1`), `AdjustLevels`, and the vertex shader's `$textransform` 2x4 matrices
  (`SetVertexShaderMatrix2x4`, BaseVSShader.cpp:458-474). *Read from published source*; whether the shipped `.vcs` were
  compiled from exactly these files is not checked.
- Item 7, the RNG: `CUniformRandomStream` is already ported bit-exact from `vstdlib.dll`'s disassembly
  (`Tf2DemoSalvage.Core.Primitives.UniformRandomStream`, findings 57, B415). `random.cpp` is not in the SDK.
- Item 3, the sticker corners: the only other mention is `tf_proto_def_messages.proto:554-557`, which types them
  `ParseSettable< Vector2D >`; the unit is still open.

## 8. The port, and what it showed (2026-11-16)

- **The CPU executor came first and was too slow to draw with**: 3.2 s for a 512² Night Owl and 33 s at 2048², where the
  local player's composite is the base texture's full size. *Measured* (probe `paint-kits composite`). So the tree now
  resolves to a `CompositorPlan` — passes in draw order and the textures that must load — and the GPU runs that plan
  (`Render.CompositorRenderer`). The CPU executor stays as the suite's reference, since CI has no GPU. On a rotated 4×4
  texture the two agree to a worst channel difference of 0 (`CompositorRenderTests`). *Measured.*
- **The first real-demo shot drew the panel painted and the held weapon stock.** Not the compositor: the launcher's
  attributes come in its instance baseline, and the prop track read the econ wire only from a delta touching an
  element-scoped property, which none did. The player's item list reads the whole entity state, so it had them. Fixed by
  reading the wire once at creation. *Measured* on `rgl-pug-2026-08-10-pov.dem`, entity 406, tick 32800.
- **Against TF2** at that freeze: the "carrying" panel's Horror Holiday launcher matches in pattern, colours and sticker
  placement. The one difference was the name's colour, which TF2 takes from the War Paint's grade (`GetRarity`) and the
  panel took from the definition. *Measured* (side-by-side capture), and fixed.
- **The proxy census had a blind spot.** `vmt-proxy` counted a proxy only by its keys, so an empty block —
  `"WeaponSkin" {}`, `"invis" {}` — was invisible: it reported no material running either. Counting the block's opening
  finds 174 materials running `WeaponSkin` and 7,753 running `invis`. An absence the instrument could not have seen.
