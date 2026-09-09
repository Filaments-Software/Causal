# S&Box Project-Level Perf Playbook (digest of dossier/00,10-16,20,90,91)

Audience: agents writing **addon / game code** against S&Box engine (`Sandbox.*`, `net10.0`, RyuJIT x64 + Arm64). Not engine patches.
Follow these by default; prefer simple readable code that fits S&Box patterns. No bench harness in this project - verify by opening the scene and playing it.

## 0. Golden rules

1. **Linear passes over dense arrays win.** `Component` pointer-chase per `Scene.Tick` is the #1 killer. Batch in a `GameObjectSystem<T>` over `Span`/array, not per-entity `virtual Update()`.
2. **Default to BCL vector, avoid hand-rolled intrinsics.** `TensorPrimitives` / `Vector<T>` / `Array.Sort` / `FrozenDictionary` first. No manual `Avx2/Neon` in game code.
3. **Never per-entity expensive op:** `virtual/interface` dispatch, `TypeLibrary`/`Reflection`, `P/Invoke`/`Interop`, `new byte[]/string`, `Dictionary<string>`, `%`/`/` in tight loop, `try/throw`, `WriteLine`/sync IO.
4. **Prefer dense, predictable code.** Contiguous arrays, biased branches, batched native calls.
5. **Gate every change:** correct → no extra per-frame alloc → keep readable version if tied.

## 1. Memory / layout (dossier 10)

**Do:**
- Group co-accessed fields. Per-pass choice: AoS `struct Body { Vector3 Pos, Vel; float Mass; }[]` for integrate; SoA `float[] x,y,z` for SIMD sweeps.
- Pack structs: largest→smallest, `[StructLayout(Sequential, Pack=4/8)]`, assert `Unsafe.SizeOf<T>()`. 64B pad only for cross-thread queues (false sharing).
- Reuse: `ArrayPool<T>.Shared.Rent/Return`, `SharedArrayPool`, `stackalloc` small temps, `Span<T>` slices. No `new` in tick.
- Chunk IO: `FileStream(buffer:1<<20, useAsync:true)` + `Pipelines`, `ArrayPool` tiles. Never 1B reads, never sync load in tick.

**Don't:** `class[]` graphs for hot data, `Pack=1` on SIMD structs, pow2 grid/bucket sizes (stride 256 = 10× cliff - pad +1/+17), `mmap`/huge-page tricks from managed.

```csharp
// GOOD: dense, pooled, flat
var pool = ArrayPool<Vector3>.Shared;
var buf = pool.Rent(count);
try {
  var span = buf.AsSpan(0, count);
  for (int i = 0; i < span.Length; i++) span[i] += vel[i] * dt;
} finally { pool.Return(buf); }
// BAD: per-entity alloc + virtual hop
foreach (var e in GameObjects) e.Update(); // + new Vector3() inside
```

## 2. Loops / branches / SIMD (dossier 11,12)

**Do:**
- Make hot branch biased (>90%) or remove it. Sort/partition first (`sort by dist` before `if(dist<lod)`), or branchless `Math.Min/Max/Clamp`, `Vector.ConditionalSelect`, `mask & value`.
- Cold out-of-line: `if(!enabled) return;` hot fall-through, `destroy/migrate/throw` in `[MethodImpl(NoInlining)]` helper.
- Vectorize bulk math: `TensorPrimitives.Add/Multiply/Sum/Min/Max`, `Vector<T>`, `Fma.MultiplyAdd`.
- Kill hot div/mod: precompute `1/dt`, prefer `& (m-1)` for pow2 wraps, LUT for normalize percents.

**Don't:** coin-toss `if` per element on random data, hand `>>31` masks (use `Math/BitOperations`), manual unroll, per-elem SIMD `Extract/Insert`.

```csharp
// GOOD: branchless + vector
s += TensorPrimitives.Sum(falloffSpan); // or
for (int i = 0; i < n; i++) dmg[i] = Math.Min(dmg[i], cap);
// + sort once: Array.Sort(distSpan, idSpan);
// BAD: unpredictable branch per entity
foreach (var e in ents) if (e.Dist < 50) s += e.Dmg; // sort by dist first instead
```

## 3. Dispatch / calls / boundaries (dossier 12,16)

**Do:**
- `sealed` components/systems, `static` helpers `[MethodImpl(AggressiveInlining)]` (<32B IL). Dense `switch(typeId)` over `virtual Update`.
- Cache `MethodInfo/delegate` once `static readonly`. Prefer source-gen (`Sandbox.Generator`, `GeneratedRegex`, `MemoryPack`) over runtime Reflection.
- Batch native: one `UpdateSceneBatch`, one instanced draw, one physics step with N traces - never `Interop.Trace(single)` per entity. Cache handles. `SuppressGCTransition` only tiny blittable leaf.

