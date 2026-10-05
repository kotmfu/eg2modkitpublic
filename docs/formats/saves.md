# Saves (`slotN.sav`)

Part of the [file format reference](../REFERENCE.md): [4.26 Saves (`slotN.sav`)](../REFERENCE.md#426-saves-slotnsav).

```
"Asura   " "AsuraZlb"  u32 compressed length  u32 (raw length - 16)  zlib stream -> an ordinary Asura archive
```

A save holds `bsnf` (with the lair's region), `DYMG`, `DLIG`, `SMXG`, `ATIG`, `ARNM`, `dtvs`, `stsy`, `ttsy` and
about 500 `ENTI`, all hashes and numbers. The grid uses the `.base` format, and current saves store each placed
object as version 104. `slot0` is a raw 8 KB property block zero-padded to 2 MB.
