# Schemes (`rscm`, `rspl`, `rsdv`)

Part of the [file format reference](../REFERENCE.md): [4.18 Schemes (`rscm`, `rspl`, `rsdv`)](../REFERENCE.md#418-schemes-rscm-rspl-rsdv).

`rscm` first prop `0x38`:

| Part | Meaning |
|---|---|
| raw6 f32 | Duration in seconds |
| raw16[1] | Payout interval of "while running" schemes |
| raw16[3] | Heat gain to region (negative lowers heat) |
| minion list | Groups of alternatives `[n] + prop4 [type][count][category][u8 1]`; the game picks one group per offer |
| raw8 f32 | Offer expiry (-1 = stays) |
| child [9] | Launch cost `[rcns][amount]` pairs |

Minion categories: Worker 0, Guard types 1, Valet types 2, Technician types 3.

`rspl` (scheme pool): after the 24-byte header, `[u32 0x80000002][u8 0][u32 8 + 8n][u32 n][n x (u32 rscm, u32
weight 1)][u32 ?]`. Region upgrade rules (`rrtl`) and objective steps refer to pools. `rsdv` names world-map schemes
by matching `rtag`s.
