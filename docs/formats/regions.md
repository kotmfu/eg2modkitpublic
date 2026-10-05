# Regions (`rmlr`, `rrtl`)

Part of the [file format reference](../REFERENCE.md): [4.19 Regions (`rmlr`, `rrtl`)](../REFERENCE.md#419-regions-rmlr-rrtl).

`rmlr` holds a world-map region: +45 name key, +83 KeyHash(`<stem>.base`) (the lair map it picks), +100 globe slot,
heat upgrades and scheme tags. `rrtl` holds region upgrades; `UpgradeHeatScheme_0N_<ANVIL|PATRIOT|OCEAN>` spawn the
heat-lowering schemes.
