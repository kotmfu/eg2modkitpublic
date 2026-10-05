// Eg2 ModKit runtime tweaks: an xinput1_4.dll proxy loaded from the game's bin\ folder.
// (This folder is still named winmm\ for history; the DLL it builds is xinput1_4.dll.)
//
// * We proxy xinput1_4, not winmm: the game imports both, but Steam's steamclient64.dll imports ~15
//   winmm functions, so a winmm proxy must forward them all or SteamAPI_Init fails. Nothing in Steam
//   (or the overlay) imports xinput1_4, so proxying it is safe and small -- 14 exports vs winmm's 180.
// * Every xinput1_4 export is a stub (stubs.asm) jumping through g_real[i], which DllMain fills from the
//   real system xinput1_4.dll. The game imports XInputGetState/SetState/GetCapabilities by ordinal (2/3/4).
// * A worker thread reads eg2modkit.cfg (next to this DLL): each [section] names a byte
//   pattern, how to get from the match to a value, and what to write. The DLL itself knows
//   nothing about the game, so new tweaks are config-only. Meant for DATA (globals, struct
//   fields); only kind = code_bytes entries change game code.
// * Intro/outro videos (fmv\*.webm) go the same way; a 0-byte .asrpatch skips the video (the open fails as missing).
// * Lair maps (Envs\BaseDefinitions\*.base) are read like saves, without the engine's .asrpatch lookup, so the
//   exe's CreateFileA/W imports are pointed at wrappers that open <file>.asrpatch instead when ModKit installed one.
// * Everything it does goes to eg2modkit.log next to the DLL.
#include <windows.h>
#include <cstdio>
#include <cstdint>
#include <cstring>
#include <string>
#include <vector>
#include <map>
#include <type_traits>
#include <algorithm>

// The jump targets for the export stubs in stubs.asm; filled in DllMain from the real xinput1_4.dll.
// Order matches stubs.asm / exports.def: 7 named, then the 7 NONAME ordinals (100,101,102,103,104,108,109).
extern "C" void* g_real[];

static wchar_t g_dir[MAX_PATH];
static FILE* g_log;

static void Log(const char* fmt, ...)
{
    if (!g_log) return;
    SYSTEMTIME t; GetLocalTime(&t);
    fprintf(g_log, "%02d:%02d:%02d.%03d  ", t.wHour, t.wMinute, t.wSecond, t.wMilliseconds);
    va_list a; va_start(a, fmt); vfprintf(g_log, fmt, a); va_end(a);
    fputc('\n', g_log);
    fflush(g_log);
}

// ------------------------------------------------------------------ pattern scan

struct Pattern { std::vector<int> bytes; };   // -1 = wildcard

static Pattern Parse(const char* s)
{
    Pattern p;
    for (const char* c = s; *c;) {
        if (*c == ' ') { c++; continue; }
        if (*c == '?') { p.bytes.push_back(-1); c += (c[1] == '?') ? 2 : 1; continue; }
        p.bytes.push_back((int)strtol(std::string(c, 2).c_str(), nullptr, 16));
        c += 2;
    }
    return p;
}

static bool Readable(DWORD prot)
{
    return !(prot & (PAGE_NOACCESS | PAGE_GUARD)) && prot != 0;
}

/// All matches inside the main module's committed, readable pages.
static std::vector<uint8_t*> Scan(const Pattern& p)
{
    std::vector<uint8_t*> hits;
    auto base = (uint8_t*)GetModuleHandleW(nullptr);
    auto nt = (IMAGE_NT_HEADERS*)(base + ((IMAGE_DOS_HEADER*)base)->e_lfanew);
    uint8_t* end = base + nt->OptionalHeader.SizeOfImage;
    size_t n = p.bytes.size();
    for (uint8_t* at = base; at < end;) {
        MEMORY_BASIC_INFORMATION mi{};
        if (!VirtualQuery(at, &mi, sizeof mi)) break;
        auto rb = (uint8_t*)mi.BaseAddress, re = rb + mi.RegionSize;
        if (mi.State == MEM_COMMIT && Readable(mi.Protect) && re - rb >= (ptrdiff_t)n) {
            uint8_t first = (uint8_t)p.bytes[0];
            for (uint8_t* q = rb; q + n <= re;) {
                q = (uint8_t*)memchr(q, first, re - q - n + 1);
                if (!q) break;
                size_t k = 1;
                while (k < n && (p.bytes[k] < 0 || q[k] == (uint8_t)p.bytes[k])) k++;
                if (k == n) hits.push_back(q);
                q++;
            }
        }
        at = re;
    }
    return hits;
}

/// Address of a RIP-relative operand: instruction at `ins`, rel32 at +relAt, instruction length `len`.
static uint8_t* Rip(uint8_t* ins, int relAt, int len) { return ins + len + *(int32_t*)(ins + relAt); }

// ------------------------------------------------------------------ safe writes

static bool Writable(void* p, size_t n)
{
    MEMORY_BASIC_INFORMATION mi{};
    if (!VirtualQuery(p, &mi, sizeof mi) || mi.State != MEM_COMMIT) return false;
    if (mi.Protect & (PAGE_READWRITE | PAGE_WRITECOPY | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)) return true;
    DWORD old;
    return VirtualProtect(p, n, PAGE_READWRITE, &old) != 0;   // data pages only, never code
}

static bool ReadablePtr(const void* p, size_t n)
{
    MEMORY_BASIC_INFORMATION mi{};
    return p && VirtualQuery(p, &mi, sizeof mi) && mi.State == MEM_COMMIT && Readable(mi.Protect)
        && (const uint8_t*)p + n <= (const uint8_t*)mi.BaseAddress + mi.RegionSize;
}

// ------------------------------------------------------------------ config-driven patches
//
// eg2modkit.cfg: one [section] per value to write, so new tweaks never need a new DLL.
//
//   [minion_hard_cap]
//   pattern = 8B 3D ?? ?? ?? ?? 8B 8B        ; bytes to find in the game (?? = any)
//   at      = 0        ; instruction start, relative to the match          (default 0)
//   rel     = 2        ; RIP-relative operand: rel32 at +rel ...            (omit for none)
//   len     = 6        ; ... of an instruction this long
//   deref   = 0        ; 1 = the resolved address holds a pointer; follow it every time
//   offset  = 0        ; added last (field inside a struct)
//   type    = u32      ; u8 u16 u32 i32 u64 f32 f64
//   value   = 1000
//   skip_if = 0xFFFFFFFF   ; don't write while the current value equals this (not initialised yet)
//
// A pattern must resolve to exactly one address (several matches are fine if they all
// resolve to the same one). Values are re-applied every second.

//   kind      = hashmap_record  ; a per-save record reached through the game's resource hash map:
//   hit       = 0               ;   use the pattern's Nth match (sorted by address)
//   id_at     = 19              ;   `mov esi,[rip+id]` at match+id_at (the record's key)
//   mirror_at = 25              ;   `mov [rip+mirror],eax` at match+mirror_at; mirror = [current][max]
//   offset    = 4               ;   field in the record to write
//   kind      = table_lookup    ; a record the game looks up by id in a static table (e.g. its game-setup preset):
//   id_at     = 0               ;   `mov r32,[rip+id]` at match+id_at
//   alt_at    = 12              ;   `cmove r32,[rip+alt]` at match+alt_at: the id used when *id == alt_if
//   alt_if    = 0xAF968B71      ;   (hash of "custom")
//   call_at   = 19              ;   `call lookup` at match+call_at; the lookup's code gives the table: count @+4,
//                               ;   capacity @+23, values @+38, keys @+55 (checked before use)
//   offset    = 0x58            ;   field in the record to write
// The record is the value of the heap hash map entry whose key is *id and whose [current][max]
// equal the mirror the game keeps up to date. Found by scanning private read/write memory for
// the map ({lock, pad, u64 values*, u64 keys*, u32 capacity (power of 2), u32 count}, quadratic
// probing, see the game's lookup), then cached and re-found if it stops matching.

struct Entry
{
    std::string name, pattern, type = "u32", value, kind, stem, source;
    int at = 0, rel = -1, len = 0, offset = 0, hit = 0, idAt = 0, mirrorAt = 0, altAt = 0, callAt = 0;
    uint32_t altIf = 0, region = 0;
    // table_lookup
    uint32_t *altPtr = nullptr, *countPtr = nullptr, *capPtr = nullptr;
    uint64_t *valsPtr = nullptr, *keysPtr = nullptr;
    uint32_t lastId = 0;
    bool deref = false, hasSkip = false;
    mutable bool seen = false;          // the first value found has been logged
    long long skipIf = 0;
    uint8_t* resolved = nullptr;   // address after rip; before deref/offset
    bool failed = false;
    // hashmap_record
    uint32_t* idPtr = nullptr;
    uint32_t* mirror = nullptr;
    uintptr_t map = 0;
    int misses = 0;
    ULONGLONG nextScan = 0;
    uint64_t lastSeen = ~0ull;
};

