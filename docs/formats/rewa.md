# Achievements (`REWA`)

Part of the [file format reference](../REFERENCE.md): [4.30 Other engine chunks](../REFERENCE.md#430-other-engine-chunks).

**Achievements (`REWA`, `rewards\rewards.asr`).** `[u32 1][u32 0][u32 93]`, then 93 records
`[u64 id]` prop 2 { `[u64 id][u32 1][u32 n]` + n fields, `[u32 0]` }. A field is `[u32 KeyHash of the name][u8 type]`
+ value: type 1 is a null-terminated string padded to 4 bytes, type 2 a u64.

| Field | Value |
|---|---|
| `steamid` | `EG2_ACH_0NN` |
| `xboxid` | Number as a string |
| `playstationid` | Trophy number; 0 when the achievement has no trophy |
| `epicid` | Empty |
| `stadiaid` | 0 |

`STRC` in `stat\stat.asr` names each record id once, in that achievement's reaction flowgraph.
