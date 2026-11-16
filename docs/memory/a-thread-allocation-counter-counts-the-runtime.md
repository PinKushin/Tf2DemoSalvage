---
name: a-thread-allocation-counter-counts-the-runtime
description: GC.GetAllocatedBytesForCurrentThread over one call can read runtime work (tiering, OSR) as the method's; assert on the minimum of several calls.
metadata:
  type: feedback
---

`GC.GetAllocatedBytesForCurrentThread` counts everything allocated on the THREAD, including one-off runtime work that
lands mid-call. On 2026-11-16 `ToStereo16_IntoABufferOnARealClip_…AllocatesNothing` read 7,376 bytes in one gate run
over a call that allocates nothing, and then passed alone and under every forced-tiering setting tried
(`DOTNET_TC_CallCountingDelayMs=0`, `TC_CallCountThreshold=2`, `OSR_HitLimit=1`). So the mechanism is inferred, not
reproduced.

**Why:** a single measured call after a single warm-up makes a zero-allocation test flaky under load.

**How to apply:** for an "allocates nothing" test, warm once, then measure the MINIMUM over several calls. A real
allocation in the method shows up in every call, so the minimum still catches it. Sabotage that proves it: one
`new int[1]` in the method read 32 bytes and failed the test. Taking a minimum is not a retry: a retry reruns a
failing test, while this defines the measurement as the steady state.