static std::string Trim(std::string s)
{
    while (!s.empty() && isspace((unsigned char)s.back())) s.pop_back();
    size_t i = 0;
    while (i < s.size() && isspace((unsigned char)s[i])) i++;
    return s.substr(i);
}

static std::vector<Entry> LoadConfig()
{
    std::vector<Entry> out;
    FILE* f = _wfopen((std::wstring(g_dir) + L"\\eg2modkit.cfg").c_str(), L"r");
    if (!f) return out;
    char buf[1024];
    while (fgets(buf, sizeof buf, f)) {
        std::string line = buf;
        if (auto c = line.find(';'); c != std::string::npos) line.erase(c);
        if (auto c = line.find('#'); c != std::string::npos) line.erase(c);
        line = Trim(line);
        if (line.empty()) continue;
        if (line[0] == '[') { out.push_back({}); out.back().name = Trim(line.substr(1, line.find(']') - 1)); continue; }
        auto eq = line.find('=');
        if (eq == std::string::npos || out.empty()) continue;
        auto key = Trim(line.substr(0, eq)), v = Trim(line.substr(eq + 1));
        auto& e = out.back();
        auto num = [&] { return (int)strtol(v.c_str(), nullptr, 0); };
        if (key == "pattern") e.pattern = v;
        else if (key == "at") e.at = num();
        else if (key == "rel") e.rel = num();
        else if (key == "len") e.len = num();
        else if (key == "deref") e.deref = num() != 0;
        else if (key == "offset") e.offset = num();
        else if (key == "type") e.type = v;
        else if (key == "value") e.value = v;
        else if (key == "skip_if") { e.hasSkip = true; e.skipIf = _strtoi64(v.c_str(), nullptr, 0); }
        else if (key == "kind") e.kind = v;
        else if (key == "stem") e.stem = v;
        else if (key == "source") e.source = v;
        else if (key == "region") e.region = (uint32_t)_strtoui64(v.c_str(), nullptr, 0);
        else if (key == "hit") e.hit = num();
        else if (key == "id_at") e.idAt = num();
        else if (key == "mirror_at") e.mirrorAt = num();
        else if (key == "alt_at") e.altAt = num();
        else if (key == "call_at") e.callAt = num();
        else if (key == "alt_if") e.altIf = (uint32_t)_strtoui64(v.c_str(), nullptr, 0);
    }
    fclose(f);
    return out;
}

static int SizeOf(const std::string& t)
{
    if (t == "u8") return 1;
    if (t == "u16") return 2;
    if (t == "u64" || t == "f64") return 8;
    return 4;
}

static uint8_t* ResolveOne(const Entry& e, uint8_t* match)
{
    uint8_t* a = match + e.at;
    return e.rel >= 0 ? Rip(a, e.rel, e.len) : a;
}

/// Returns true once the entry is resolved or has definitely failed.
static bool Resolve(Entry& e, std::map<std::string, std::vector<uint8_t*>>& cache)
{
    if (e.resolved || e.failed) return true;
    if (e.pattern.empty() || e.value.empty()) { Log("[%s] needs pattern and value -- skipped", e.name.c_str()); e.failed = true; return true; }
    auto it = cache.find(e.pattern);
    if (it == cache.end() || it->second.empty()) it = cache.insert_or_assign(e.pattern, Scan(Parse(e.pattern.c_str()))).first;
    auto& hits = it->second;
    if (hits.empty()) return false;                     // code not unpacked yet, try again later
    if (e.kind == "hashmap_record") {
        if (e.hit >= (int)hits.size()) { Log("[%s] pattern has %zu matches, need #%d -- skipped", e.name.c_str(), hits.size(), e.hit); e.failed = true; return true; }
        uint8_t* m = hits[e.hit];                       // Scan returns matches in address order
        e.idPtr = (uint32_t*)Rip(m + e.idAt, 2, 6);
        e.mirror = (uint32_t*)Rip(m + e.mirrorAt, 2, 6);
        e.resolved = m;
        Log("[%s] match #%d of %zu: id at %p, mirror at %p", e.name.c_str(), e.hit, hits.size(), e.idPtr, e.mirror);
        return true;
    }
    if (e.kind == "table_lookup") {
        if (hits.size() != 1) { Log("[%s] pattern has %zu matches, need exactly 1 -- skipped", e.name.c_str(), hits.size()); e.failed = true; return true; }
        uint8_t* m = hits[0];
        uint8_t* fn = Rip(m + e.callAt, 1, 5);
        static const uint8_t want[4][3] = { { 0x83, 0x3D, 0 }, { 0x44, 0x8B, 0x1D }, { 0x48, 0x8B, 0x1D }, { 0x48, 0x8B, 0x3D } };
        static const int at[4] = { 4, 23, 38, 55 }, n[4] = { 2, 3, 3, 3 };
        if (!ReadablePtr(fn, 64)) { Log("[%s] lookup %p not readable -- skipped", e.name.c_str(), fn); e.failed = true; return true; }
        for (int k = 0; k < 4; k++)
            if (memcmp(fn + at[k], want[k], n[k]) != 0) { Log("[%s] lookup %p isn't the expected code (game version changed?) -- skipped", e.name.c_str(), fn); e.failed = true; return true; }
        e.idPtr = (uint32_t*)Rip(m + e.idAt, 2, 6);
        e.altPtr = (uint32_t*)Rip(m + e.altAt, 3, 7);
        e.countPtr = (uint32_t*)Rip(fn + 4, 2, 7);
        e.capPtr = (uint32_t*)Rip(fn + 23, 3, 7);
        e.valsPtr = (uint64_t*)Rip(fn + 38, 3, 7);
        e.keysPtr = (uint64_t*)Rip(fn + 55, 3, 7);
        e.resolved = m;
        Log("[%s] id at %p (alt %p), table count %p cap %p values %p keys %p", e.name.c_str(), e.idPtr, e.altPtr, e.countPtr, e.capPtr, e.valsPtr, e.keysPtr);
        return true;
    }
    if (e.kind == "code_bytes" && hits.size() != 1) { Log("[%s] pattern has %zu matches, need exactly 1 -- skipped", e.name.c_str(), hits.size()); e.failed = true; return true; }
    if (!e.kind.empty() && e.kind != "code_bytes") { Log("[%s] unknown kind '%s' -- skipped", e.name.c_str(), e.kind.c_str()); e.failed = true; return true; }
    uint8_t* a = ResolveOne(e, hits[0]);
    for (auto h : hits)
        if (ResolveOne(e, h) != a) { Log("[%s] %zu matches resolve to different addresses -- skipped", e.name.c_str(), hits.size()); e.failed = true; return true; }
    e.resolved = a;
    Log("[%s] resolved to %p (%zu match%s)", e.name.c_str(), a, hits.size(), hits.size() == 1 ? "" : "es");
    return true;
}

template <class T>
static void Write(const Entry& e, uint8_t* at, T value)
{
    T cur = *(T*)at;
    if (!e.seen) {
        e.seen = true;
        if constexpr (std::is_floating_point_v<T>) Log("[%s] found %g at %p", e.name.c_str(), (double)cur, at);
        else Log("[%s] found %lld at %p", e.name.c_str(), (long long)cur, at);
    }
    if (e.hasSkip && (long long)cur == e.skipIf) return;
    if (memcmp(&cur, &value, sizeof(T)) == 0) return;
    if (!Writable(at, sizeof(T))) { Log("[%s] cannot write at %p", e.name.c_str(), at); return; }
    *(T*)at = value;
    if constexpr (std::is_floating_point_v<T>) Log("[%s] %g -> %g", e.name.c_str(), (double)cur, (double)value);
    else Log("[%s] %lld -> %lld", e.name.c_str(), (long long)cur, (long long)value);
}

// ------------------------------------------------------------------ hashmap_record
//
// Every read of game heap memory goes through ReadProcessMemory on our own process: it fails
// cleanly on pages that vanish mid-read, instead of raising an access violation that the game's
// anti-tamper exception handlers would see before ours.

struct Region { uintptr_t lo, hi; };

static bool Peek(uintptr_t at, void* out, size_t n)
{
    SIZE_T got = 0;
    return ReadProcessMemory(GetCurrentProcess(), (void*)at, out, n, &got) && got == n;
}

static std::vector<Region> PrivateRw()
{
    std::vector<Region> out;
    SYSTEM_INFO si; GetSystemInfo(&si);
    for (auto at = (uint8_t*)si.lpMinimumApplicationAddress; at < (uint8_t*)si.lpMaximumApplicationAddress;) {
        MEMORY_BASIC_INFORMATION mi{};
        if (!VirtualQuery(at, &mi, sizeof mi)) break;
        if (mi.State == MEM_COMMIT && mi.Type == MEM_PRIVATE && (mi.Protect & 0xFF) == PAGE_READWRITE && !(mi.Protect & PAGE_GUARD))
            out.push_back({ (uintptr_t)mi.BaseAddress, (uintptr_t)mi.BaseAddress + mi.RegionSize });
        at = (uint8_t*)mi.BaseAddress + mi.RegionSize;
    }
    return out;
}

