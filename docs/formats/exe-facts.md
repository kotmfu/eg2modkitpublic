# Exe facts

Part of the [file format reference](../REFERENCE.md): [4.27 Exe facts](../REFERENCE.md#427-exe-facts).

| Fact | Where |
|---|---|
| Save path built from base dir + `PC_ProfileSaves` + steam id | RVA `0x4f03a0` (DX12 exe) |
| Minion hard cap and its HUD copy | Data globals; the HUD copies the cap once at startup and shows "Max" when the count reaches the copy |
| Videos | CRT `fopen_s("fmv/%s.webm")` |
