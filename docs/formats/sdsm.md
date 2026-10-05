# Sound samples (`SDSM`)

Part of the [file format reference](../REFERENCE.md): [4.32 Sound and dialogue](../REFERENCE.md#432-sound-and-dialogue).

**Sound samples (`SDSM`, `envs\*.pc`, `misc\common.asr`, packages, `sounds\gmsndmeta.asr`; 8816).** One WAV per chunk,
version 9. `misc\common.asr` holds 4254, packages 3921, `gmsndmeta.asr` 289 and each island 80 to 97.

```
[u32 9][u32 0][name]
[u32 KeyHash(path with / separators)][path]           Sounds\Props\Satellite\PRP_..._Loop_01.wav
[f32 a][f32 1][f32 a]                                  a = 1 in 8774; 0.1 to 1.5 otherwise
[u8 loop]                                              1 on 783 samples, 777 named *Loop*
[category][subcategory]                                Vocalisation/Vocalisation2, Props/Props_02, Weapons/WPN, Doomsday/Props
[u32 n][n x [u32 p][i32 q]]                            (0,-1)(15,-1) or (0,-1)(14,-1)(15,-1) on 270 samples, mostly loops
["Default"][6 bytes 0][u32 0xe9712026][f32 t0][f32 t1]
```

- An event refers to a sample by KeyHash of the sample name. The stored hash covers the path.
- The WAV is the `RSCF` whose path is the lower-cased path. 8450 samples sit in the `.pc.sounds` beside
  their file. The other 366 sit in `sounds\gmsnd.asr.pc.sounds`, the shared bank.
- `0xe9712026` is KeyHash(`StreamingSounds`) in every sample.
- `t0` is 0 to 0.4 and `t1` 0 to 50. Both are 0 in 8701 samples; `t1` is 4 to 50 on ambience loops, probably a distance.

**Field values.** - The `[u32 p][i32 q]` list appears on 270 samples and lists `p` = 0 and 15, or 0, 14 and 15, with `q` = -1 (one
  sample stores (15, 2)). Every sample without the list keeps the defaults: `a` = 1 and `t0` = `t1` = 0. The list
  probably marks the fields edited away from their defaults: 157 listed samples change `a` or `t0`/`t1`, the other 113
  store defaults.
- `t0`/`t1` is a pair: (0, 4 to 50) on 99 ambience loops (`ENV_Insects_Jungle_Loop_01` 0, 10), (0.1, 0.1) on 13 foley
  samples, (0.1, 5), (0, 15) and (0.4, 0.4) on single props. It probably sets a random start-offset range in seconds.