static bool In(const std::vector<Region>& rs, uintptr_t p, size_t n)
{
    size_t lo = 0, hi = rs.size();
    while (lo < hi) {                                   // regions are in address order
        size_t mid = (lo + hi) / 2;
        if (rs[mid].hi <= p) lo = mid + 1; else hi = mid;
    }
    return lo < rs.size() && rs[lo].lo <= p && p + n <= rs[lo].hi;
}

struct MapHead { uint32_t lock, pad; uint64_t vals, keys; uint32_t cap, count; };

/// Cheap structural test on a map header already copied into our buffer.
static bool Plausible(const MapHead& h)
{
    return h.cap >= 4 && h.cap <= (1u << 22) && !(h.cap & (h.cap - 1)) && h.count && h.count <= h.cap
        && h.lock < 0x10000 && h.vals > 0x10000 && h.vals < 0x7FFFFFFFFFFFull && !(h.vals & 7)
        && h.keys > 0x10000 && h.keys < 0x7FFFFFFFFFFFull && !(h.keys & 3);
}

/// The game's lookup (G2): slot = key & (cap-1), then slot += ++step (masked); empty slot = absent.
static uintptr_t Lookup(uintptr_t map, uint32_t key)
{
    MapHead h;
    if (!Peek(map, &h, sizeof h) || !Plausible(h)) return 0;
    uint32_t slot = key & (h.cap - 1);
    for (uint32_t step = 0; step < h.cap; ) {
        uint64_t v; uint32_t k;
        if (!Peek(h.vals + slot * 8ull, &v, 8) || !Peek(h.keys + slot * 4ull, &k, 4)) return 0;
        if (!v && !k) return 0;
        if (v && k == key) return (uintptr_t)v;
        slot = (slot + ++step) & (h.cap - 1);
    }
    return 0;
}

/// Record = [current][max]. It must hold the game's max (or ours, once written) and the live current.
static bool Matches(const Entry& e, uintptr_t rec, uint32_t want)
{
    uint32_t r[2], m[2];
    return rec && Peek(rec, r, 8) && Peek((uintptr_t)e.mirror, m, 8) && r[0] == m[0] && (r[1] == m[1] || r[1] == want);
}

/// Find the map holding the record: one pass over private read/write memory, copied in 1 MB chunks.
static uintptr_t FindMap(const Entry& e, uint32_t want)
{
    uint32_t key = *e.idPtr;
    ULONGLONG t0 = GetTickCount64();
    auto rs = PrivateRw();
    size_t mb = 0, candidates = 0, nearMisses = 0;
    for (auto& r : rs) mb += (r.hi - r.lo) >> 20;
    Log("[%s] scanning %zu regions (%zu MB) for the map holding key 0x%08X", e.name.c_str(), rs.size(), mb, key);
    const size_t chunk = 1 << 20;
    std::vector<uint8_t> buf(chunk + sizeof(MapHead));
    uintptr_t foundMap = 0, foundRec = 0;
    for (auto& r : rs)
        for (uintptr_t at = r.lo; at < r.hi; at += chunk) {
            size_t n = (size_t)std::min<uintptr_t>(chunk + sizeof(MapHead), r.hi - at);
            if (n < sizeof(MapHead) || !Peek(at, buf.data(), n)) continue;
            for (size_t o = 0; o + sizeof(MapHead) <= n && o < chunk; o += 8) {
                auto& h = *(const MapHead*)(buf.data() + o);
                if (h.cap < 4 || (h.cap & (h.cap - 1)) || !Plausible(h)) continue;
                if (!In(rs, h.vals, h.cap * 8ull) || !In(rs, h.keys, h.cap * 4ull)) continue;
                candidates++;
                uintptr_t rec = Lookup(at + o, key);
                if (!rec) continue;
                if (!Matches(e, rec, want)) {
                    uint32_t r[4] = {}, m[2] = {};
                    Peek(rec, r, 16); Peek((uintptr_t)e.mirror, m, 8);
                    if (nearMisses++ < 10) Log("[%s] map %p has the key -> %p = [%u][%u][%u][%u] (mirror %u/%u)",
                                               e.name.c_str(), (void*)(at + o), (void*)rec, r[0], r[1], r[2], r[3], m[0], m[1]);
                    continue;
                }
                if (foundRec && foundRec != rec) { Log("[%s] two different records match -- not writing", e.name.c_str()); return 0; }
                foundMap = at + o; foundRec = rec;
            }
        }
    Log("[%s] scan took %llu ms, %zu candidate maps: %s", e.name.c_str(), GetTickCount64() - t0, candidates, foundMap ? "found" : "no match");
    if (foundMap) Log("[%s] map %p, record %p", e.name.c_str(), (void*)foundMap, (void*)foundRec);
    return foundMap;
}

static void ApplyRecord(Entry& e)
{
    uint32_t key, m[2];
    if (!Peek((uintptr_t)e.idPtr, &key, 4) || !Peek((uintptr_t)e.mirror, m, 8)) {
        if (!e.misses++) Log("[%s] id/mirror not readable (%p, %p)", e.name.c_str(), e.idPtr, e.mirror);
        return;
    }
    // say what we see whenever it changes, so a silent log is never ambiguous
    uint64_t seen = ((uint64_t)key << 32) ^ m[1] ^ ((uint64_t)m[0] << 16);
    if (seen != e.lastSeen) { e.lastSeen = seen; Log("[%s] id 0x%08X, mirror current %u max %u", e.name.c_str(), key, m[0], m[1]); }
    if (key == 0 || m[1] == 0 || m[1] == 0xFFFFFFFF) return;   // no lair loaded
    uint32_t want = (uint32_t)_strtoi64(e.value.c_str(), nullptr, 0);

    uintptr_t rec = e.map ? Lookup(e.map, key) : 0;
    if (e.map && !Matches(e, rec, want)) {
        if (++e.misses >= 5) { Log("[%s] map %p stopped matching; searching again", e.name.c_str(), (void*)e.map); e.map = 0; }
        return;
    }
    if (!e.map) {
        if (GetTickCount64() < e.nextScan) return;
        e.nextScan = GetTickCount64() + 15000;       // a full scan takes a while; at most every 15 s
        e.map = FindMap(e, want);
        e.misses = 0;
        if (!e.map) return;
        rec = Lookup(e.map, key);
    }
    e.misses = 0;
    uint32_t cur;
    uintptr_t at = rec + e.offset;
    if (!Peek(at, &cur, 4) || cur == want) return;
    SIZE_T wrote = 0;
    if (WriteProcessMemory(GetCurrentProcess(), (void*)at, &want, 4, &wrote) && wrote == 4) Log("[%s] %u -> %u at %p", e.name.c_str(), cur, want, (void*)at);
    else Log("[%s] cannot write at %p", e.name.c_str(), (void*)at);
}

/// The record for the current id in a static table, looked up the way the game does (G2 probing).
static uint8_t* TableRecord(Entry& e)
{
    uint32_t id, count, cap; uint64_t vals, keys;
    if (!Peek((uintptr_t)e.idPtr, &id, 4)) return nullptr;
    if (id == e.altIf && !Peek((uintptr_t)e.altPtr, &id, 4)) return nullptr;
    if (id != e.lastId) { e.lastId = id; Log("[%s] id 0x%08X", e.name.c_str(), id); }
    if (!id || !Peek((uintptr_t)e.countPtr, &count, 4) || !count || !Peek((uintptr_t)e.capPtr, &cap, 4) || !cap || (cap & (cap - 1))
        || !Peek((uintptr_t)e.valsPtr, &vals, 8) || !Peek((uintptr_t)e.keysPtr, &keys, 8) || !vals || !keys) return nullptr;
    uint32_t slot = id & (cap - 1);
    for (uint32_t step = 0; step < cap; ) {
        uint64_t v; uint32_t k;
        if (!Peek(vals + slot * 8ull, &v, 8) || !Peek(keys + slot * 4ull, &k, 4)) return nullptr;
        if (!v && !k) return nullptr;
        if (v && k == id) return (uint8_t*)v;
        slot = (slot + ++step) & (cap - 1);
    }
    return nullptr;
}

// kind = code_bytes: value = hex bytes written once over game code at the match + at (the pattern, matched exactly
// once, pins the bytes replaced). The only entries that change code.
static void ApplyCode(Entry& e)
{
    Pattern v = Parse(e.value.c_str());
    std::vector<uint8_t> b(v.bytes.begin(), v.bytes.end());
    uint8_t* a = e.resolved;
    e.resolved = nullptr;                               // once
    DWORD old;
    if (b.empty() || !VirtualProtect(a, b.size(), PAGE_EXECUTE_READWRITE, &old)) { Log("[%s] cannot write code at %p", e.name.c_str(), a); return; }
    memcpy(a, b.data(), b.size());
    VirtualProtect(a, b.size(), old, &old);
    FlushInstructionCache(GetCurrentProcess(), a, b.size());
    Log("[%s] %zu code bytes written at %p (RVA %llx)", e.name.c_str(), b.size(), a, (unsigned long long)(a - (uint8_t*)GetModuleHandleW(nullptr)));
}

