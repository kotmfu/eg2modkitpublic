# Facial animation (`FAAN`)

Part of the [file format reference](../REFERENCE.md): [4.36 Animation and skeletons](../REFERENCE.md#436-animation-and-skeletons).

**Facial animation (`FAAN`, same files, 5508).** One per facial clip.

```
u32 3, u32 0
u32 clip hash                 KeyHash of the HCAN name it accompanies
u32 has second, u32 hash      0, 0 on all but 8
u32 3
f32 duration
u32 channels                  189 in every instance
u32 0
u32 keys                      total
channels x 8                  [u32 first key][u32 key count]
keys x u16                    time in milliseconds (duration x 1000 for the last key)
keys x u8                     value
```

- First-key fields chain as in `HCAN`.
- 5489 clip hashes name an `HCAN` in the install; 19 name nothing found.
- Up to 12676 keys and 40 s.