**Don't:** `GetMethod/Invoke` per frame, `delegate` alloc per tick, per-field `P/Invoke`, `try/catch` for control flow, `string` marshal across boundary in loop.

```csharp
// GOOD
switch (comp.TypeId) { case 0: UpdateA(ref c0); break; case 1: UpdateB(ref c1); break; }
// + static readonly Func<...> Cached = ...;
// BAD
foreach (var c in comps) c.VirtualUpdate(); // + TypeLibrary.Invoke per c
```

## 4. Collections / search / parse (dossier 14)

**Do:**
- IDs as `int`, maps as `FrozenDictionary<int,T>` / flat `int[]` pool + `CollectionsMarshal.AsSpan`. Intern string keys to int once, never `Dictionary<string>` on a hot path.
- Static sorted data: `MemoryExtensions.BinarySearch`, keep manifests/indexes sorted; join sorted, don't point-chase.
- Parse bulk: `ReadOnlySpan<byte>` + `Utf8Parser.TryParse` + `Pipelines`, `FrozenDictionary` after parse. Bitsets: `BitArray`/`Vector<ulong>` + `BitOperations.PopCount/TrailingZeroCount`.
- Range sums: static prefix `TensorPrimitives` scan; dynamic add+sum only then Fenwick/wide-B.

**Don't:** `Dictionary<string,*>` hot, `LINQ/MinBy/Select` hot, `int.Parse/ReadLine` per value, jagged `int[][]`, hand-rolled hash/Bloom before trying `Frozen`.

## 5. Float / int hygiene (dossier 15)

**Do:**
- Tolerance compares: `Math.Abs(a-b) <= 1e-4f*Math.Max(1,Math.Max(Math.Abs(a),Math.Abs(b)))`. Never `==` on pos/angle. `double` accum then cast for `Time.Delta`/damage/leaderboard sums; `TensorPrimitives.Sum` or `Fma`.
- `MathF.Sqrt / Vector3.Normalized` - already hardware-accelerated. Don't ship Quake-style hacks except shader-compat.
- Money/prices: `decimal`/`long` cents, never `float`. Net quantize: `Vector3 → Half/fixed`. NaN guard after normalize.
- IDs/ticks wrap: `unchecked((uint)(a+b))`, guard `Math.Abs(MinValue)`, `BinaryPrimitives` for save endianness. `% cols / timers / hash buckets` - hoist, mask if pow2.

**Don't:** `float.Epsilon` as tolerance, `float` loop counter to 1<<25, `x+0==x` / `x==x` assumptions (NaN), hand `popcnt/ctz` loops (use `BitOperations`), hand RSA/AES/SHA/RNG (use BCL `AesGcm/SHA256/RandomNumberGenerator`, gameplay `Random.Shared` (seeded)).

## 6. Subsystem quick recipes

| You are writing | Apply first | Avoid |
|---|---|---|
| Math / bulk transforms | SoA `float[]` + `TensorPrimitives`/`Fma`, block L1/L2, `Half/octa` packing | scalar `Matrix*` per vert, Quake rsqrt, `float==` |
| Scene / tick | `GameObjectSystem` dense `Span` sweep, flat ID→slot, dirty-mask scan | per-entity virtual, `Dictionary` lookup per tick |
| Render / cull | sort-by-dist/material, branchless AABB slab, compact alive→instance buffer, ComputeShader large | 50% `dist<R` unsorted, per-instance draw call |
| Physics / trace | 4-wide AABB, batch traces single step, `1/dt` const | per-body `P/Invoke`, `%` bucket hot, branchy broadphase |
| Net / snapshot | `int` IDs, `Frozen` tables, `ByteStream` varint batch, `XxHash3/CRC32` + `MemoryPack/protobuf` | `string` compare per packet, JSON hot, toy hash |
| Audio / DSP | `Vector<float>` blocks 64/128/256 + `FMA`/LUT, `ConditionalSelect` gates | per-sample `if/div`, per-source ray-march w/o cache |
| UI / layout | int style IDs, dirty-rect cache, shape once (Skia/HarfBuzz) | `HashSet<string>` lookup + reflow per frame |
| Files / assets | 1MB chunks async + pool, sorted index + 2-pointer merge, `MemoryCache` MRU for scans | small random reads, `ReadLine+int.Parse`, LRU on cyclic scan |
| Gen / hotload | bake tables/offsets at build, cache manifest, profile replay for PGO | Reflection scan at startup, Cecil rewrite per tick |

Vendored from `/home/devin/Desktop/hpc/playbook/` - full deep sift in `/home/devin/Desktop/hpc/dossier/` when that disk is available. Techniques only - verify by playing the scene.