static void Apply(Entry& e)
{
    if (e.kind == "hashmap_record") { if (e.resolved) ApplyRecord(e); return; }
    if (e.kind == "code_bytes") { if (e.resolved) ApplyCode(e); return; }
    uint8_t* a = e.resolved;
    if (!a) return;
    if (e.kind == "table_lookup") {
        a = TableRecord(e);
        if (!a) return;
    }
    else if (e.deref) {
        if (!ReadablePtr(a, sizeof(void*))) return;
        a = *(uint8_t**)a;
        if (!a) return;                                 // object not created yet
    }
    a += e.offset;
    int n = SizeOf(e.type);
    if (!ReadablePtr(a, n)) return;
    const char* v = e.value.c_str();
    if (e.type == "f32") Write<float>(e, a, (float)strtod(v, nullptr));
    else if (e.type == "f64") Write<double>(e, a, strtod(v, nullptr));
    else if (n == 1) Write<uint8_t>(e, a, (uint8_t)_strtoi64(v, nullptr, 0));
    else if (n == 2) Write<uint16_t>(e, a, (uint16_t)_strtoi64(v, nullptr, 0));
    else if (n == 8) Write<uint64_t>(e, a, (uint64_t)_strtoui64(v, nullptr, 0));
    else Write<uint32_t>(e, a, (uint32_t)_strtoi64(v, nullptr, 0));
}

// ------------------------------------------------------------------ file redirects

static decltype(&CreateFileW) g_createW;
static decltype(&CreateFileA) g_createA;

template <class C> static bool EndsWithI(const C* s, const char* suffix)
{
    size_t n = 0, m = strlen(suffix);
    while (s[n]) n++;
    if (n < m) return false;
    for (size_t i = 0; i < m; i++)
        if (tolower((int)s[n - m + i]) != suffix[i]) return false;
    return true;
}

template <class C> static bool ContainsI(const C* s, const char* part)
{
    size_t m = strlen(part);
    for (size_t i = 0; s[i]; i++) {
        size_t k = 0;
        while (k < m && s[i + k] && tolower((int)s[i + k]) == part[k]) k++;
        if (k == m) return true;
    }
    return false;
}

// ------------------------------------------------------------------ island aliases
// [name] kind = island_alias, value = <source lair stem>, stem = <new lair stem>. A new lair that keeps its source's
// lair id gets the source's settings, which name the source's island (Envs\<source>.pc and its sounds/texture table).
// While the lair map last opened is the new lair's, those island files are opened from the new lair's copies instead.
struct Alias { std::string from, stem; uint32_t region; };
static std::vector<Alias> g_alias;
static volatile LONG g_activeAlias = -1;

template <class C> static long FindI(const C* s, const std::string& part)
{
    for (size_t i = 0; s[i]; i++) {
        size_t k = 0;
        while (k < part.size() && s[i + k] && tolower((int)s[i + k]) == tolower((unsigned char)part[k])) k++;
        if (k == part.size()) return (long)i;
    }
    return -1;
}

// The name to open instead (empty: no change). Lair maps (.base) and scenarios switch the active alias.
template <class C> static std::basic_string<C> Aliased(const C* name)
{
    if (!name || g_alias.empty()) return {};
    if (EndsWithI(name, ".base") || EndsWithI(name, ".scenario")) {
        LONG a = -1;
        for (size_t k = 0; k < g_alias.size(); k++)
            if (EndsWithI(name, (g_alias[k].stem + ".base").c_str()) && FindI(name, "\\" + g_alias[k].stem) >= 0) a = (LONG)k;
        InterlockedExchange(&g_activeAlias, a);
        return {};
    }
    LONG a = g_activeAlias;
    if (a < 0 || FindI(name, "envs") < 0) return {};
    const auto& al = g_alias[a];
    long at = FindI(name, "\\" + al.from + ".");
    if (at < 0) return {};
    std::basic_string<C> out(name, name + at + 1);
    for (char ch : al.stem) out.push_back((C)ch);
    out += name + at + 1 + al.from.size();
    // the engine loads "<file>.asrpatch" in place of <file> when there is one (that's how island edits load): answer
    // it with the new lair's own file, so its island is loaded the same way
    if (EndsWithI(out.c_str(), ".asrpatch")) out.resize(out.size() - 9);
    return out;
}

// Just enough zlib inflate (after puff.c) to read the start of a save.
struct Inflate
{
    const uint8_t* in; size_t inLen, limit, pos = 2;   // past the zlib header
    std::vector<uint8_t> out;
    uint32_t bitBuf = 0; int bitCnt = 0; bool bad = false;
    struct Huff { short count[16], symbol[288]; };

    int Bits(int n)
    {
        uint32_t v = bitBuf;
        while (bitCnt < n) { if (pos >= inLen) { bad = true; return 0; } v |= (uint32_t)in[pos++] << bitCnt; bitCnt += 8; }
        bitBuf = v >> n; bitCnt -= n;
        return (int)(v & ((1u << n) - 1));
    }
    int Decode(const Huff& h)
    {
        int code = 0, first = 0, index = 0;
        for (int len = 1; len < 16 && !bad; len++) {
            code |= Bits(1);
            int count = h.count[len];
            if (code - count < first) return h.symbol[index + (code - first)];
            index += count; first = (first + count) << 1; code <<= 1;
        }
        bad = true; return -1;
    }
    static void Build(Huff& h, const short* length, int n)
    {
        memset(h.count, 0, sizeof h.count);
        for (int s = 0; s < n; s++) h.count[length[s]]++;
        short offs[16] = {};
        for (int len = 1; len < 15; len++) offs[len + 1] = offs[len] + h.count[len];
        for (int s = 0; s < n; s++) if (length[s]) h.symbol[offs[length[s]]++] = (short)s;
    }
    void Codes(const Huff& lens, const Huff& dists)
    {
        static const short lbase[29] = { 3,4,5,6,7,8,9,10,11,13,15,17,19,23,27,31,35,43,51,59,67,83,99,115,131,163,195,227,258 };
        static const short lext[29] = { 0,0,0,0,0,0,0,0,1,1,1,1,2,2,2,2,3,3,3,3,4,4,4,4,5,5,5,5,0 };
        static const short dbase[30] = { 1,2,3,4,5,7,9,13,17,25,33,49,65,97,129,193,257,385,513,769,1025,1537,2049,3073,4097,6145,8193,12289,16385,24577 };
        static const short dext[30] = { 0,0,0,0,1,1,2,2,3,3,4,4,5,5,6,6,7,7,8,8,9,9,10,10,11,11,12,12,13,13 };
        for (;;) {
            int sym = Decode(lens);
            if (bad || sym == 256 || out.size() >= limit) return;
            if (sym < 256) { out.push_back((uint8_t)sym); continue; }
            if ((sym -= 257) >= 29) { bad = true; return; }
            int len = lbase[sym] + Bits(lext[sym]);
            int d = Decode(dists);
            if (bad || d >= 30) { bad = true; return; }
            size_t dist = dbase[d] + Bits(dext[d]);
            if (dist > out.size()) { bad = true; return; }
            while (len--) out.push_back(out[out.size() - dist]);
        }
    }
    void Run()
    {
        int last;
        do {
            last = Bits(1);
            int type = Bits(2);
            if (type == 0) {
                bitBuf = 0; bitCnt = 0;
                if (pos + 4 > inLen) { bad = true; return; }
                size_t len = in[pos] | in[pos + 1] << 8; pos += 4;
                if (pos + len > inLen) len = inLen - pos;
                out.insert(out.end(), in + pos, in + pos + len); pos += len;
            }
            else if (type == 1) {
                static Huff lens, dists; static bool made;
                if (!made) {
                    short l[288]; int s = 0;
                    for (; s < 144; s++) l[s] = 8; for (; s < 256; s++) l[s] = 9; for (; s < 280; s++) l[s] = 7; for (; s < 288; s++) l[s] = 8;
                    Build(lens, l, 288);
                    for (s = 0; s < 30; s++) l[s] = 5;
                    Build(dists, l, 30);
                    made = true;
                }
                Codes(lens, dists);
            }
            else if (type == 2) {
                static const uint8_t order[19] = { 16,17,18,0,8,7,9,6,10,5,11,4,12,3,13,2,14,1,15 };
                int nlen = Bits(5) + 257, ndist = Bits(5) + 1, ncode = Bits(4) + 4;
                if (nlen > 286 || ndist > 30) { bad = true; return; }
                short l[320] = {};
                for (int i = 0; i < ncode; i++) l[order[i]] = (short)Bits(3);
                Huff lens, dists;
                Build(lens, l, 19);
                for (int i = 0; i < nlen + ndist && !bad;) {
                    int sym = Decode(lens);
                    if (sym < 16) { l[i++] = (short)sym; continue; }
                    short v = 0; int rep;
                    if (sym == 16) { if (i == 0) { bad = true; return; } v = l[i - 1]; rep = 3 + Bits(2); }
                    else rep = sym == 17 ? 3 + Bits(3) : 11 + Bits(7);
                    if (i + rep > nlen + ndist) { bad = true; return; }
                    while (rep--) l[i++] = v;
                }
                if (bad) return;
                Build(lens, l, nlen);
                Build(dists, l + nlen, ndist);
                Codes(lens, dists);
            }
            else bad = true;
        } while (!last && !bad && out.size() < limit);
    }
};

