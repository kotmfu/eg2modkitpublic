# Streamed sounds (`*.pc.streamsounds`)

Part of the [file format reference](../REFERENCE.md): [4.11 Streamed sounds (`*.pc.streamsounds`)](../REFERENCE.md#411-streamed-sounds-pcstreamsounds).

```
"Asura   " "ASTS" u32 (file length - 12), u32 2, u32 0, u32 n, u8 0
n x [name NUL-padded to 4 from its start][u8 flag][u32 size][u32 absolute offset]
WAVs back to back, then 4 zero bytes
```

`sounds\streamingsounds.ssm.pc.sounds` holds only an `ASTS` table (no data) of 6 music and ambience streams, with offsets into `sounds\streamingsounds.asr.pc.streamsounds`.
`sounds\streamingsounds.ssm.pc.streamsounds` copies the table of `streamingsounds.asr.pc.streamsounds` (byte
+28 = 1, offsets into the store). 531 names appear twice with identical data. Music is stereo MS-ADPCM (block 76),
dialogue mono (block 22).
