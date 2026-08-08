# Flattened-property differential

The artifacts that located `RISKS.md` B12.

`flatprops.rs` is an example for [demostf/parser](https://codeberg.org/demostf/parser). Drop it
in that repo's `examples/`, build, and run it against a demo:

```bash
cargo build --release --example flatprops
./target/release/examples/flatprops z1800.dem CTFPlayer
```

It prints one line per property — `<class-id>\t<class-name>\t<index>\t<table>.<prop>` — in the
order entity deltas index them. That order is the thing worth comparing: normal parser output
cannot reveal a disagreement in it, because a wrong index reads a real value into the wrong
field rather than failing.

The two `.tsv` files are that output for `z1800.dem`'s `CTFPlayer`, from the oracle and from
this parser. Compare with:

```bash
diff ctfplayer-flattened-oracle.tsv ctfplayer-flattened-ours.tsv
```

`DumpFlattened.cs` is the same dump from this parser's side. Point a throwaway console project
at `Tf2DemoSalvage.Core` and run it as `<demo> ALL` for every class, or `<demo> CTFPlayer` for
one.

**What they established.** The first run showed both lists holding exactly 741 properties, 235
of them array elements, with identical sets of names — nothing unique to either side. Only the
order differed, first at index 20. That cleared the schema parser, the exclusion rules and
array expansion in one step, since each would have changed the set, and pinned the fault to
`SchemaFlattener`'s final partition. See `RISKS.md` B12.

**Current state: zero differences**, on every class of all four corpus demos — roughly 204,000
properties. Re-run after any change to flattening; an empty diff for one class is not enough,
because the ordering rules only diverge on particular shapes.


## Per-snapshot entity differential (B13)

`snapshots.rs` is a second oracle example — same install as `flatprops.rs` — printing one line
per entity update:

```
<snapshot>	<entity>	<update-type>	<class>	<prop-index>,<prop-index>,...
```

`DumpFlattened.cs` produces the same shape with `<demo> snapshots <limit>`. Diff them and read
the *first* difference only; a misaligned bit reader reports impossible values long after it
went wrong, so every later line is noise.

`z1800-snapshots-oracle-head.tsv` is the first 60 lines of the oracle's output, kept so the
comparison can be sanity-checked without rebuilding Rust.