// Loading a save opens no lair .base, so the save itself says which lair it is: its first chunk (bsnf) holds the lair's
// world-map region, and a new lair's own region switches its island alias on (any other save: off).
template <class C> static void CheckSave(const C* path, DWORD access, DWORD disposition)
{
    if (g_alias.empty() || !path || disposition != OPEN_EXISTING || (access & GENERIC_WRITE) || !EndsWithI(path, ".sav")) return;
    HANDLE h;
    if constexpr (sizeof(C) == 2) h = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr);
    else h = CreateFileA(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr);
    if (h == INVALID_HANDLE_VALUE) return;
    std::vector<uint8_t> buf(256 * 1024);
    DWORD got = 0;
    ReadFile(h, buf.data(), (DWORD)buf.size(), &got, nullptr);
    CloseHandle(h);
    if (got < 26 || memcmp(buf.data(), "Asura   AsuraZlb", 16) != 0) return;   // slot0 is the profile, not a game
    Inflate z{ buf.data() + 24, got - 24, 64 * 1024 };
    z.Run();
    const auto& o = z.out;
    LONG a = -1;
    if (o.size() >= 16 && memcmp(&o[8], "bsnf", 4) == 0) {
        uint32_t size; memcpy(&size, &o[12], 4);
        size_t end = 8 + (size_t)size < o.size() ? 8 + (size_t)size : o.size();
        for (size_t j = 16; j + 4 <= end && a < 0; j++) {
            uint32_t v; memcpy(&v, &o[j], 4);
            for (size_t k = 0; k < g_alias.size(); k++) if (g_alias[k].region && v == g_alias[k].region) a = (LONG)k;
        }
    }
    InterlockedExchange(&g_activeAlias, a);
    std::string n;
    for (size_t i = 0; path[i]; i++) n.push_back((char)path[i]);
    Log("save: %s (%zu bytes read) -> %s", n.c_str(), o.size(), a >= 0 ? g_alias[a].stem.c_str() : "not a new lair");
}

// ------------------------------------------------------------------ file blocks
// [name] kind = file_block, value = <game file, e.g. misc\common.asr>, offset = <byte offset>, source = <file in
// bin\eg2modkit_blocks>. Reads of that file (ReadFile / ReadFileEx) get those bytes in place of the file's own: a changed
// compressed block goes in without a .asrpatch of the file (a common.asr.asrpatch loses the menu art).
struct FileBlock { std::string file; uint64_t offset = 0; std::vector<uint8_t> data; };
static std::vector<FileBlock> g_blocks;
static SRWLOCK g_blockLock = SRWLOCK_INIT;
struct BlockHandle { std::string file; DWORD volume, hi, lo; };
static std::map<HANDLE, BlockHandle> g_blockHandles;   // open handles of those files (with the file's identity: a handle value can be reused)
struct PendingRead { LPOVERLAPPED_COMPLETION_ROUTINE done; uint8_t* buf; uint64_t at; std::string file; };
static std::map<LPOVERLAPPED, PendingRead> g_pending;   // ReadFileEx calls on them, until their completion routine
static volatile LONG g_blockReads = 0;
static decltype(&ReadFile) g_readFile;
static decltype(&ReadFileEx) g_readFileEx;
static decltype(&CloseHandle) g_closeHandle;
static decltype(&CreateFileMappingW) g_mapW;
static decltype(&CreateFileMappingA) g_mapA;

template <class C> static HANDLE TrackBlocks(const C* name, HANDLE h)
{
    if (g_blocks.empty() || h == INVALID_HANDLE_VALUE || !name) return h;
    std::string n;
    for (size_t i = 0; name[i]; i++) n.push_back(name[i] == '/' ? '\\' : (char)tolower((int)name[i]));
    for (auto& b : g_blocks)
        if (n.size() >= b.file.size() && n.compare(n.size() - b.file.size(), b.file.size(), b.file) == 0) {
            BY_HANDLE_FILE_INFORMATION fi;
            if (!GetFileInformationByHandle(h, &fi)) break;
            AcquireSRWLockExclusive(&g_blockLock);
            g_blockHandles[h] = { b.file, fi.dwVolumeSerialNumber, fi.nFileIndexHigh, fi.nFileIndexLow };
            ReleaseSRWLockExclusive(&g_blockLock);
            Log("block: %s opened (handle %p)", n.c_str(), h);
            break;
        }
    return h;
}

static std::string BlockFile(HANDLE h)
{
    if (g_blocks.empty()) return {};
    AcquireSRWLockShared(&g_blockLock);
    auto it = g_blockHandles.find(h);
    BlockHandle b = it == g_blockHandles.end() ? BlockHandle{} : it->second;
    ReleaseSRWLockShared(&g_blockLock);
    if (b.file.empty()) return {};
    BY_HANDLE_FILE_INFORMATION fi;
    DWORD err = GetLastError();
    bool same = GetFileInformationByHandle(h, &fi) && fi.dwVolumeSerialNumber == b.volume && fi.nFileIndexHigh == b.hi && fi.nFileIndexLow == b.lo;
    SetLastError(err);
    if (same) return b.file;
    AcquireSRWLockExclusive(&g_blockLock);   // closed elsewhere and the value reused for another file
    g_blockHandles.erase(h);
    ReleaseSRWLockExclusive(&g_blockLock);
    return {};
}

static void ApplyBlocks(const std::string& file, uint64_t at, uint8_t* buf, uint64_t n, const char* how)
{
    DWORD err = GetLastError();
    if (InterlockedIncrement(&g_blockReads) <= 40) Log("block: %s %s at %llu, %llu bytes", file.c_str(), how, at, n);
    for (auto& b : g_blocks) {
        if (b.file != file) continue;
        uint64_t lo = (std::max)(at, b.offset), hi = (std::min)(at + n, b.offset + b.data.size());
        if (lo >= hi) continue;
        memcpy(buf + (lo - at), b.data.data() + (lo - b.offset), (size_t)(hi - lo));
        Log("block: %s bytes %llu..%llu swapped in", file.c_str(), lo, hi);
    }
    SetLastError(err);
}

static BOOL WINAPI HookReadFile(HANDLE h, LPVOID buf, DWORD n, LPDWORD got, LPOVERLAPPED ov)
{
    std::string file = BlockFile(h);
    if (file.empty()) return g_readFile(h, buf, n, got, ov);
    uint64_t at = 0;
    if (ov) at = ov->Offset | (uint64_t)ov->OffsetHigh << 32;
    else { LARGE_INTEGER zero{}, pos{}; SetFilePointerEx(h, zero, &pos, FILE_CURRENT); at = (uint64_t)pos.QuadPart; }
    BOOL ok = g_readFile(h, buf, n, got, ov);
    if (!ok) {
        if (GetLastError() == ERROR_IO_PENDING) { DWORD e = GetLastError(); Log("block: %s async ReadFile at %llu (%u bytes): NOT swapped", file.c_str(), at, n); SetLastError(e); }
        return ok;
    }
    ApplyBlocks(file, at, (uint8_t*)buf, got ? *got : ov ? (uint64_t)ov->InternalHigh : n, "ReadFile");
    return ok;
}

static VOID CALLBACK BlockReadDone(DWORD err, DWORD n, LPOVERLAPPED ov)
{
    PendingRead p{};
    AcquireSRWLockExclusive(&g_blockLock);
    auto it = g_pending.find(ov);
    bool found = it != g_pending.end();
    if (found) { p = it->second; g_pending.erase(it); }
    ReleaseSRWLockExclusive(&g_blockLock);
    if (!found) return;
    if (err == 0) ApplyBlocks(p.file, p.at, p.buf, n, "ReadFileEx");
    p.done(err, n, ov);
}

static BOOL WINAPI HookReadFileEx(HANDLE h, LPVOID buf, DWORD n, LPOVERLAPPED ov, LPOVERLAPPED_COMPLETION_ROUTINE done)
{
    std::string file = BlockFile(h);
    if (file.empty() || !ov || !done) return g_readFileEx(h, buf, n, ov, done);
    AcquireSRWLockExclusive(&g_blockLock);
    g_pending[ov] = { done, (uint8_t*)buf, ov->Offset | (uint64_t)ov->OffsetHigh << 32, file };
    ReleaseSRWLockExclusive(&g_blockLock);
    BOOL ok = g_readFileEx(h, buf, n, ov, BlockReadDone);
    if (!ok) { DWORD e = GetLastError(); AcquireSRWLockExclusive(&g_blockLock); g_pending.erase(ov); ReleaseSRWLockExclusive(&g_blockLock); SetLastError(e); }
    return ok;
}

