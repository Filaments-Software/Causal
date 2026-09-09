# Agent Workflow: apply checklist (companion to 00-sbox-project-playbook.md)

No bench harness in this project. Follow the playbook by default, keep changes small, verify by opening the scene and playing it.

## 1. Before touching a hot path

- [ ] Is it actually a problem in-game (hitch, frame drop, broken feel)? If not, leave it.
- [ ] Can it be fixed by structure (batch into a system, sort once, cache handle/delegate, pool buffer) instead of micro-tuning? Do that first.
- [ ] Does the change keep `Component` lifecycle / `IsValid()` / `TimeSince` conventions in `AGENTS.md`? No `async void` without a generation guard, no throw for control flow.

## 2. Apply gates (all must pass)

- [ ] **Correct?** No overflow/OOB, `ctz`/index edge cases guarded, tails safe, `NaN` guarded after normalize.
- [ ] **No new per-frame cost?** No `new` / `string` / `Dictionary<string>` / `LINQ` / Reflection / `P/Invoke` added to tick, switch, or trace paths.
- [ ] **Readable?** Prefer BCL (`TensorPrimitives`, `FrozenDictionary`, `Array.Sort`, `BitOperations`) over hand-rolled SIMD/hash/sort. Keep the simple version if tied.

## 3. Verify

- Open `Assets/scenes/causal.scene` in S&Box and play it. Exercise the CAUSE/EFFECT switch and sector traversal.
- Watch for hitches during switch transitions and loop resets. Non-critical failures log `Log.Warning`, never throw.

## 4. Agent don't-list

- Don't add manual `Avx2/Neon`, SW prefetch, `mmap`/huge-page tricks, or hand hash/crypto/RNG/sort/compress to game code.
- Don't reorder float math for speed (`-ffast-math` style) - JIT is strict and determinism matters.
- Don't add per-entity native calls, per-frame Reflection, or sync IO/asset loads in tick.
- Don't copy engine bench numbers as targets - they were direction only, from a different rig and context.

Vendored from `/home/devin/Desktop/hpc/playbook/` - full deep sift in `/home/devin/Desktop/hpc/dossier/` when available.
