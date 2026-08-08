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

**What they establish.** Both lists hold exactly 741 properties, exactly 235 of them array
elements, and the sets of names are identical — `comm` reports nothing unique to either side.
Only the order differs, first at index 20. So the schema parser and the exclusion rules are
right, and the fault is confined to the sequencing rules in `SchemaFlattener`.
