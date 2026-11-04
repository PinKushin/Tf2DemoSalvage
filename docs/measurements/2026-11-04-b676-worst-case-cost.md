# What B676's light average costs in its worst shape

## Observation

A review of `light-average-cost`: its "worst case" had every light match on its first comparison and no style delegate, so 237 ns is not a worst case, and the 0.3% quoted from it in RISKS is not a bound.

## Question

How many nanoseconds and bytes does one draw take when every light misses every stored one, with left lights written and a real style delegate? And what does that make the frame, at the count `2026-11-04-b676-the-switch-and-the-count.md` read?

## Prediction

Under 600 ns for "none matched", 0 bytes on every enabled shape, so under 0.04 ms at 63 models. Falsified by over 1,000 ns or any allocation on an enabled shape.

## Method

`dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- light-average-cost`, under the machine lock. Shapes are in the probe's remarks: all matched, none matched, left, off, twice each, the second pass being the number. Cost is the worst settled shape times 63 models, the largest per-second mean that record read, against its best-settled 5.1 ms frame.

## Control

The "off" shape must cost little and allocate nothing, since it now hands back the caller's list: if it did not, the loop rather than the average would be the number.

## Environment

Machine as in the record beside this one. Build: the commit carrying this file.

## Result

## Verdict
