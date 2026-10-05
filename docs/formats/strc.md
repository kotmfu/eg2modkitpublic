# Stat reactions (`STRC`)

Part of the [file format reference](../REFERENCE.md): [4.37 GUI, fonts, input and stats](../REFERENCE.md#437-gui-fonts-input-and-stats).

**Stat reactions (`STRC`, `stat\stat.asr`, 1).** `[u32 1][u32 0][u32 118]` + 118 x `[u32 reaction id]` prop 1
{ flowgraph }: "… Reward Reaction" graphs that grant achievements, "Recruit Minion Type Reaction - …" graphs, and
`<stat>_UpdateCodeParams` graphs that feed stats to the game code.

The `STSM` and `STRC` flowgraphs share one layout, different from [4.20](../REFERENCE.md#420-scripts-flowgraphs):

```
u32 5, u32 node count
node count x [u32 2][u32 node type hash][u32 size] + node body
"<name>/Flowgraph\0", [u32 1][u32 n][u32 0], prop 1 { u32 n, n x [u32 node id][u32 node index] }, …
```

They use 36 node types in all. Node pins appear as `[i32 -1][u32 0][u32 hash][u32 index][u32 hash]`.