// The game memory-maps misc\common.asr. A mapping of a tracked file is made copy-on-write, and so is every view of it;
// the block bytes are then written into the view (private pages, the file itself never changes) and the pages set back
// to read-only.
struct BlockMap { std::string file; uint64_t size; };
static std::map<HANDLE, BlockMap> g_blockMaps;
static decltype(&MapViewOfFile) g_mapView;

static BlockMap MapOf(HANDLE m)
{
    if (g_blocks.empty() || !m) return {};
    AcquireSRWLockShared(&g_blockLock);
    auto it = g_blockMaps.find(m);
    BlockMap b = it == g_blockMaps.end() ? BlockMap{} : it->second;
    ReleaseSRWLockShared(&g_blockLock);
    return b;
}

static BOOL WINAPI HookCloseHandle(HANDLE h)
{
    if (!g_blocks.empty()) {
        AcquireSRWLockExclusive(&g_blockLock);
        g_blockHandles.erase(h);
        g_blockMaps.erase(h);
        ReleaseSRWLockExclusive(&g_blockLock);
    }
    return g_closeHandle(h);
}

template <class F, class C> static HANDLE BlockMapping(F real, HANDLE h, LPSECURITY_ATTRIBUTES sa, DWORD protect, DWORD hi, DWORD lo, const C* name)
{
    std::string file = BlockFile(h);
    if (file.empty()) return real(h, sa, protect, hi, lo, name);
    DWORD base = protect & 0xff;
    if (base == PAGE_READONLY || base == PAGE_EXECUTE_READ) protect = (protect & ~0xffu) | PAGE_WRITECOPY;
    HANDLE m = real(h, sa, protect, hi, lo, name);
    if (m) {
        LARGE_INTEGER size{};
        GetFileSizeEx(h, &size);
        AcquireSRWLockExclusive(&g_blockLock);
        g_blockMaps[m] = { file, (uint64_t)size.QuadPart };
        ReleaseSRWLockExclusive(&g_blockLock);
        Log("block: %s memory-mapped (copy-on-write)", file.c_str());
    }
    else Log("block: %s mapping failed (%lu)", file.c_str(), GetLastError());
    return m;
}
static HANDLE WINAPI HookMapW(HANDLE h, LPSECURITY_ATTRIBUTES sa, DWORD protect, DWORD hi, DWORD lo, LPCWSTR name) { return BlockMapping(g_mapW, h, sa, protect, hi, lo, name); }
static HANDLE WINAPI HookMapA(HANDLE h, LPSECURITY_ATTRIBUTES sa, DWORD protect, DWORD hi, DWORD lo, LPCSTR name) { return BlockMapping(g_mapA, h, sa, protect, hi, lo, name); }

static LPVOID WINAPI HookMapView(HANDLE m, DWORD access, DWORD hi, DWORD lo, SIZE_T n)
{
    BlockMap bm = MapOf(m);
    if (bm.file.empty()) return g_mapView(m, access, hi, lo, n);
    if (!(access & FILE_MAP_WRITE)) access = FILE_MAP_COPY;
    auto view = (uint8_t*)g_mapView(m, access, hi, lo, n);
    if (!view) { Log("block: %s view failed (%lu)", bm.file.c_str(), GetLastError()); return view; }
    DWORD err = GetLastError();
    uint64_t at = (uint64_t)hi << 32 | lo, len = n ? n : bm.size - at;
    for (auto& b : g_blocks) {
        if (b.file != bm.file) continue;
        uint64_t s = (std::max)(at, b.offset), e = (std::min)(at + len, b.offset + b.data.size());
        if (s >= e) continue;
        DWORD old;
        if (!VirtualProtect(view + (s - at), (SIZE_T)(e - s), PAGE_WRITECOPY, &old)) { Log("block: %s view not writable (%lu)", bm.file.c_str(), GetLastError()); continue; }
        memcpy(view + (s - at), b.data.data() + (s - b.offset), (size_t)(e - s));
        VirtualProtect(view + (s - at), (SIZE_T)(e - s), PAGE_READONLY, &old);
        Log("block: %s bytes %llu..%llu swapped into the mapped view", bm.file.c_str(), s, e);
    }
    SetLastError(err);
    return view;
}

template <class C> static bool WantsPatch(const C* name, DWORD disposition)
{
    return name && disposition == OPEN_EXISTING && (EndsWithI(name, ".base") || EndsWithI(name, ".scenario") || EndsWithI(name, ".webm")
                                                    || (EndsWithI(name, ".pc") && ContainsI(name, "envs")));   // island scenery
}

// A 0-byte .asrpatch means "this file is gone" (e.g. a skipped intro video): the open fails as if it didn't exist.
static HANDLE Missing() { SetLastError(ERROR_FILE_NOT_FOUND); return INVALID_HANDLE_VALUE; }
static bool Empty(const WIN32_FILE_ATTRIBUTE_DATA& a) { return a.nFileSizeHigh == 0 && a.nFileSizeLow == 0; }

static HANDLE WINAPI HookCreateFileW(LPCWSTR name, DWORD access, DWORD share, LPSECURITY_ATTRIBUTES sa, DWORD disposition, DWORD flags, HANDLE tmpl)
{
    CheckSave(name, access, disposition);
    auto alias = Aliased(name);
    if (!alias.empty()) { Log("island: %ls -> %ls", name, alias.c_str()); name = alias.c_str(); }
    if (name && ContainsI(name, "envs") && (EndsWithI(name, ".ts") || EndsWithI(name, ".ts.asrpatch") || EndsWithI(name, ".sounds"))) Log("open: %ls", name);   // the island's texture table and sounds
    if (WantsPatch(name, disposition)) {
        std::wstring patch = std::wstring(name) + L".asrpatch";
        WIN32_FILE_ATTRIBUTE_DATA a;
        if (EndsWithI(name, ".webm")) Log("video: %ls", name);
        if (GetFileAttributesExW(patch.c_str(), GetFileExInfoStandard, &a)) {
            if (Empty(a)) { Log("skip: %ls", name); return Missing(); }
            Log("map: %ls -> %ls", name, patch.c_str());
            return g_createW(patch.c_str(), access, share, sa, disposition, flags, tmpl);
        }
        if (!EndsWithI(name, ".webm")) Log("open: %ls", name);   // which lair/island the game actually loads
    }
    return TrackBlocks(name, g_createW(name, access, share, sa, disposition, flags, tmpl));
}

static HANDLE WINAPI HookCreateFileA(LPCSTR name, DWORD access, DWORD share, LPSECURITY_ATTRIBUTES sa, DWORD disposition, DWORD flags, HANDLE tmpl)
{
    CheckSave(name, access, disposition);
    auto alias = Aliased(name);
    if (!alias.empty()) { Log("island: %s -> %s", name, alias.c_str()); name = alias.c_str(); }
    if (name && ContainsI(name, "envs") && (EndsWithI(name, ".ts") || EndsWithI(name, ".ts.asrpatch") || EndsWithI(name, ".sounds"))) Log("open: %s", name);   // the island's texture table and sounds
    if (WantsPatch(name, disposition)) {
        std::string patch = std::string(name) + ".asrpatch";
        WIN32_FILE_ATTRIBUTE_DATA a;
        if (EndsWithI(name, ".webm")) Log("video: %s", name);
        if (GetFileAttributesExA(patch.c_str(), GetFileExInfoStandard, &a)) {
            if (Empty(a)) { Log("skip: %s", name); return Missing(); }
            Log("map: %s -> %s", name, patch.c_str());
            return g_createA(patch.c_str(), access, share, sa, disposition, flags, tmpl);
        }
        if (!EndsWithI(name, ".webm")) Log("open: %s", name);   // which lair/island the game actually loads
    }
    return TrackBlocks(name, g_createA(name, access, share, sa, disposition, flags, tmpl));
}

static decltype(&GetFileAttributesExW) g_attrExW;
static decltype(&GetFileAttributesW) g_attrW;

static BOOL WINAPI HookGetFileAttributesExW(LPCWSTR name, GET_FILEEX_INFO_LEVELS level, LPVOID info)
{
    auto alias = Aliased(name);
    return g_attrExW(alias.empty() ? name : alias.c_str(), level, info);
}

static DWORD WINAPI HookGetFileAttributesW(LPCWSTR name)
{
    auto alias = Aliased(name);
    return g_attrW(alias.empty() ? name : alias.c_str());
}

// Videos are opened with the C runtime (fopen_s("fmv/%s.webm")), which never goes through the exe's CreateFile slots.
typedef errno_t(__cdecl* FopenS)(FILE**, const char*, const char*);
static FopenS g_fopenS;

