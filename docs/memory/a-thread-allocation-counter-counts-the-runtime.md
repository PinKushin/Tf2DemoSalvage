---
name: a-thread-allocation-counter-counts-the-runtime
description: GC.GetAllocatedBytesForCurrentThread over one call can read runtime work as the method's; assert the minimum of several calls, every reading in the message.
metadata:
  type: feedback
---

`GC.GetAllocatedBytesForCurrentThread` counts the THREAD, so it can count one-off runtime work that lands mid-call.
2026-11-16: `ToStereo16_IntoABufferOnARealClip_…AllocatesNothing` read 7,376 bytes in one gate run, on a call that
allocates nothing. It then passed alone and under forced tiering (`DOTNET_TC_CallCountingDelayMs=0`,
`TC_CallCountThreshold=2`, `OSR_HitLimit=1`). Mechanism inferred, NOT reproduced.

**Why:** one measured call after one warm-up made a zero-allocation test flaky under gate load.

**How to apply:**
- Warm once. Assert the MINIMUM over several measured calls. Put every reading in the failure message.
- A per-call allocation shows in every call, so the minimum still catches it. Sabotage: one `new int[1]` read 32 bytes
  and failed.
- The minimum hides only an allocation that fires on some calls. Use it only after reading the method for such a
  branch. It tolerates noise, and it is a retry in effect, so say so in the test.
