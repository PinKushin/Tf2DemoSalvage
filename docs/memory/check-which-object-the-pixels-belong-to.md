---
name: check-which-object-the-pixels-belong-to
description: Before reading a box from a shot, prove which object it covers; a camera turned 180 degrees has its left and right swapped
---

B669: a back view at yaw 270 put -x on the right of the frame, while the front view at yaw 90 had it on the left. A box on the left of both read two different pipes. The wrong one gave a tinted 0.021, the colour exactly matched a group of lights, and a fit with two more lights agreed within about 1% on four faces. All of it was a measurement of the next placement along. The real back was zero.

**Why:** a precise colour and a close fit feel like evidence about the subject, and neither checks the subject. A camera placed at a seam between two props has one on each side.

**How to apply:** when a box's pixels are to stand for one prop, project its lighting origin or bounds through the camera first, or use `TF2VIEW_PICK` in ours. Take the box on the side that computation names. A fit with free subsets agreeing is not a control.