static errno_t __cdecl HookFopenS(FILE** f, const char* name, const char* mode)
{
    if (name && EndsWithI(name, ".webm")) {
        Log("video: %s", name);
        WIN32_FILE_ATTRIBUTE_DATA a;
        std::string patch = std::string(name) + ".asrpatch";
        if (GetFileAttributesExA(patch.c_str(), GetFileExInfoStandard, &a)) {
            if (Empty(a)) { Log("skip: %s", name); if (f) *f = nullptr; return ENOENT; }
            Log("video: %s -> %s", name, patch.c_str());
            return g_fopenS(f, patch.c_str(), mode);
        }
    }
    return g_fopenS(f, name, mode);
}

// Swap the exe's import slots for the file functions (kernel32 CreateFileA/W, CRT fopen_s; the attribute checks with
// island aliases; the reads, closes and mappings with file blocks) once the protector has filled them in. True when every
// wanted one the exe imports is done.
static bool HookImports()
{
    struct Want { bool crt; const char* fn; void* hook; void** orig; bool on; };
    Want wants[] = {
        { false, "CreateFileW", (void*)HookCreateFileW, (void**)&g_createW, true },
        { false, "CreateFileA", (void*)HookCreateFileA, (void**)&g_createA, true },
        { true, "fopen_s", (void*)HookFopenS, (void**)&g_fopenS, true },
        // existence checks only matter for island aliases (a lair's island asked for under another lair's name)
        { false, "GetFileAttributesExW", (void*)HookGetFileAttributesExW, (void**)&g_attrExW, !g_alias.empty() },
        { false, "GetFileAttributesW", (void*)HookGetFileAttributesW, (void**)&g_attrW, !g_alias.empty() },
        { false, "ReadFile", (void*)HookReadFile, (void**)&g_readFile, !g_blocks.empty() },
        { false, "ReadFileEx", (void*)HookReadFileEx, (void**)&g_readFileEx, !g_blocks.empty() },
        { false, "CloseHandle", (void*)HookCloseHandle, (void**)&g_closeHandle, !g_blocks.empty() },
        { false, "CreateFileMappingW", (void*)HookMapW, (void**)&g_mapW, !g_blocks.empty() },
        { false, "CreateFileMappingA", (void*)HookMapA, (void**)&g_mapA, !g_blocks.empty() },
        { false, "MapViewOfFile", (void*)HookMapView, (void**)&g_mapView, !g_blocks.empty() },
    };
    auto base = (uint8_t*)GetModuleHandleW(nullptr);
    HMODULE k32 = GetModuleHandleW(L"kernel32.dll"), kb = GetModuleHandleW(L"kernelbase.dll");
    if (!ReadablePtr(base, 0x400)) return false;
    auto nt = (IMAGE_NT_HEADERS64*)(base + ((IMAGE_DOS_HEADER*)base)->e_lfanew);
    if (!ReadablePtr(nt, sizeof *nt)) return false;
    auto dir = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (!dir.VirtualAddress) return false;
    int found = 0, done = 0;
    for (auto d = (IMAGE_IMPORT_DESCRIPTOR*)(base + dir.VirtualAddress); ReadablePtr(d, sizeof *d) && d->Name; d++) {
        const char* dll = (char*)(base + d->Name);
        bool k = _stricmp(dll, "kernel32.dll") == 0, crt = _stricmp(dll, "api-ms-win-crt-stdio-l1-1-0.dll") == 0;
        if ((!k && !crt) || !d->OriginalFirstThunk) continue;
        HMODULE crtMod = crt ? GetModuleHandleA("ucrtbase.dll") : nullptr;
        auto names = (IMAGE_THUNK_DATA64*)(base + d->OriginalFirstThunk);
        auto slots = (IMAGE_THUNK_DATA64*)(base + d->FirstThunk);
        for (int i = 0; names[i].u1.AddressOfData; i++) {
            if (IMAGE_SNAP_BY_ORDINAL64(names[i].u1.Ordinal)) continue;
            const char* fn = ((IMAGE_IMPORT_BY_NAME*)(base + names[i].u1.AddressOfData))->Name;
            Want* w = nullptr;
            for (auto& x : wants) if (x.on && x.crt == crt && strcmp(fn, x.fn) == 0) w = &x;
            if (!w) continue;
            found++;
            void* cur = (void*)slots[i].u1.Function;
            if (cur == w->hook) { done++; continue; }
            // only once it holds the real function (kernel32's / kernelbase's / ucrtbase's), not the protector's placeholder
            if (crt ? !(crtMod && cur == (void*)GetProcAddress(crtMod, fn))
                    : cur != (void*)GetProcAddress(k32, fn) && !(kb && cur == (void*)GetProcAddress(kb, fn))) continue;
            *w->orig = cur;
            DWORD old;
            if (!VirtualProtect(&slots[i].u1.Function, 8, PAGE_READWRITE, &old)) continue;
            slots[i].u1.Function = (ULONGLONG)w->hook;
            VirtualProtect(&slots[i].u1.Function, 8, old, &old);
            Log("map redirect: hooked %s", fn);
            done++;
        }
    }
    return found >= 3 && done == found;
}

static DWORD WINAPI MapWorker(void*)
{
    for (int i = 0; i < 1200; i++) { if (HookImports()) return 0; Sleep(100); }   // up to 2 minutes
    Log("map redirect: not every import was found (see the \"hooked\" lines above); patched maps or skipped videos may not work");
    return 0;
}

static DWORD WINAPI TraceWorker(void* p);

static DWORD WINAPI Worker(void*)
{
    g_log = _wfopen((std::wstring(g_dir) + L"\\eg2modkit.log").c_str(), L"w");
    auto entries = LoadConfig();
    Log("eg2modkit runtime tweaks: %zu entries in eg2modkit.cfg", entries.size());
    for (auto& e : entries) Log("  [%s] %s = %s", e.name.c_str(), e.type.c_str(), e.value.c_str());
    // island aliases and diagnostics aren't patches (aliases are read before the file hooks go in)
    for (auto it = entries.begin(); it != entries.end();)
        if (it->kind == "island_alias") {
            g_alias.push_back({ it->value, it->stem, it->region });
            Log("[%s] island %s -> %s while that lair is loaded", it->name.c_str(), it->value.c_str(), it->stem.c_str());
            it = entries.erase(it);
        }
        else if (it->kind == "file_block") {
            FileBlock b;
            for (char c : it->value) b.file.push_back(c == '/' ? '\\' : (char)tolower((unsigned char)c));
            b.offset = (uint64_t)it->offset;
            if (FILE* f = _wfopen((std::wstring(g_dir) + L"\\eg2modkit_blocks\\" + std::wstring(it->source.begin(), it->source.end())).c_str(), L"rb")) {
                char buf[65536];
                size_t n;
                while ((n = fread(buf, 1, sizeof buf, f)) > 0) b.data.insert(b.data.end(), buf, buf + n);
                fclose(f);
            }
            if (b.data.empty()) Log("[%s] block file %s missing: %s stays as it is", it->name.c_str(), it->source.c_str(), it->value.c_str());
            else {
                Log("[%s] %s bytes %llu..%llu from %s as the game reads them", it->name.c_str(), b.file.c_str(), b.offset, b.offset + b.data.size(), it->source.c_str());
                g_blocks.push_back(std::move(b));
            }
            it = entries.erase(it);
        }
        else if (it->kind == "trace_lair") {
            if (HANDLE h = CreateThread(nullptr, 0, TraceWorker, new Entry(*it), 0, nullptr)) CloseHandle(h);
            it = entries.erase(it);
        }
        else ++it;
    if (HANDLE h = CreateThread(nullptr, 0, MapWorker, nullptr, 0, nullptr)) CloseHandle(h);
    if (entries.empty()) return 0;

    // the exe is wrapped by a protector; keep looking until its code is readable (up to 2 min)
    std::map<std::string, std::vector<uint8_t*>> cache;
    for (int i = 0; i < 240; i++) {
        bool done = true;
        for (auto& e : entries) done &= Resolve(e, cache);
        if (done) break;
        Sleep(500);
    }
    for (auto& e : entries)
        if (!e.resolved && !e.failed) Log("[%s] pattern never found (game version changed?)", e.name.c_str());

    // re-apply periodically: some values are (re)initialised when a lair loads
    for (;;) {
        for (auto& e : entries) Apply(e);
        Sleep(1000);
    }
}

// ------------------------------------------------------------------ lair trace (diagnostics)
// [name] kind = trace_lair, value = <lair file stem>, at = RVA of the lair list pointer (count at +0xC).
// Finds that lair's record in the game's list (0x1E8 bytes: +0x20 name hash, +0x28 file name) and puts CPU data
// watchpoints (debug registers, every thread) on those two fields. Each distinct piece of code that reads or writes
// them is logged once with its return addresses (RVAs), then counted: which game code handles the lair, and the last
// code to touch it before the game gives up. Changes nothing in the game.

#include <tlhelp32.h>
#include <set>

