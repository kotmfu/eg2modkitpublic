"""Emit golden.json: expected outputs the C# port must reproduce exactly.

Run from this folder:  python make_golden.py > golden.json
The GUI's Diagnostics > Self-test recomputes every entry and compares.
"""
import hashlib, json, struct, sys, zlib
sys.path.insert(0, ".")
from deflate4k import zlib4k

def lcg_bytes(n, seed):
    x, out = seed, bytearray()
    for _ in range(n):
        x = (x * 1103515245 + 12345) & 0x7FFFFFFF
        out.append(0x41 + ((x >> 16) & 0x0F))
    return bytes(out)

def key_hash(key):
    h = 0
    for c in key.lower().encode("ascii"):
        h = (h * 31 + c) & 0xFFFFFFFF
    return h

def zbb(payload, block):
    blocks = []
    for i in range(0, max(len(payload), 1), block):
        raw = payload[i:i + block]
        blocks.append((zlib4k(raw), len(raw)))
    out = bytearray()
    for i, (s, n) in enumerate(blocks):
        if i:
            out += struct.pack("<2I", len(s), n)
        out += s
    return b"AsuraZbb" + struct.pack("<4I", len(out) + 8, len(payload), len(blocks[0][0]), blocks[0][1]) + bytes(out)

fox = b"The quick brown fox jumps over the lazy dog. " * 200
lcg = lcg_bytes(5000, 12345)
inputs = {
    "empty": b"",
    "one": b"a",
    "fox": fox,
    "lcg": lcg,
    "mixed": lcg + fox,
}
sha = lambda b: hashlib.sha256(b).hexdigest()
g = {"zlib4k": {}, "zbb": {}, "key_hash": {}, "inputs": {
    "fox": "'The quick brown fox jumps over the lazy dog. ' x200 (ASCII)",
    "lcg": "5000 bytes: x=(x*1103515245+12345)&0x7fffffff from seed 12345; byte=0x41+((x>>16)&0xF)",
    "mixed": "lcg + fox"}}
for name, data in inputs.items():
    z = zlib4k(data)
    assert zlib.decompress(z, 12) == data
    g["zlib4k"][name] = {"len": len(z), "sha256": sha(z)}
for name, block in (("mixed_block4096", 4096), ("fox_block2M", 2 * 1024 * 1024)):
    data = inputs["mixed"] if name.startswith("mixed") else inputs["fox"]
    z = zbb(data, block)
    g["zbb"][name] = {"block": block, "len": len(z), "sha256": sha(z)}
for k in ["FE_NEW_GAME", "HEALTHSMARTSRESTORE_01_NRM_NAME", "EVILBUNK_NAME", "ROOM"]:
    g["key_hash"][k] = f"0x{key_hash(k):08x}"
json.dump(g, sys.stdout, indent=2)
print()
