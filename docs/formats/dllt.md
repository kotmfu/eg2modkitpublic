# Dialogue line templates (`DLLT`)

Part of the [file format reference](../REFERENCE.md): [4.32 Sound and dialogue](../REFERENCE.md#432-sound-and-dialogue).

**Dialogue line templates (`DLLT`; 21).** One per dialogue file and content package.

```
[u32 11][u32 0][name][u32 KeyHash(name)][10 bytes]
```

20 copies hold `DefaultLineTemplate` with `01 00 00 00 00 00 00 00 00 00`. The one in `sounds\gmsndmeta.asr_en` holds
`Default` with `00 02 00 02 00 00 00 00 00 00`.