struct TraceHit { uintptr_t rip, frames[6]; int field; volatile LONG count; };
static TraceHit g_hits[256];
static volatile LONG g_hitCount, g_hitLock, g_hitDropped;
static uintptr_t g_imgLo, g_imgHi;

static bool InImage(uintptr_t a) { return a >= g_imgLo && a < g_imgHi; }

static LONG CALLBACK TraceVeh(EXCEPTION_POINTERS* x)
{
    if (x->ExceptionRecord->ExceptionCode != EXCEPTION_SINGLE_STEP || (x->ContextRecord->Dr6 & 15) == 0) return EXCEPTION_CONTINUE_SEARCH;
    int which = (x->ContextRecord->Dr6 & 1) ? 0 : (x->ContextRecord->Dr6 & 2) ? 1 : (x->ContextRecord->Dr6 & 4) ? 2 : 3;
    auto* c = x->ContextRecord;
    c->Dr6 = 0;
    TraceHit h{};
    h.rip = c->Rip;
    h.field = which;
    // return addresses on the stack: in the exe and right after a call instruction
    int n = 0;
    auto* sp = (uintptr_t*)c->Rsp;
    for (int i = 0; i < 96 && n < 6; i++) {
        uintptr_t v;
        if (!ReadablePtr(sp + i, sizeof v)) break;
        v = sp[i];
        if (!InImage(v) || !ReadablePtr((void*)(v - 6), 6)) continue;
        auto* b = (const uint8_t*)v;
        if (b[-5] == 0xE8 || b[-6] == 0xFF || b[-2] == 0xFF || b[-3] == 0xFF) h.frames[n++] = v;
    }
    while (InterlockedCompareExchange(&g_hitLock, 1, 0) != 0) YieldProcessor();
    LONG k = 0;
    for (; k < g_hitCount; k++)
        if (g_hits[k].rip == h.rip && g_hits[k].field == h.field && g_hits[k].frames[0] == h.frames[0] && g_hits[k].frames[1] == h.frames[1]) break;
    if (k < g_hitCount) InterlockedIncrement(&g_hits[k].count);
    else if (k < 256) { h.count = 1; g_hits[k] = h; g_hitCount = k + 1; }
    else InterlockedIncrement(&g_hitDropped);
    InterlockedExchange(&g_hitLock, 0);
    return EXCEPTION_CONTINUE_EXECUTION;
}

// Debug registers on every thread of the game (new threads on later passes), break on read or write:
// DR0 = 4 bytes at rec+0x20 (name hash), DR1 = 8 at +0x28 (file name), DR2 = 4 at +0x44 (lair id), DR3 = 8 at +0x48
// (the parsed header that follows).
static const int kField[4] = { 0x20, 0x28, 0x44, 0x48 };
static void Arm(uintptr_t rec, std::set<DWORD>& armed, bool all)
{
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
    if (snap == INVALID_HANDLE_VALUE) return;
    THREADENTRY32 te{ sizeof te };
    DWORD me = GetCurrentThreadId(), pid = GetCurrentProcessId();
    for (BOOL ok = Thread32First(snap, &te); ok; ok = Thread32Next(snap, &te)) {
        if (te.th32OwnerProcessID != pid || te.th32ThreadID == me || (!all && armed.count(te.th32ThreadID))) continue;
        HANDLE t = OpenThread(THREAD_GET_CONTEXT | THREAD_SET_CONTEXT | THREAD_SUSPEND_RESUME, FALSE, te.th32ThreadID);
        if (!t) continue;
        if (SuspendThread(t) != (DWORD)-1) {
            CONTEXT c{}; c.ContextFlags = CONTEXT_DEBUG_REGISTERS;
            if (GetThreadContext(t, &c)) {
                c.Dr0 = rec + kField[0]; c.Dr1 = rec + kField[1]; c.Dr2 = rec + kField[2]; c.Dr3 = rec + kField[3];
                // L0-L3 on; RW = 11 (read/write) for all; LEN0 = 4 bytes (11), LEN1 = 8 (10), LEN2 = 4 (11), LEN3 = 8 (10)
                c.Dr7 = (c.Dr7 & ~0xFFFF00FFull) | 0x55 | (3ull << 16) | (3ull << 18) | (3ull << 20) | (2ull << 22)
                        | (3ull << 24) | (3ull << 26) | (3ull << 28) | (2ull << 30);
                if (SetThreadContext(t, &c)) armed.insert(te.th32ThreadID);
            }
            ResumeThread(t);
        }
        CloseHandle(t);
    }
    CloseHandle(snap);
}

static DWORD WINAPI TraceWorker(void* p)
{
    auto* e = (Entry*)p;
    auto base = (uint8_t*)GetModuleHandleW(nullptr);
    auto* nt = (IMAGE_NT_HEADERS*)(base + ((IMAGE_DOS_HEADER*)base)->e_lfanew);
    g_imgLo = (uintptr_t)base; g_imgHi = g_imgLo + nt->OptionalHeader.SizeOfImage;
    auto** listAt = (uintptr_t**)(base + e->at);
    auto* countAt = (uint32_t*)(base + e->at + 0xC);
    uintptr_t rec = 0;
    for (int i = 0; i < 600 && !rec; i++, Sleep(500)) {   // up to 5 minutes: the list fills at startup
        uintptr_t* list; uint32_t count;
        if (!ReadablePtr(listAt, 8) || !ReadablePtr(countAt, 4)) continue;
        list = *listAt; count = *countAt;
        if (!list || count > 4096 || !ReadablePtr(list, count * 8ull)) continue;
        for (uint32_t k = 0; k < count && !rec; k++) {
            uintptr_t d = list[k];
            if (!d || !ReadablePtr((void*)d, 0x30)) continue;
            auto* name = *(const char**)(d + 0x28);
            if (name && ReadablePtr(name, 1) && ContainsI(name, e->value.c_str())) {
                rec = d;
                Log("[%s] lair record %p (list index %u of %u): %s, hash %08x", e->name.c_str(), (void*)d, k, count, name, *(uint32_t*)(d + 0x20));
            }
        }
    }
    if (!rec) { Log("[%s] no lair record naming %s in the list", e->name.c_str(), e->value.c_str()); return 0; }
    AddVectoredExceptionHandler(1, TraceVeh);
    std::set<DWORD> armed;
    std::vector<LONG> shown(256, 0);
    for (int pass = 0;; pass++) {
        Arm(rec, armed, pass % 20 == 0);   // new threads each second, all threads every 20 s
        LONG n = g_hitCount;
        for (LONG k = 0; k < n; k++) {
            LONG c = g_hits[k].count;
            if (c == shown[k]) continue;
            if (shown[k] == 0) {
                char f[160] = ""; size_t o = 0;
                for (int j = 0; j < 6 && g_hits[k].frames[j]; j++)
                    o += snprintf(f + o, sizeof f - o, " %llx", (unsigned long long)(g_hits[k].frames[j] - g_imgLo));
                Log("[%s] +%x touched by code at %llx, called from%s", e->name.c_str(), kField[g_hits[k].field], (unsigned long long)(g_hits[k].rip - g_imgLo), f);
            }
            else if (c - shown[k] >= 1) Log("[%s] code at %llx: %ld more times", e->name.c_str(), (unsigned long long)(g_hits[k].rip - g_imgLo), c - shown[k]);
            shown[k] = c;
        }
        if (g_hitDropped) { Log("[%s] %ld hits from more code places than the log keeps", e->name.c_str(), (long)g_hitDropped); g_hitDropped = 0; }
        Sleep(1000);
    }
}

// ------------------------------------------------------------------ entry

BOOL WINAPI DllMain(HINSTANCE self, DWORD reason, void*)
{
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    DisableThreadLibraryCalls(self);

    wchar_t sys[MAX_PATH];
    GetSystemDirectoryW(sys, MAX_PATH);
    HMODULE real = LoadLibraryW((std::wstring(sys) + L"\\xinput1_4.dll").c_str());
    if (!real) return FALSE;   // kept loaded for the process lifetime
    static const char* names[] = { "XInputGetState", "XInputSetState", "XInputGetCapabilities",
        "XInputEnable", "XInputGetBatteryInformation", "XInputGetKeystroke", "XInputGetAudioDeviceIds" };
    for (int i = 0; i < 7; i++) g_real[i] = (void*)GetProcAddress(real, names[i]);
    static const WORD ords[] = { 100, 101, 102, 103, 104, 108, 109 };   // XInputGetStateEx & friends (NONAME)
    for (int i = 0; i < 7; i++) g_real[7 + i] = (void*)GetProcAddress(real, MAKEINTRESOURCEA(ords[i]));

    GetModuleFileNameW(self, g_dir, MAX_PATH);
    if (wchar_t* slash = wcsrchr(g_dir, L'\\')) *slash = 0;
    if (HANDLE h = CreateThread(nullptr, 0, Worker, nullptr, 0, nullptr)) CloseHandle(h);
    return TRUE;
}
