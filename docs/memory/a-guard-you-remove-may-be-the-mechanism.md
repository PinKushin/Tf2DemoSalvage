---
name: a-guard-you-remove-may-be-the-mechanism
description: Widening a gate to reach more cases can turn a skipped proxy into a wrong answer; the engine's own refusal was what the gate reproduced.
metadata:
  type: project
---

`WorldRenderer.ApplyProxies` built its material-variable table only when the material carried
`$colortint_base`:

```csharp
if (Tintable(materialIndex) is { } tintBase) { variables = new() { ["$colortint_base"] = tintBase }; }
```

Every variable proxy was gated on `variables is not null`. Implementing `YellowLevel` — which
writes `$yellow` on 7,570 materials, most carrying no paint at all — meant widening that to every
material. It looked like removing an accident of how the paint work happened to be written.

**It was the mechanism.** With the table always present, `SelectFirstIfNonZero` on a material with
no `$colortint_base` read it as ZERO, took the other branch, and overwrote the modulation constants.
Five reflection pixel tests went red — the fourth time that family has caught a change to this
buffer, and the first time it caught a *removed* guard rather than an added constant.

**The comment beside the gate said so and I read past it**: *"a `SelectFirstIfNonZero` reading a
missing variable as zero would paint every unpainted cosmetic black"*. It described the value being
missing, and I took it as being about the seed rather than about the gate.

**What the gate was reproducing, in the engine, is a REFUSAL.** `CFunctionProxy::Init` calls
`pMaterial->FindVar( name, &foundVar, false )` and returns false when the material does not declare
the variable — and a proxy whose `Init` fails is never bound at all. So the correct rule is not "seed
more variables" and not "gate the table"; it is **a proxy whose named sources do not exist does not
run**, which is now the explicit test in each handler.

**How to apply.** Before widening a condition to reach a new case, ask what the narrow version was
REFUSING, not just what it was allowing — and look for the engine's own refusal, which is usually a
failed `Init` or an early return rather than a value. Related: [[half-a-mechanism-is-not-parity]],
where an invariant one system keeps turns out to be another's unstated precondition.

**And keep the pixel tests that have nothing to do with the feature.** Nothing in the proxy or
paint suites could see this; what failed was five reflection tests on weapon models, because they
are the only ones that measure a whole draw rather than the value under construction.
