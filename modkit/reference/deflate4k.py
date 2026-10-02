"""Reference zlib encoder with a 4 KiB window (the window Rebellion's archives
declare: zlib header 0x48 0x89).

.NET's ZLibStream always writes a 32 KiB-window stream, and we cannot know
whether the game's inflater is initialised for 4 KiB, so the C# port emits
streams with this encoder instead. This Python version is the executable
spec: the C# encoder must produce byte-identical output (see golden.json).

Algorithm (deliberately simple and deterministic):
  * greedy LZ77, 3-byte hash (15 bits), chain limit 32, min 3 / max 258,
    distance <= 4096
  * a single final fixed-Huffman block (BTYPE=01)
  * zlib header 48 89, Adler-32 trailer
"""

import struct
import zlib

LEN_BASE = [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31,
            35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258]
LEN_EXTRA = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2,
             3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0]
DIST_BASE = [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193,
             257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145,
             8193, 12289, 16385, 24577]
DIST_EXTRA = [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6,
              7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13]

WINDOW = 4096
MIN_MATCH, MAX_MATCH = 3, 258
HASH_BITS = 15
HASH_MASK = (1 << HASH_BITS) - 1
MAX_CHAIN = 32


class BitWriter:
    def __init__(self):
        self.out = bytearray()
        self.acc = 0
        self.n = 0

    def bits(self, value, count):          # LSB-first
        self.acc |= value << self.n
        self.n += count
        while self.n >= 8:
            self.out.append(self.acc & 0xFF)
            self.acc >>= 8
            self.n -= 8

    def huff(self, code, length):          # Huffman codes go MSB-first
        rev = 0
        for _ in range(length):
            rev = (rev << 1) | (code & 1)
            code >>= 1
        self.bits(rev, length)

    def flush(self):
        if self.n:
            self.out.append(self.acc & 0xFF)
            self.acc = 0
            self.n = 0
        return bytes(self.out)


def _lit(w, v):
    if v < 144:
        w.huff(0x30 + v, 8)
    elif v < 256:
        w.huff(0x190 + v - 144, 9)
    elif v < 280:
        w.huff(v - 256, 7)
    else:
        w.huff(0xC0 + v - 280, 8)


def _length(w, length):
    i = 0
    while i + 1 < len(LEN_BASE) and LEN_BASE[i + 1] <= length:
        i += 1
    _lit(w, 257 + i)
    if LEN_EXTRA[i]:
        w.bits(length - LEN_BASE[i], LEN_EXTRA[i])


def _dist(w, dist):
    i = 0
    while i + 1 < len(DIST_BASE) and DIST_BASE[i + 1] <= dist:
        i += 1
    w.huff(i, 5)
    if DIST_EXTRA[i]:
        w.bits(dist - DIST_BASE[i], DIST_EXTRA[i])


def _hash(data, i):
    return ((data[i] << 10) ^ (data[i + 1] << 5) ^ data[i + 2]) & HASH_MASK


def deflate_fixed(data: bytes) -> bytes:
    n = len(data)
    head = [-1] * (1 << HASH_BITS)
    prev = [-1] * n
    w = BitWriter()
    w.bits(1, 1)      # BFINAL
    w.bits(1, 2)      # BTYPE = 01 fixed Huffman
    i = 0
    while i < n:
        best_len, best_dist = 0, 0
        if i + MIN_MATCH <= n:
            h = _hash(data, i)
            cand = head[h]
            chain = 0
            limit = min(MAX_MATCH, n - i)
            while cand >= 0 and i - cand <= WINDOW and chain < MAX_CHAIN:
                l = 0
                while l < limit and data[cand + l] == data[i + l]:
                    l += 1
                if l > best_len:
                    best_len, best_dist = l, i - cand
                    if l == limit:
                        break
                cand = prev[cand]
                chain += 1
        if best_len >= MIN_MATCH:
            _length(w, best_len)
            _dist(w, best_dist)
            end = i + best_len
            while i < end:
                if i + MIN_MATCH <= n:
                    h = _hash(data, i)
                    prev[i] = head[h]
                    head[h] = i
                i += 1
        else:
            _lit(w, data[i])
            if i + MIN_MATCH <= n:
                h = _hash(data, i)
                prev[i] = head[h]
                head[h] = i
            i += 1
    _lit(w, 256)
    return w.flush()


def zlib4k(data: bytes) -> bytes:
    return b"\x48\x89" + deflate_fixed(data) + struct.pack(">I", zlib.adler32(data) & 0xFFFFFFFF)


if __name__ == "__main__":
    import sys, time
    for p in sys.argv[1:]:
        raw = open(p, "rb").read()[:300_000]
        t = time.time()
        z = zlib4k(raw)
        dt = time.time() - t
        back = zlib.decompress(z, 12)          # enforces the 4 KiB window
        assert back == raw
        print(f"{p}: {len(raw)} -> {len(z)} ({len(z)/max(1,len(raw)):.2%}) {dt:.2f}s  ok, window-12 decode")
