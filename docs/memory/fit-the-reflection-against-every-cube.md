---
name: fit-the-reflection-against-every-cube
description: "A reflection's value can be fitted against each of a map's cubes from TF2's own shots: with mat_specular on minus off, over tint and alpha. It named the cube to three digits and killed a wrong reading of the shader (B669)."
metadata:
  type: feedback
---

**A reflection is arithmetic you can run on TF2's screenshots.** With `mat_specular 1` minus `mat_specular 0`, per pane or per box, divide by the tint and the alpha, and compare with a bilinear lookup of every `c*.hdr.vtf` in the map's pakfile along each pixel's mirror ray. On B669 that named TF2's cube first of 40 to 0.001-0.008, named ours' as a different one (the translucent pass had bound none), and proved `$envmapcontrast .5` was contrast 0, after a shot had made the VMT's contrast look closer.

**Two more from the same bug.** A record's default is a value production may simply never have set: `WorldVertex` kept `NormalZ = 1` for every brush face, and the reflection tests set normals by hand, so they passed. And draw the vector as colour before reading a lighting term: (0.40, 0.88, 0.23) against a view ray of (0.40, 0.89, -0.22) said the normal was (0, 0, 1) in one shot.

**How to apply:** when a reflection is off, measure the term alone on both sides and fit it before reading anything else. A control for the ray: it must equal the `pixel-face` probe's at a known pixel. `D:/measure-f12/b669/rem/fitcube.py` is the script.
