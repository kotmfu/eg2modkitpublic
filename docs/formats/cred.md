# Credits (`CRED`)

Part of the [file format reference](../REFERENCE.md): [4.37 GUI, fonts, input and stats](../REFERENCE.md#437-gui-fonts-input-and-stats).

**Credits (`CRED`, `gui\gui.asr`, 1).** `[u32 1][u32 0][u32 1][u32 0xa35b867e]`, then prop 0 { `[u32 0xa35b867e]`
prop 1 { `[u32 147]` + 147 lines } }. A line is prop 0 { `[u32 0][u32 menu][u32 key hash][u32 style]` prop 1
{ `[u32 n]` + n prop 1 { name\0 padded with zeros } } }. The key names a `menu` text (`CREDITS_CEO_DIR` "CEO",
`CREDITS_DEVELOPMENT_TEAM` "Development Team"); the n names follow it (434 in all). Some name buffers carry leftover
bytes after the zero padding.

| Style | Lines | Use |
|---|---|---|
| 0 | 1 | Title ("Rebellion") |
| 1 | 6 | Section heading ("Development Team", "Special Thanks") |
| 2 | 32 | Sub-heading or role with a name ("CEO", "Production") |
| 3 | 106 | Role and names ("Lead Producer") |
| 4 | 2 | Video editing roles |
