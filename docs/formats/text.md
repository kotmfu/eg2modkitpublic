# Text (`HTXT`)

Part of the [file format reference](../REFERENCE.md): [4.5 Text (`HTXT`)](../REFERENCE.md#45-text-htxt).

`text\pc\<table>\<table>.asr_<lang>`, 32 tables in the base game.

```
u32 version (4), u32 0, u32 count, u32 KeyHash(table name), u32 text_bytes, u32 0
count x { u32 KeyHash(key), u32 units (UTF-16 units incl. NUL), u16 text[units] }
key table: name\0 padded so the field is a multiple of 4, u32 size, keys\0... in entry order
```

- Entries are unsorted.
- U+E003…U+E004 sequences mark inline icons, value slots and colour spans.
- The game loads only the tables `text\pc\localisation.asr` lists. It ignores text inside a package.
- A `DLLN` line id in `misc\Common.asr_en` doubles as the key of its subtitle, mostly in table `character`.
