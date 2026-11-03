# B669: the light traces of placement #215's static state, logged from the running engine

## Observation

`measurements/2026-11-03-b669-pipe-lights.md`, by this project against live TF2 11191711: #215's direct lights read -y 0.201 and +z 0.391 in TF2 against 0.136 and 0.328, +y and -z zero in both. Reading the trace found no difference and no subset of the lights fits.

## Question

For the state TF2 builds at (-3459 -683 506), which lights reach `FUN_1801b9570` or `FUN_1801b8e20`, from what point, and which does each return accept? Where does that list differ from ours?

## Method

Local listen server, `-insecure -vulkan`, `cp_process_f12`, `mat_hdr_level 2`, under `run-exclusive.ps1` with a lock that times itself out. Log-only breakpoints, rebased from the module's load address, read registers and memory only and never write the game. Arguments at entry; the float return in XMM0 at the return. Filtered on the start point. Forced by a fresh map load with the breakpoints armed. The final slots and cube if reachable. In ours, `prop-shade` at the same point.

## Control

A light ours and the disassembly both keep must log accept, and one behind the floor (spot (-3480 -728 343)) must log reject. A filter with no hit for #215 says nothing until another placement's start point hits. Logged lights must number no more than the map's world lights.

## Environment

Machine and build as in the records above. Ours: branch head at the run.

## Result

## Verdict
