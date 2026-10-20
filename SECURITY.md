# Security policy

Tf2DemoSalvage reads files from strangers: demos from league archives and archive.org, maps, textures, models and
sounds. It has no network service. The security question is therefore not "who can attack an endpoint" but "what can
a crafted file make the program do", and that is what this policy covers.

## What is in scope

A bug is a security issue when a crafted input file makes the viewer, the command-line tool or a library:

- read or write outside the memory it should (a length read from a file that reaches an allocation or an index
  without a bound),
- run out of memory, hang or crash on a small file (a decompression bomb, a loop driven by a count in the file),
- write a file outside the folder it was told to use (a path taken from a demo, a sidecar or a config),
- run a program or load code it was not asked to.

The inputs are demos (`.dem`), maps (`.bsp`), textures (`.vtf`), models (`.mdl`, `.vvd`, `.vtx`, `.phy`), sounds,
game archives (`.vpk`), particle and scene files, and the capture companion files (`.png.json`) the viewer writes
and reads back with `--like`.

## What is out of scope

- Valve's game and its content. Report problems in TF2 itself to Valve.
- A config file (`.cfg`, a mastercomfig `.vpk`) you chose to load. The viewer treats the configs in your own game
  folder like the game does: they are commands you wrote or installed, and running them is their purpose.
- A game folder, a download folder or a corpus you pointed the viewer at and then modified yourself.

## Which versions are supported

The project is a public beta (`0.1.0-beta.N`). Only the **latest published release** gets security fixes. If a
report concerns an older build, the answer is almost always "update to the latest".

## How to report

Use GitHub's private vulnerability reporting: on the repository's **Security** tab, choose **Report a vulnerability**.
That keeps the details private between you and the maintainer until a fix is out. Please do not open a public issue
for a vulnerability.

Include, if you can:

- the file that triggers it (or how to make one), or the command line,
- what you saw (a crash, a hang, memory use, a file written somewhere it should not be),
- the version (the name of the release zip you downloaded) and your Windows version.

A fuzzer-found crash with its input file is exactly the right kind of report.

## What to expect

This is a solo project, so there is no promised response time. Reports are read, and a confirmed issue is fixed
in the next release, with the fix and its test recorded in the release notes once it is safe to say so. If you want
to be credited, say how.
