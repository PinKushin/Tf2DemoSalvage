---
name: a-negative-retry-is-a-sleep
description: Retry.WhileFalse on a condition that must never become true always runs the full window; it is Thread.Sleep in a costume and evades the no-sleep rule.
metadata:
  type: project
---

The standing rule is *"synchronise on the condition, never on the clock — no `Thread.Sleep`, no
`Task.Delay`, no 'usually long enough'"*. Three UI tests broke it without containing either call:

```csharp
Retry.WhileFalse(() => Count(Spectated) > before, TimeSpan.FromSeconds(2));
Count(Spectated).ShouldBe(before, "the free camera does not spectate anybody");
```

**A retry whose condition must NEVER become true always runs the full window.** It is a sleep, and it
reads as synchronisation, which is why it survives review. One of them even carried a comment
defending it: *"the claim is that nothing happens, so the only honest instrument is to give it the
same window the positive test gets and then look."*

That is wrong twice over. It costs the whole window on every green run — the owner spotted it from
outside, watching the suite: *"it sits on the free cam and i dont see antyhing happen for a little
while"* — and it is **weaker** than a synchronised check, because "the app froze" satisfies it
exactly as well as "the input was correctly ignored".

**There is always something to wait for: evidence the app processed the input and carried on.** Here
that is `viewmodel pass skipped`, written once per frame while the camera is not first-person. Wait
for it to advance, then assert the negative — which now means something, because a frozen viewer
fails the wait instead of passing the assertion.

Measured: the UI suite went **22s to 9s**, and no test now exceeds 1.04s. Two of the three seconds
saved were pure clock.

**How to apply:** any `Retry`/`WaitFor` whose predicate is the thing you are about to assert is
FALSE is this bug. Find a positive signal that proves the app is alive and past the input — a frame
counter, a log line the loop writes, a state that must change — synchronise on that, and assert the
negative afterwards. Related: [[slow-ui-tests-measure-the-app]],
[[instrument-bugs-outnumber-decoder-bugs]], [[read-the-trx-total-not-the-console]].
