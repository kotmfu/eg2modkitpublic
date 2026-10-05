# Chunks

Part of the [file format reference](../REFERENCE.md): [4.2 Chunks](../REFERENCE.md#42-chunks).

Uppercase tags mark engine chunks and lowercase tags mark game data. Chunks sit back to back with no padding.

| Tag | Contents |
|---|---|
| `FNFO` | First chunk. `u32 1, u32 flags (0/4/11/15), u32 payload_size (= payload length - 4), u32 8` |
| `RSFL` | Resource list: `u32 ver, u32 0, u32 count`, entries `name\0` padded to 4 + `u32 hash, u32 size, u32 count`. In a manifest, entry 0's size equals the `rpkg` chunk size. In `misc\common.asr` the entries hold payload offsets, so nothing before a resource may change size. |
| `RSCF` | Embedded file ([4.6](../REFERENCE.md#46-embedded-files-rscf)) |
| `HTXT` | Text table ([4.5](../REFERENCE.md#45-text-htxt)) |
| `HTPR` | One text table name in `localisation.asr`: `[u32 1][u32 0][NAME\0 padded to 4]` |
| `COMA` | Per-item art binding keyed by the u32 at +12. Keep the list sorted by that key; an unsorted list hangs the loader. |
| `HSKN`, `HSKL`, `HMPT`, `HSBB`, `HSKE` | Model and skeleton, LOD names, mount points, bounds, end of a model run ([4.7](../REFERENCE.md#47-models-and-meshes), [4.36](../REFERENCE.md#436-animation-and-skeletons)) |
| `HCAN`, `FAAN` | Animations and facial animation ([4.36](../REFERENCE.md#436-animation-and-skeletons)) |
| `MARE` | Materials ([4.8](../REFERENCE.md#48-materials-mare)) |
| `TXST` | Texture streaming table, in `.ts` files ([4.9](../REFERENCE.md#49-texture-streaming-ts--txst-pc_textures)) |
| `ASTS` | Streamed sound store ([4.11](../REFERENCE.md#411-streamed-sounds-pcstreamsounds)) |
| `ENTI` | Entity: lair grid, characters, island entities ([4.22](../REFERENCE.md#422-lair-maps-envsbasedefinitionslairbase)) |
| `INST` | Island scenery instances ([4.23](../REFERENCE.md#423-islands-envslairpc)) |
| `BLUE` | Class-tree data, including lair settings in `common.asr` ([4.25](../REFERENCE.md#425-lair-settings-misccommonasr-blue-25-chunk-27)) |
| `GUAT` | GUI layout ([4.21](../REFERENCE.md#421-gui-layouts-guat)) |
| `GUAP`, `FONT`, `FNTK` | GUI parameters, fonts, kerning ([4.37](../REFERENCE.md#437-gui-fonts-input-and-stats)) |
| `DLLN`, `DLEV`, `DLET`, `DLLT` | Dialogue lines, events and templates ([4.32](../REFERENCE.md#432-sound-and-dialogue)) |
| `PSKY`, `FOG `, `PLUT`, `PHEN`, `NAV1`, `PBRV`, `OCMH`, `CT*`, `FX*` | Island sky, fog, colour grade, physics mesh, navmesh stub, probe volume, occlusion, cutscenes and effects ([4.33](../REFERENCE.md#433-effects-and-cutscenes), [4.34](../REFERENCE.md#434-island-level-chunks-and-entities)) |
| `bsnf`, `ARNM`, `dtvs`, `stsy`, `ttsy`, `DYMG`, `DLIG`, `SMXG`, `ATIG` | Map and save state |
| `rpkg` | Package manifest ([4.4](../REFERENCE.md#44-packages-rpkg)) |
| other lowercase | Data objects ([4.3](../REFERENCE.md#43-data-objects-and-properties)) |
