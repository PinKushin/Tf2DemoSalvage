---
name: insert-below-the-member-not-above-it
description: Adding a member just above an existing one splits that member from its XML doc comment; the build breaks with CS1572/CS1573 naming the WRONG member.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-01T17:17:55.247Z
---

Inserting a new member immediately before an existing one, by anchoring an edit on that member's
signature, puts the new member *between* the existing doc comment and the thing it documents. The
`<param>` tags then bind to the new member, and the compiler reports `CS1572: XML comment has a param
tag for 'x', but there is no parameter by that name` — against the NEW member, which never had those
parameters.

**Why:** it cost five build breaks in one session (`EntityModels.WorldBoxFor`,
`SceneViewmodel`, `WeaponModels.AttachmentsFor`, `BspLeafTree.LeafAt`,
`WorldVisibility.Leaves`). The error names the wrong member, so each one reads as a fresh mistake in
the code just written rather than as the same displacement every time.

**How to apply:** anchor the insertion on the END of the preceding member (its closing brace, or a
constant/field above), never on the signature of the member being pushed down. In this repo almost
every member carries a long doc comment, so the gap between `}` and the next `public` is several
dozen lines of prose — the signature is the tempting anchor precisely because it is the searchable
part, and that is the trap.

When it happens anyway: move the STRANDED doc comment down to its member rather than editing tags.
The comment is correct and complete; only its position is wrong.

Related: [[edit-files-with-the-file-tools]].

**The sibling failure, 2026-09-06: a doc comment that never listed a parameter at all.** Writing a
new method with two parameters and documenting only the first produces CS1573 — *"Parameter 'x' has
no matching param tag in the XML comment (but other parameters do)"*. Same family, different cause:
the first is a doc separated from its member, this is a doc that is merely incomplete.

**What made it expensive was WHO paid.** A sabotage-verifier was running against that project at the
time. My broken build stopped it after its first sabotage, so four of five checks never ran and the
agent correctly reported the blocker rather than working around it. **A build break costs whatever
else is running**, which with concurrent agents is no longer just my own next command.

**How to apply:** after writing any new public member, the build is not optional before starting or
resuming an agent that compiles the same project. And when an agent reports a compile error in a
file it was told not to touch, that is my file and my fix — it is not the agent failing the task.
