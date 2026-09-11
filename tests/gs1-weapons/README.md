# Official GS1 Weapon Fixtures

Each `.gs1` file is the source sent to the globally registered
`preagonal-client-headless.upload_script` tool with `type: weapon`.
The adjacent `.official.json` preserves its structured response and complete
bytecode. Tests compare every byte, including event flags and table ordering.
Do not regenerate expected bytecode using this compiler.

`weapon%042Bomb.gs1` was extracted from `weapons_to_test/weapon%042Bomb.txt`,
with `//#CLIENTSIDE` prepended. Two independent official compilations returned
identical 1,890-byte results on 2026-09-11. SHA-256:
`a15167b15f5063fa7bed499d16207ab1ae8a9fd91930935f54510b61110384cd`.

The probes isolate Bomb's translations, single-equals precedence, logical
branches, the four/five-call register-caching boundary, function-table order,
and table growth with collisions. They are not additional corpus weapons.

Native client functions inspected with Ghidra explain the function-table order:

- `THashList::getHashcode(TString const&)`, `001f71d0`: start at `0x1505`,
  lowercase ASCII letters, multiply by 17 and XOR each signed byte.
- `THashList::addLink`, `001f79b0`: use `hash & (capacity - 1)` and prepend.
- `THashList::expand`, `001f79f0`: start with 32 buckets, grow by eight,
  and reinsert old buckets in traversal order.
- `THashListIterator::findNextObject`, `001f85c0`: traverse buckets ascending,
  following each bucket's head-to-tail chain.

The official compiler's function-order and growth probes independently verify
that this ordering applies to its emitted function table.
