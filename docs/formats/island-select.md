# Island select

Part of the [file format reference](../REFERENCE.md): [4.24 Island select](../REFERENCE.md#424-island-select).

- At startup the exe registers each `*.base` in `envs\basedefinitions\`, plus `JustAPlane.base` as a fallback.
- A `felr` object in `frontend` describes an island-select entry: picture keys, point-of-interest texts, name and
  description keys `LAIR_<STEM>_NAME/_DESC`, and a ref to the island's `rmlr` (+280 on Caine Key, +284 on the
  others).
- The region (`rmlr`) picks the map through +83 = KeyHash(`<stem>.base`).
- The game then fetches per-lair settings by lair id from `misc\common.asr` `BLUE` ([4.25](../REFERENCE.md#425-lair-settings-misccommonasr-blue-25-chunk-27)).
- `fegd` (frontend) describes a genius on the new-game screen: +65 actor-type id, +73/+77 GUI keys of
  `t_frontend_geniusselect_photo_X` and `t_icon_geniusicon_X`, +332/+340 the two `t_geniusability_X_00N` pictures.
