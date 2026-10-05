# Runtime config (`bin\eg2modkit.cfg`)

Part of the [file format reference](../REFERENCE.md): [4.28 Runtime config (`bin\eg2modkit.cfg`)](../REFERENCE.md#428-runtime-config-bineg2modkitcfg).

INI-like: one `[section]` per entry, `#` comments.

```ini
[minion_hard_cap]
pattern = 8B 3D ?? ?? ?? ?? 8B 8B ?? ?? ?? ?? 03 0D ?? ?? ?? ?? 03 8B ?? ?? ?? ?? 3B F9 0F 43 F9
rel = 2          ; rel32 at match+at+rel ...
len = 6          ; ... of an instruction this long -> the global's address
type = u32       ; u8 u16 u32 i32 u64 f32 f64
value = 1000
```

| Key | Meaning |
|---|---|
| `pattern` | Bytes to find, `??` = any. It must resolve to one address. |
| `at`, `rel`, `len` | Instruction start from the match, and its rel32 operand |
| `deref`, `offset` | Follow a pointer, then add an offset |
| `skip_if` | Skip the write while the current value equals this |
| `kind = hashmap_record` | `hit, id_at, mirror_at, offset`: a per-save record in the game's resource hash map, found by its HUD mirror [current][max] |
| `kind = table_lookup` | `id_at, alt_at, alt_if, call_at, offset`: a record the game looks up by id |
| `kind = island_alias` | `value = source stem, stem = new stem, region = 0x…`: open the new lair's island files in place of the source's |
| `kind = file_block` | `value = game file, offset, source`: bytes from `bin\eg2modkit_blocks\<source>` go into reads and mapped views of that file |
| `kind = trace_lair` | Logs the code that reads a lair's record |

The protector unpacks the exe after start, so the proxy retries unresolved patterns for 2 minutes and then writes
each value every second.
