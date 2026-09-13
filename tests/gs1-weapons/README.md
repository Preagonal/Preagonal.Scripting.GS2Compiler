# Official GS1 Weapon Fixtures

This public report documents parity results without reproducing borrowed scripts.
Corpus sources, bytecode captures, evidence, and corpus-dependent tests remain
private local material. Paths below identify that local evidence, not public downloads.
Test counts describe the recorded verification checkpoints, not the public suite.

Each `.gs1` file is the source sent to the globally registered
`preagonal-client-headless.upload_script` tool with `type: weapon`.
The adjacent `.official.json` preserves its structured response and complete
bytecode. Tests compare every byte, including event flags and table ordering.
Do not regenerate expected bytecode using this compiler.

GS1 corpus expansion has resumed after resolving all historical GS2 failures.
Any new or changed test failure takes priority over advancing to another script.

The user approved excluding all-NUL files throughout the remaining corpus without
further prompts. Preserve each original, byte length, and SHA-256; record a
corrupt-source exclusion, not a bytecode match, and continue in ordinal order.

Post-corpus parity expansion now includes four additional official probes:

| Probe | Official Bytes | Coverage |
| --- | ---: | --- |
| extra-message-codes | 142 | Bare message-code translations and literal `#7` |
| indexed-message-codes | 316 | Player/NPC indexing and repeated function reads |
| message-code-cache | 312 | Loop register caching for all new translations |
| level-cache | 314 | Direct level access and the five-reference threshold |

The GS1 adapter previously emitted literal text for `#6`, `#D`, `#N`, and `#F`.
Fresh official bytecode verifies these translations:

| Code | GS2 Expression |
| --- | --- |
| `#6` | `player.carrynpc` |
| `#6(index)` | `players[index].carrynpc` |
| `#D` | `downloadfile` |
| `#N` | `name` |
| `#N(index)` | `npcs[index].name` |
| `#F` | `level.name` |

Official retains `#7` as literal text; that behavior remains protected.
The loop probe also exposed a GS1-only exclusion of `level` from function-cache
analysis. Removing that exclusion matches both the loop and the five-reference
threshold, including direct `level` and `level.name` expressions.
No grammar changes were needed, and GS2 behavior is unchanged.

Each newly failing probe was resolved before advancing. All previous corpus and
historical GS2 expectations remain unchanged. Complete structured official
responses are adjacent to their probe sources;
`evidence/message-codes-reconciliation.json` records all failure IDs and sequential
full-suite checkpoints. These probes establish bytecode parity for the listed
forms, not runtime validation or complete message-code support.

The latest corpus matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| flamespiral | 122 | `0x02` |
| kalzor/swordsystem | 219 | `0x0a` |
| kalzor/swordsystem2 | 326 | `0x0a` |
| rupees | 199 | `0x00` |
| rupees2 | 200 | `0x00` |

All five preserve executable source unchanged. Uploads only extract the weapon
envelope, add a missing clientside marker, and normalize exterior whitespace.
Flamespiral and both rupees variants also normalize CRLF line endings.
No compiler or grammar changes were needed. Full-byte regressions protect each
capture, including event flags, tokenized sword properties, substring conversion,
loop jumps, and randomized item-drop arguments. Flamespiral is distinct from the
previously verified flame spiral.
`evidence/rupees2-reconciliation.json` records each sequential full-suite
checkpoint and the zero-regression audit.

The ordinal corpus pass is complete. `evidence/corpus-inventory.json` accounts for
all 169 source files with SHA-256 hashes, classifications, and evidence links:

| Classification | Files |
| --- | ---: |
| Original-source match | 133 |
| Documented compatible-source match | 25 |
| Clientside-section match | 1 |
| All-NUL exclusion | 8 |
| Incomplete-source exclusion | 1 |
| Non-GS1 exclusion | 1 |

All 159 matched inputs have passing full-byte regressions at the recorded checkpoint.
Hilt's inline source and official bytecode test are retained locally in
`tests/private-gs1/Gs1HiltParityTests.cs`, outside the public test project. The inventory
verified its corpus source against that test. Player/Hats is explicitly clientside
only. Every compatible source has its own saved fixture; these are not match claims
for their originals. The eight all-NUL hashes and lengths were rechecked. GC Tool
and the C# gr entry retain zero-byte rejection regressions. No corpus files remain
unclassified. Five early original fixtures also normalize trailing whitespace per
line; the inventory records these separately and verifies no other source changes.

This completes bytecode comparisons for the recorded corpus inputs, not runtime
validation or exhaustive GS1 language coverage.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| WuSeh Key | 270 | `0x03` |
| Zorbi Key | 347 | `0x83` |
| Zorbi Ring (compatible) | 2,519 | `0x03` |
| flame spiral | 124 | `0x02` |

WuSeh Key, Zorbi Key, and flame spiral preserve executable source unchanged.
All uploads extract the weapon envelope, add a missing clientside marker, and
normalize exterior whitespace.

Official rejects Zorbi Ring's removed palette command at line 124. Under the
established palette convention, its compatible fixture replaces only this removed
call with `;`, retaining its branch and all other source. The original and complete
zero-byte rejection remain in `evidence/weaponZorbi%032Ring.*`. Its original still
compiles locally (2,549 bytes), and the shared palette regression verifies the
exact transformation, official diagnostic, and distinct successful local output.

The compatible fixture initially emitted 2,525 local bytes against 2,519 official
bytes. The difference was function-register-cache use in `startswith` and
`#e(...)`. Their GS1 AST translations now use the existing embedded-expression
marker, excluding both variable-cache collection and cached emission. GS2 parsing
and emission are unchanged; no grammar changes were needed.

The fresh official `probes/zorbi-string-cache` capture (409 bytes) covers both
operations with message-code arguments, numeric substring arguments, and repeated
variables that must not create cache entries. Zorbi Ring also covers bypassing an
existing player register. Both initial mismatch tests were resolved before moving
to flame spiral, without changing previous expected bytecode.
`evidence/flame-spiral-reconciliation.json` records their failure IDs and each
sequential full-suite checkpoint.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| TriSword | 907 | `0x81` |
| Trident | 5,922 | `0x03` |
| Vampire Bite | 822 | `0x03` |
| War Staff | 1,154 | `0x00` |

All four preserve executable source unchanged. Uploads only extract the weapon
envelope, add a missing clientside marker, and normalize exterior whitespace.
Full-byte regressions protect every capture, including table ordering and event
flags, directional hit-object branches, indexed player/enemy access, board writes,
and the helper functions in Trident and War Staff.

Vampire Bite initially failed locally at line 28 because the GS1 string lexer
closed a string-containment argument at the literal's first right parenthesis.
Official accepted the original script. The shared ANTLR GS1 string mode now tracks
literal parentheses in function arguments, preserving nested commas and message
codes without swallowing following arguments or conditions. Command strings still
end at semicolons even when their literal parentheses are unbalanced.

The fresh official `probes/parenthesized-strings` capture (409 bytes) verifies
nested parentheses in both argument positions, commas, message-code interpolation,
following boolean expressions, and an unbalanced command string. Eight new shared
grammar tests assert argument boundaries, retained text, and command termination.
Both new parser failures were captured before the fix and resolved before moving
to War Staff. No emitter changes or previous expected-bytecode replacements were
needed. `evidence/war-staff-reconciliation.json` records the failure IDs and each
sequential full-suite checkpoint.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| Tools*Bank Card | 6,978 | `0x03` |
| Tools/@Bank Card | 6,979 | `0x03` |
| Tools/Bank Card | 6,978 | `0x03` |
| Treasure Map | 1,055 | `0x01` |

All four match complete official bytecode without compiler, grammar, or executable
source changes. Uploads only extract the weapon envelope, add a missing clientside
marker, and normalize exterior whitespace. Full-byte regressions protect each
bank variant's 15 function entries, table ordering, event flags, and arithmetic,
plus Treasure Map's dynamic string names, image coordinates, and helper function.
`evidence/treasure-map-reconciliation.json` records the sequential full-suite
checkpoints and zero-regression audit.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| Sword Dance | 253 | `0x80` |
| Swords Only | 213 | `0x01` |
| Titanium | 534 | `0x83` |
| Toggle Magic | 149 | `0x01` |

All four match complete official bytecode without compiler, grammar, or executable
source changes. Uploads only extract the weapon envelope, add a missing clientside
marker, and normalize exterior whitespace; Titanium's CRLF line endings are also
normalized. Full-byte regressions protect every capture, including event flags,
weapon lookup, numeric player aliases, and message interpolation.
`evidence/toggle-magic-reconciliation.json` records the sequential full-suite
checkpoints and zero-regression audit.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| Stone Morph | 251 | `0x80` |
| Stone Tablet (compatible) | 885 | `0x83` |
| Sub (compatible) | 1,093 | `0x03` |
| Suicide Dagger | 250 | `0x81` |

Stone Morph and Suicide Dagger preserve executable source unchanged. All uploads
extract the weapon envelope, add the missing clientside marker, and normalize
exterior whitespace. No compiler or grammar changes were needed.

Official rejects Stone Tablet's removed palette command at line 21. Following the
established tablet convention, its compatible fixture replaces only six removed
palette calls with empty statements,
preserving every branch. It matches official completely; the original remains
supported locally (1,037 bytes). The shared palette theory verifies the exact
transformation, zero-byte official rejection, and distinct successful local output.

Official rejects Sub's unquoted chat argument at line 10, then the
quoted-only variant's unterminated final sprite assignment at line 25. Following
the food convention, its compatible fixture quotes only that argument and adds only that
semicolon. The shared food theory checks both exact edits and diagnostics and
original/compatible local bytecode equality. Its diagnostic line numbers are now
parameters; all previous food fixtures retain their exact expected lines.
Original and intermediate sources and complete responses remain under
`evidence/weaponStone%032Tablet.*` and `evidence/weaponSub*`.

`weaponStaff_Guild Admin.txt` contains only 1,060 NUL bytes and was excluded under
the standing all-NUL rule. Its original and hash are preserved in
`evidence/weaponStaff_Guild Admin.corrupt.json`; it was not uploaded or counted as
a bytecode match. `evidence/suicide-dagger-reconciliation.json` records the
sequential full-suite checkpoints and zero-regression audit.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| Space Toy | 264 | `0x80` |
| Spooon Warp | 340 | `0x01` |
| Staff Tool (unprefixed) | 6,596 | `0x09` |
| Staff/Boots | 419 | `0x10003` |

All four match complete official bytecode without compiler, grammar, or executable
source changes. Uploads only extract the weapon envelope, add a missing clientside
marker, and normalize exterior whitespace. Full-byte regressions protect the
attribute-8 property toggle, timed warping, Staff Tool's three function entries and
indexed player inspection, and Staff/Boots' keypressed event and movement loop.
The unprefixed Staff Tool fixture is separate from the already covered `*Staff Tool`
compatible fixture. `evidence/staff-boots-reconciliation.json` records each
sequential full-suite checkpoint and the zero-regression audit.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| Skull Storm | 277 | `0x80` |
| Skull Summon | 1,007 | `0x80` |
| Soft Drink (compatible) | 1,966 | `0x03` |
| Space Storm | 4,086 | `0x100B1` |

Skull Storm, Skull Summon, and Space Storm preserve executable source unchanged.
Space Storm already has a clientside marker, retained exactly once. The other
uploads gain only a missing marker and envelope/exterior whitespace normalization,
apart from Soft Drink's documented compatible edits below.

Space Storm initially compiled to 4,030 local bytes versus 4,086 officially.
The compiler now folds GS1 arithmetic involving `pi`, while retaining bare `pi` as
its opcode, and evaluates negation before multiplication instead of relocating it
after the product. Numeric arithmetic/comparison results and nested negation no
longer receive redundant conversions before unary minus; casts and ternaries keep
their official conversions. Negative booleans fold to numbers. Fresh full-byte
`probes/space-math` (361 bytes) and `probes/space-numeric` (277 bytes) cover these
boundaries, including ordinary variables, math calls, grouped expressions,
double negation, powers, and constant precision. Shared grammars and GS2 behavior
are unchanged. All prior captures still pass; no official expectation was replaced.

Official rejects Soft Drink's unquoted chat argument at line 5. Following
the food-fixture convention, the quoted-only variant changes only that argument
to a quoted string. Official then requires a semicolon after the final
sprite assignment at line 21. The compatible fixture contains only
those two edits and matches official fully. The extended food theory checks the
exact transformations, both zero-byte rejections, and original/compatible local
bytecode equality; the original remains supported. Earlier food fixtures retain
their line-15 diagnostic checks, now supplied as a parameter. Original and
intermediate sources and responses are preserved under `evidence/weaponSoft%032Drink*`.

`evidence/space-storm-reconciliation.json` records each sequential full-suite
checkpoint and the resolved focused-probe failure.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| Portable Chair | 519 | `0x81` |
| Quiver (unprefixed) | 418 | `0x0B` |
| STARCON FireMine | 481 | `0x02` |
| Shovel (unprefixed, compatible) | 394 | `0x02` |

The first three preserve executable source unchanged. Quiver's CRLF line endings
were normalized; all three needed a clientside marker after envelope extraction.
Shovel already contains the marker, which is retained exactly once in the canonical
original and compatible fixtures. Official rejects its removed palette command at
line 18 and returns no bytecode. Following the established removed-palette
convention, the compatible source replaces only that call with an empty statement.
It matches official completely; the original remains supported locally (424 bytes).
The shared palette regression checks the exact transformation, official diagnostic,
zero-byte rejection, and distinct successful local outputs. Its test name was
generalized from tablet to palette; existing tablet assertions were not changed.

The original and complete rejection response are in `evidence/weaponShovel.*`.
An earlier upload accidentally prepended a duplicate clientside marker; its line-19
rejection and exact source remain in `evidence/weaponShovel-duplicate-marker.*`.
No compiler or grammar changes were needed. Full-byte regressions protect all four
captures. `evidence/shovel-unprefixed-reconciliation.json` records the sequential
full-suite checkpoints and zero-regression audit.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| Panda Wine | 296 | `0x00` |
| PermaClothes TM | 2,981 | `0x02` |
| Pestilence Staff | 1,196 | `0x00` |
| Poly Trail | 1,164 | `0x10089` |

All four match complete official bytecode with no compiler, grammar, or executable
source changes. Uploads only extract the weapon envelope, add the missing
clientside marker, and normalize exterior whitespace. Full-byte tests protect
dynamic appearance-string names, function table entries, polygon arrays,
indexed strings, and keypressed event flags. `evidence/poly-trail-reconciliation.json`
records each sequential full-suite checkpoint and the zero-regression audit.

Four consecutive corpus entries were excluded under the standing all-NUL rule:
`weaponPizza_Pizza.txt` (902 bytes), `weaponPizza_Salami.txt` (1,010),
`weaponPizza_Tomato.txt` (1,011), and `weaponPlayer_Hats.txt` (1,080).
Each original and SHA-256 is preserved in the corresponding
`evidence/weapon*.corrupt.json`. None was uploaded or counted as a bytecode match.
The corrupt `weaponPlayer_Hats.txt` is distinct from the verified prefixed
`weapon%045Player%047Hats.gs1` fixture; that fixture's clientside match is unchanged.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| Old Graal Warper | 613 | `0x82` |
| Opposite Boots | 2,370 | `0x02` |
| Out Of Bounds | 130 | `0x01` |
| Panda Beer | 296 | `0x00` |

All four preserve executable source unchanged. Uploads only extract the weapon
envelope, add the missing clientside marker, and normalize exterior whitespace.
No compiler or grammar changes were needed. Full-byte regressions protect map
calls and rotation conditions, Opposite Boots' color restoration and nested loops
with extra for-update semicolons, and Panda Beer's bare chat argument.
`evidence/panda-beer-reconciliation.json` records every sequential full-suite
checkpoint and the zero-regression audit.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| No NPC Weapons are allowed | 1,219 | `0x09` |
| Noise Maker | 990 | `0x02` |
| Off-limits Grenade | 105 | `0x01` |
| Official Nimda Dragon's Map | 511 | `0x08` |

All four match complete official bytecode without compiler, grammar, or executable
source changes. Uploads only extract the weapon envelope, add the missing
clientside marker, and normalize exterior whitespace. Full-byte tests protect each
capture, including No NPC Weapons' focus loops and Noise Maker's
spaced chat-call syntax and omitted final semicolons.
`evidence/nimda-dragon-map-reconciliation.json` records each sequential full-suite
checkpoint and the zero-regression audit.

Music Maker matches all 1,071 official bytes, flags `0x02`, without executable
source changes. Official accepts its unquoted `playersays` arguments as variables
or numbers, not strings. The compiler now parses those arguments with the existing
ANTLR GS1 grammar, including arithmetic, member access, and GS1 aliases. Quoted
strings retain their meaning, and unquoted multiword phrases remain a local
extension. Fresh `probes/music-playersays` (314 bytes) and `probes/music-betting`
(332 bytes) captures lock the distinctions, GS1 precedence, and command-name
variables such as `show`.

The one newly failing test assumed that quoting Betting System's bare variable
names preserved its bytecode. Its old body and the official evidence contradicting
it are documented in `evidence/betting-playersays-prior-assertion.md`. The corrected
test preserves the exact compatible-source edits and verifies local phrase support
separately from variable-versus-string semantics. No official expectations were
replaced. `evidence/music-maker-reconciliation.json` records the initial failure
and final zero-failure audit.

`weaponNinji Counter.txt` contains only 129 NUL bytes; the user approved recording
it as a corrupt-source exclusion and continuing. `weaponNinji Star.txt` also
contains only NUL bytes (590); the user explicitly approved excluding it and all
further all-NUL files. Originals and SHA-256 hashes remain preserved in
`evidence/weaponNinji-Counter.corrupt.json` and
`evidence/weaponNinji-Star.corrupt.json`. Neither was uploaded and no bytecode
match is claimed for either source.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| Meditation | 912 | `0x03` |
| MiniGameSystem | 2,992 | `0x01` |
| MoonBoots! | 143 | `0x02` |
| Morpher | 1,981 | `0x03` |

All four preserve executable source exactly; uploads only extract the weapon
envelope, add the missing clientside marker, and normalize exterior whitespace.
MiniGameSystem required two fixes verified against fresh official output:
GS1 `#W` expands to the selected weapon's image, and `#W(index)` to the indexed
weapon's image; GS1 `%` binds above multiplication/division but below powers.
The latter is implemented in the shared ANTLR GS1 grammar and AST adapter,
leaving GS2 precedence unchanged. Parentheses and left-associative repeated `%`
remain respected. The full-byte `probes/minigame-expressions` (343 bytes) and
`probes/minigame-modulo` (235 bytes) captures protect message expansion,
dynamic indices, precedence, constant evaluation, and explicit grouping.
Seven shared grammar regressions cover the new precedence tier.
MoonBoots! also retains its omitted final-statement semicolon.
`evidence/morpher-reconciliation.json` records each sequential full-suite
checkpoint and the zero-regression audit.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| Leapfrog | 663 | `0x85` |
| Lode Glove | 1,485 | `0x103` |
| Magic Smoke | 247 | `0x80` |
| Magic Vial | 665 | `0x00` |

All four match complete official bytecode without compiler, grammar, or
executable-source corrections. Uploads only extract the weapon envelope, add
the missing clientside marker, and normalize exterior whitespace. Full-byte
regressions protect each capture, including Lode Glove's nested loops and
negated single-equals conditions, and Magic Vial's interpolated text arguments.
`evidence/magic-vial-reconciliation.json` records each sequential full-suite
checkpoint and the zero-regression audit.

`weaponKnight's Shield.txt` is a user-approved corrupt-source exclusion:
all 1,241 bytes are NUL, with no weapon envelope or script to extract.
The original is untouched; `evidence/weaponKnight-Shield.corrupt.json` records
its SHA-256, length, and exact repeated-byte description. No official upload was
attempted and no bytecode match is claimed. The user initially chose to wait,
then explicitly approved skipping this entry. Its original and hash remain
preserved; no replacement source was invented.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| Jumping Boots | 1,457 | `0x81` |
| Knife of Judgement | 4,749 | `0x85` |

Both match complete official bytecode without compiler, grammar, or executable
source corrections. Uploads only extract the weapon envelope, add the missing
clientside marker, and normalize exterior whitespace. Full-byte regressions cover
their two functions each, nested loops, tile/player/compu access, and event flags.
`evidence/knife-judgement-reconciliation.json` records each full-suite checkpoint
and the zero-regression audit.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| Ice Tablet (palette-compatible) | 982 | `0x83` |
| Jellybeans | 1,177 | `0x81` |
| Joltbomb | 181 | `0x00` |
| Jump Rope | 219 | `0x81` |

Ice Tablet follows the existing palette-compatible convention: replace only its
eight `setbackpal` calls with empty statements, preserving every branch. These
cover both repeated and distinct palette resources.
Original source and the official line-22 rejection remain under
`evidence/weaponIce%032Tablet.*`. The shared tablet regression retains Fire
Tablet's earlier checks and verifies exact removals and original local acceptance
(1,159 bytes). Only the compatible input is an official bytecode match.

Fixed the redundant GS1 jump over an empty `else`; GS2 emission is unchanged.
The 189-byte `ice-tablet-empty-else` official probe locks empty semicolon/braced
else branches, nested branches, an empty then branch, and both-empty branches.
Shared grammars are unchanged. Jellybeans, Joltbomb, and Jump Rope retain their
executable source, with only envelope extraction, missing clientside markers,
newline normalization, and exterior whitespace normalization. Full-byte tests
protect all four captures. `evidence/jump-rope-reconciliation.json` records each
sequential full-suite checkpoint and the zero-regression audit.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| Hamburger (compatible) | 1,022 | `0x03` |
| Herb | 271 | `0x81` |
| Hook Thingy | 264 | `0x02` |
| Ice Cream (compatible) | 1,047 | `0x03` |

Hamburger and Ice Cream follow the existing Cheese Burger compatibility pattern:
quote only the chat-call argument, and add
the semicolon after the final sprite assignment. All other omitted
terminators, doubled semicolons, and executable text remain unchanged. Original
line-5 rejections and quoted-only line-15 rejections are preserved under
`evidence/weaponHamburger*` and `evidence/weaponIce%032Cream*`.
The shared food-fixture theory retains every previous Cheese Burger assertion
and adds the two new cases: exact compatible edits, original local acceptance,
and bytecode equality between original and compatible inputs. Only compatible
inputs are official matches; local unquoted and omitted-terminator support remains.

Herb and Hook Thingy retain their executable source, with only weapon envelope
extraction, the missing clientside marker, CRLF normalization where needed, and
exterior whitespace normalization. Hilt's existing full-byte regression covers
the intervening corpus entry and still passes; it is not a new official capture.
No compiler or shared grammar changes were needed.
`evidence/ice-cream-reconciliation.json` records each sequential full-suite
checkpoint and the zero-regression audit.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| Gaia Shot (compatible) | 2,017 | `0x02` |
| Graal Map | 148 | `0x03` |
| Graal Radio | 292 | `0x01` |
| Grog | 2,419 | `0x81` |

Gaia Shot's approved compatible input removes only the unmatched closing brace
at line 6, immediately after its first event block. Original source and the
official unexpected-closing-brace diagnostic at line 6 remain under
`evidence/weaponGaia%032Shot.*`. Tests lock the exact one-brace edit and original
rejection with no partial bytecode. Only the compatible input is an official match.

Fixed GS1 `playerz` to resolve to `player.z`, and the shared ANTLR lexer helper's
`shoot ` prototype to `RRRRRRSS`: six numeric arguments, a separate animation
string, and a final string retaining commas. The 291-byte `gaia-projectiles`
official probe covers assignments, arithmetic, interpolation, `showimg2`, and
literal/interpolated shoot arguments. Four shared parser regressions cover literal,
interpolated, braced, and empty strings. The required command-name space remains
unchanged; GS2 parsing and emission are unchanged.

Graal Map, Graal Radio, and Grog retain their executable source, adding only the
missing clientside marker and normalizing exterior whitespace after envelope
extraction. Full-byte regressions protect each capture, including Grog's nested
loops, long conditions, and shutdown function. `evidence/grog-reconciliation.json`
records each sequential full-suite checkpoint and the zero-regression audit.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| GC Uniform | 4,483 | `0x01` |
| GP Barricade | 116 | `0x01` |
| GP Flashbomb | 99 | `0x01` |
| GP Warpbomb | 98 | `0x01` |

All four match complete official bytecode without compiler, grammar, or
executable-source corrections. Uploads only extract the weapon envelope, add
the missing clientside marker, and normalize exterior whitespace. Full-byte
regressions cover GC Uniform's indexed colors, stored suit properties, and two
functions, together with all four event-flag segments.
`evidence/gp-warpbomb-reconciliation.json` records each sequential full-suite
checkpoint and the zero-regression audit.

`weaponGC%032Tool.txt` is a user-approved incomplete-source exclusion.
It ends inside a string-assignment interpolation, with no remaining argument or
closing blocks. Official rejects it with `unexpected token:  at line 388`; local rejects
the malformed statement at line 387. Both return zero bytecode. Original source
and the complete structured response are preserved under
`evidence/weaponGC%032Tool.*`; a regression checks that no partial bytecode escapes.
No compiler or grammar changes were needed. This is not an official bytecode match.
The user approved retaining this evidence and continuing to GC Uniform.
`evidence/gc-tool-truncated-reconciliation.json` records the exclusion and
zero-regression audit.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| Fire Tablet (palette-compatible) | 1,049 | `0x83` |
| Fishing Rod | 2,636 | `0x8D` |
| GC Tool 2 | 4,475 | `0x409` |
| GC Tool v2 | 1,144 | `0x01` |

Fire Tablet follows the existing palette-compatible convention: replace its six
removed-official `setbackpal` calls with empty statements, preserving all branches.
The calls cover the default palette and five effect palettes.
Its original source and official line-50 rejection remain under
`evidence/weaponFire%032Tablet.*`. A regression checks the exact six replacements
and original local acceptance (1,201 bytes). Only the compatible input is an
official match; local palette-command support remains intact.

The other three retain their executable source, adding only the missing
clientside marker and normalizing exterior whitespace after envelope extraction.
Fishing Rod exposed a redundant string-receiver conversion in GS1 `startswith`.
The fix skips conversions for already-string receivers; GS2 emission is unchanged.
The 229-byte `fishing-startswith` official probe locks literal, interpolated,
variable, numeric-message, and empty-string arguments. Full-byte regressions also
protect GC Tool 2's mouse-event flags and function table, and GC Tool v2's long
multiline message with embedded commas. Shared grammars are unchanged.
`evidence/gc-tool-v2-reconciliation.json` records each sequential full-suite
checkpoint and the zero-regression audit.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| Doll | 297 | `0x81` |
| Entertainer | 1,349 | `0x81` |
| Fake Dagger | 237 | `0x81` |
| Fire Staff | 127 | `0x01` |

All four match complete official bytecode without compiler, grammar, or
executable-source corrections. Their uploads only extract the weapon envelope,
add the missing clientside marker, and normalize exterior whitespace.
Full-byte regressions protect the captures, including Doll's NPC creation
arguments and Entertainer's comma-containing animation arguments, item loop,
and single-equals conditions. `evidence/fire-staff-reconciliation.json` records
each sequential full-suite checkpoint and the zero-regression audit.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| Dancing Shoes | 1,434 | `0x02` |
| Dark Knife | 1,604 | `0x00` |
| Defensive Fire | 757 | `0x03` |
| Dice | 309 | `0x81` |

All four match complete official output without compiler, grammar, or
executable-source corrections. Their uploads only extract the weapon envelope,
add the missing clientside directive, and normalize exterior whitespace.
Full-byte regressions protect function ordering, event flags, sprite/player
aliases, numeric operations, and interpolated arguments. Dark Knife includes
empty text arguments and final comma-containing trigger arguments; Dice combines
random/integer values with animation and chat interpolation.
`evidence/dice-reconciliation.json` records each sequential full-suite checkpoint
and the zero-regression audit.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| Clairvoyance | 847 | `0x81` |
| Claw | 407 | `0x83` |
| Coin | 514 | `0x81` |
| Conch Shell | 2,159 | `0x81` |

All four match complete official bytecode without compiler, grammar, or
executable-source corrections. Uploads only extract the weapon envelope,
add the missing clientside marker, and normalize exterior whitespace.
Each capture is protected by a full-byte regression. Coin combines random
values, integer conversions, and timed events; Conch Shell combines nested
board loops, negated logical conditions, and function caching.
`evidence/conch-shell-reconciliation.json` records each sequential full-suite
checkpoint and the zero-regression audit.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| Chain Thingy | 381 | `0x82` |
| Cheese Burger (compatible) | 1,032 | `0x03` |
| Cheese | 742 | `0x03` |
| Claim Check | 385 | `0x01` |

Chain Thingy, Cheese, and Claim Check retain their executable source, adding
only the missing clientside marker and exterior whitespace normalization after
envelope extraction. Cheese Burger needs two minimal compatible edits, following
the Betting System convention: quote the chat-call argument and add
the semicolon after the final sprite assignment. The other omitted command terminators
and the doubled semicolon remain unchanged. The original line-5 rejection and
quoted-only line-15 rejection are retained under `evidence/weaponCheese%032Burger*`.
Tests check the exact edits and preserve original-source local acceptance and
bytecode equality with the compatible input. Only the compatible source is an
official match.

Fixed GS1 flattened logical chains to skip redundant conversions for known
numeric operands while still converting assignment results. The 325-byte
`burger-logical-values` probe checks numeric operands at different positions,
AND/OR chains, assignment results, and the original single-equals condition.
The Bowling Ball regressions remain passing. GS2 and shared grammars are
unchanged. `evidence/claim-check-reconciliation.json` records the full-suite
checkpoints and zero-regression audit.

The preceding matches are:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| CC Map | 145 | `0x03` |
| Call Spaceship (palette-compatible) | 818 | `0x03` |
| Cane of Brugara | 3,087 | `0x01` |
| Cape of the Hero | 500 | `0x03` |

No compiler or grammar changes were needed. CC Map, Cane of Brugara, and Cape
of the Hero need only envelope extraction, the missing clientside marker, and
exterior whitespace normalization. Their executable source is unchanged.
Call Spaceship's single removed-official palette command becomes an
empty statement. Original source and the complete official line-24 rejection
are retained under `evidence/weaponCall%032Spaceship.*`. Its 848-byte original
local output remains supported; a regression checks acceptance, the exact source
edit, and the retained palette instruction's difference from the compatible output.
Only the compatible variant is an official match. All four captures have full-byte
regressions. `evidence/cape-hero-reconciliation.json` records the per-script suite
checkpoints and zero-regression audit.

The preceding four unchanged-source captures are also regression-locked:

| Fixture | Official Bytes | Event Flags |
| --- | ---: | --- |
| Birdshot | 7,059 | `0x93` |
| Bomb Stock | 413 | `0x0B` |
| Bounty | 340 | `0x80` |
| Bowling Ball | 3,575 | `0x11` |

Uploads only extract the weapon envelope, add the missing clientside marker,
and normalize exterior whitespace. Birdshot's ten functions match without
compiler changes, as do Bomb Stock and Bounty. Bowling Ball required GS1 emitter
fixes: preserve assignment results used as logical operands, convert those
results for the boolean test, and reread array elements after value-producing
assignment instead of copying the index. GS2 emission and shared grammars
are unchanged.

`bowling-assignments` matches all 382 bytes and `bowling-assignment-chains`
all 416 bytes. The probes cover the original nested single-equals condition,
left/right operands, three-term chains, chained assignments, array results,
and function register caching. Complete official responses and bytecode are saved;
no comparison masking is used. `evidence/bowling-reconciliation.json` retains
the per-script test checkpoints and zero-regression before/after audit.

`weapon300%032Club%032Card.gs1` matches all 959 official bytes, flags `0x03`;
`weaponAngel.gs1` matches all 399 bytes, flags `0x81`. Neither required compiler
changes or executable-source corrections. Their uploads extract the weapon script,
add the missing clientside marker, and normalize exterior whitespace. Full-byte
regressions protect both.

`weaponAstral%032Projection.gs1` matches all 1,792 official bytes, flags `0xB9`,
with its executable source unchanged. The shared GS1 ANTLR grammar now accepts
one optional semicolon after a nonempty `for` update. The `astral-loops` probe
matches all 221 bytes and covers increment, decrement, compound assignment, and
continue targets. Removing the optional semicolons leaves bytecode unchanged.
Official rejects an extra semicolon after an empty update; that source and exact
line-6 diagnostic are retained in `evidence/astral-empty-update.*`, with parser
and compiler rejection regressions. GS2 parsing and both emitters are unchanged.
The before/after audit is retained in `evidence/astral-reconciliation.json`.

`weaponBetting%032System-compatible.gs1` matches all 3,109 official bytes,
flags `0x0A`. Official rejects the original with `missing semicolon at line 2`,
returning no bytecode.
Its original upload and complete response remain in `evidence/weaponBetting%032System.*`.
The user-approved semicolon-only variant is retained as
`evidence/weaponBetting%032System-semicolons.gs1`, with its full official response.
It adds exactly 12 terminators: one after a mode assignment and eleven after
flag-clearing commands, grouped as three, four, and four occurrences.
No other text changes, including an original misspelled member name.
A regression checks the exact transformation and verifies that original
and variant both produce the same 3,109 local bytes. Original support is preserved.

Official rejects that intermediate variant's unquoted multiword chat argument
with an unexpected-symbol diagnostic at line 3. The final user-approved fixture
additionally quotes all nine chat-call arguments.
The exact 18-character quote addition is regression-checked. Original and
semicolon-only inputs still emit identical local bytecode. Subsequent Music Maker
probes corrected bare variable arguments previously emitted as strings: the final
compatible input now intentionally differs from the originals, though each remains
3,109 bytes. Quoting only multiword phrases still preserves the original local
output. Unquoted support and both original rejection responses remain preserved.

The isolated `evidence/betting-playersays-quoted.*` probe accepts the quoted
form officially (95 bytes, flags zero), emitting an ordinary function call;
this does not establish runtime function availability or chat-event behavior.
It now matches locally after decoding quoted `playersays` arguments with the
existing GS2 ANTLR string lexer. At that checkpoint, unquoted GS1 arguments were
unchanged; the later Music Maker probes and assertion reconciliation above record
their corrected expression semantics. The
289-byte `playersays-strings` probe verifies escapes, padding, empty strings,
literal `#c`, and unchanged `strequals` string handling. No emitter or shared
grammar changes were needed. The zero-regression audit is retained in
`evidence/betting-reconciliation.json`. Only the compatible source is counted
as an official match. Subsequent matches and the next ordinal entry are listed above.

`weapon%045system%047sword.gs1` matches all 239 official bytes, and
`weapon%045system4.gs1` matches all 463 bytes, each with flags `0x09`.
Neither needed compiler changes or executable-source corrections; their
uploads add only the missing clientside marker and normalize exterior whitespace.
Full-byte regressions lock chat/event conditions, tokenization, sword properties,
and the `setsword` call.

`weapon%045magic%045Effect-compatible.gs1` matches all 7,538 official bytes,
flags `0x01`. Its only executable-source correction renames the user-defined
function whose name conflicts with a control-flow keyword, including commented-out
calls for consistency. Both compilers reject the original reserved-name call;
the original upload and official unexpected-opening-parenthesis diagnostic at
line 135 are retained under `evidence/weapon%045magic%045Effect.*`. A regression
verifies the exact rename and original rejection. The original is not counted
as a bytecode match. No compiler changes were needed for this compatible input.

`weapon%045nimda%032boat%032race%032display%045.gs1` matches all 755 official
bytes, flags `0x03`, without executable-source corrections. The GS1 adapter now
maps bare `timeout` to `istimeout` only in boolean operand positions: `if`/`while`
conditions, logical operators, negation, and ternary conditions. Numeric reads,
comparisons, assignment values, indexed/member access, and a bare `for`
condition retain `timeout`. This fixes the weapon's timer comparison against zero.
GS2 parsing and emission are unchanged.

The 413-byte `nimda-timeout` and 383-byte `nimda-timeout-values` probes preserve
complete official output for these contexts, including nested parentheses,
negation of comparisons, message interpolation, increments, and both loop forms.
Both weapon uploads add only the missing clientside marker and normalize exterior
whitespace, apart from the documented Effect function rename.

`weapon%045gr%095movement.gs1` matches all 222 official bytes, flags `0x01`,
without compiler changes or executable-source corrections. The next corpus
entry, `weapon%045gr.txt`, is C#, not GS1. Its original source with the required
clientside marker and complete official rejection are retained under
`evidence/weapon%045gr.*`. Official reports an unexpected return-type keyword at
line 3 and returns zero bytes; local GS1 compilation also
fails without partial bytecode. A regression protects that rejection. This
entry is excluded as non-GS1, not counted as a bytecode match or translated.

`weapon%045magic%045Attack.gs1` matches all 2,991 official bytes, flags `0x01`,
without executable-source corrections. GS1 `removestring` now emits native
string-array removal on its dynamically resolved target, bypassing function
call state and register caching. GS1 `tokenize` omits string conversion for
literal receivers but retains it for values that need conversion; GS2 emission
is unchanged. The 296-byte `magic-strings` official probe protects dynamic
target names, final-argument commas, literal/dynamic/concatenated tokenization,
and repeated removals inside a function.

`weapon%045magic%045Defend.gs1` matches all 2,391 official bytes, flags `0x01`,
without further compiler changes or executable-source corrections. Both magic
uploads add only the missing clientside directive and normalize exterior
whitespace. Their full-byte regressions cover event flags, function tables,
register caching, string commands, interpolation, and nested control flow.

Three further game weapons match without compiler changes or executable-source
corrections: `weapon%045game%045Falling%032Blocks.gs1` (5,947 bytes),
`weapon%045game%045Graal%032Invaders.gs1` (6,934 bytes), and
`weapon%045game%045Graal%032Snake.gs1` (4,513 bytes). Each has flags `0x01`.
Their complete official responses are retained beside the inputs, which add
only the missing clientside directive and normalize exterior whitespace.
Full-byte regressions cover their combined array operations, nested loops,
function ordering, register caching, and interpolated high-score strings.

`weapon%045game%045Block%032Destroyer.gs1` matches all 4,248 official bytes,
including flags `0x01`, without executable-source corrections. Fixed `#8`
to read `player.bodyimg`; player/character assignments and indexed reads use
the same mapping. The 292-byte `block-body` probe verifies those forms,
comparisons, and repeated function references with register caching.

The 409-byte `block-braces` probe verifies literal-brace boundaries after
interpolation. Braces and their intervening text remain separate string
operands after a dynamic value, including nested braces, quotes, and text
before/after the braced segment. Text before the first dynamic value still
folds normally; message codes inside braces remain literal. The change is
in the GS1 AST adapter, not the shared grammar or GS2 emitter.

`weapon%045game%045Bomb%032Dodger.gs1` matches all 3,009 official bytes,
including flags `0x01`, without executable-source corrections or further
compiler changes. Both game uploads prepend the missing clientside marker
and trim exterior whitespace; all original executable statements remain.

`weapon%045WhistleKiller.gs1` matches all 328 official bytes, flags `0x01`,
and `weapon%045WongGuard.gs1` matches all 216 bytes, flags `0x09`. Neither
required compiler changes or executable-source corrections. Both uploads
prepend the missing clientside directive and trim exterior whitespace.

`weapon%045com013%032arena%032system%045.gs1` matches all 1,059 official
bytes, flags `0x09`, without executable-source corrections. The only initial
difference was an extra player-hurt bit from `playerhurted`. Official treats
`playertouchesme`, `playertouchesother`, `playerhurted`, and `wasshooted` as
ordinary identifiers, not event aliases. Their names and expressions still
compile; only the incorrect bit mappings were removed from both grammars.
The 165-byte `arena-event-aliases` and 158-byte `arena-event-canonical`
probes verify zero flags and `0x4016`, respectively. Equivalent GS2 probes
also cover parameterized functions followed by top-level event conditions.
The superseded unit-test expectation is recorded under `evidence/`.

The explicit GS2 probes exposed a function-skip jump that incorrectly skipped
subsequent top-level statements. Both grammars now resolve that jump at the
next top-level statement. The 295-byte GS2 `arena-top-level-flow` probe
verifies interleaved functions and statements. This also makes TradeMenu match
all 684 fresh official bytes; its current capture is in `tests/official-baselines`
and its historical 681-byte baseline remains untouched.

`weapon%045System3-compatible.gs1` matches all 4,696 official bytes,
including flags `0x09`. Its only executable-source correction removes the
unmatched final `}`. The original source and official line-251 rejection
remain under `evidence/`; a regression checks both that exact correction and
local rejection of the original. The shared GS1 program rule now requires
end-of-input instead of silently accepting a valid prefix before stray tokens.
Four grammar tests cover unmatched braces and parentheses after statements
and functions. Fixed `keycode` to emit `getkeycode`; the 286-byte
`system3-keycode` probe covers literal/interpolated arguments, nested calls,
and repeated function references with register caching.

`weapon%045Vampire.gs1` matches all 2,133 official bytes, including flags
`0x11`, without executable-source corrections or further compiler changes.
Only the missing clientside directive and exterior whitespace normalization
differ from the extracted weapon source. Its five functions exercise nested
math, movement/collision checks, single-equals conditions, and caching.

`weapon%045System-compatible.gs1` matches all 8,630 official bytes, including
flags `0xA1`. The compatible source replaces only the removed palette command
with an empty statement after adding the missing clientside directive and
trimming exterior whitespace. Original source and the official line-244
`missing semicolon` rejection remain under `evidence/`. Original local
acceptance and the exact compatible-source edit are regression-covered.
The compiler fix makes every short-circuit jump in a negated GS1 AND/OR
chain target the end of that chain before `Not`, rather than an intermediate
chain instruction. The 494-byte `system-negated-chains` probe covers mixed
groups, assignments, conditions, loops, and double negation.

`weapon%045System2.gs1` matches all 8,668 official bytes, including flags
`0x19`, without executable-source corrections. Fixed `#2` as the player's
shield image, `playerhurtpower` as `player.hurtpower`, and interpolation in
`puthorse`'s image argument. The shared grammar uses `SRR` for that command
instead of upstream's `MRR`; the historical prototype comment is retained.
Other raw-string command prototypes are unchanged. The 319-byte
`system2-properties` probe covers player/character assignments, indexed
equipment codes, property arithmetic, and `puthorse` with `#5`, `#5(1)`,
`#s`, `#f`, prefixed text, and literal braced text. Six shared lexer tests
protect interpolation and restoration of argument/command modes.

For explicitly marked mixed-side fixtures, the official server selects the
clientside section. The parity test's `clientsideOnly` argument selects that
same section locally, while the saved `.gs1` retains the complete upload.
This is test-input selection, not a compiler API change or script correction.

`weapon%045Player%047Hats.gs1` matches all 717 official clientside bytes,
including flags `0x08`. Its original `//#CLIENTSIDE` marker is preserved.
The preceding serverside `playerenters` handler is excluded from this
comparison; compiling the complete source locally still includes it and
produces 769 bytes with flags `0x09`. No compiler changes were needed.

`weapon%045RideToCC.gs1` matches all 4,568 official bytes, including flags
`0x8B`, after prepending the missing clientside directive and trimming
exterior whitespace. No executable-source corrections were needed.
Fixed GS1 `#5` as `player.horseimg`, and `arrowscount`/`exploscount` as
collection lengths. The 540-byte `ride-properties` official probe protects
reads, player/character assignments, indexed message codes, loop bounds,
and repeated function references with register caching.

`setbackpal` is deprecated and removed from the official compiler, as clarified
by the user. Keep its historical GS1 support locally; variants omitting it are
explicitly distinguished from original-source parity below.

`weapon%045BarrelRide-compatible.gs1` matches all 8,456 official bytes, including
flags `0x21`. Its compatible input adds the clientside directive and twelve
array-assignment semicolons, omits the removed `setbackpal` command, and adds
spaces after nine `freezeplayer` commands and one `setlevel2` command. Official
silently drops the unspaced forms; our compiler intentionally retains them.
The user approved spacing normalization, and a regression proves the spaced
and unspaced inputs produce identical local bytes. Original, intermediate
rejections, and the official 8,309-byte silently omitted result remain under
`evidence/`. The shared lexer now treats `this.move` as a member identifier
rather than a command, including across hidden comments and after lexer reset.

`weapon%045BombAndBowUpdater-compatible.gs1` matches all 1,768 official bytes,
including flags `0x03`. Only the clientside directive, trailing whitespace
normalization, and replacement of two removed palette commands with
empty statements differ from the extracted original. The original rejection
at line 26 is preserved under `evidence/`; a separate test retains original
local acceptance.

`weapon%045DragonRide.gs1` matches all 1,536 official bytes, including flags
`0x11`. Only the clientside directive and trailing whitespace normalization
were added to the extracted source. No compiler changes were needed.

`weapon%045GC%032Droppables1.gs1` matches all 7,876 official bytes, including
flags `0x409`. The extracted source is otherwise unchanged. This fixture
required GS1 `replacestring` to emit the native string-array replacement
operation, and `imgwidth`/`imgheight` to translate to `getimgwidth`/`getimgheight`.
Replacement targets use dynamic string-variable lookup; the operation neither
populates nor uses the enclosing function's variable cache.

`weapon%045GC%032Droppables2-compatible.gs1` matches all 7,926 official bytes,
including flags `0x409`. Its only executable-source adjustment changes the
invalid coordinate containing a backslash to its apparent intended numeric value.
The original and official `unexpected token: \` diagnostic at line 208 are retained under
`evidence/`; the original is not counted as a match. This fixture required
string conversion before indirect variable-name lookup, `sarraylen` to resolve
its string-variable argument rather than treating its text as a member name,
and `ascii` to translate to `getascii`. These changes are GS1-only.

Three complete official probes protect the translations and cache behavior:
`droppables-replace` (224 bytes), `droppables-image` (257 bytes), and
`droppables-indirection` (317 bytes). The last covers nested `#s`, interpolated
variable names, bare/dynamic `sarraylen`, and a function with cached variables.

`weapon%045Nations.gs1` matches all 6,144 official bytes, including flags
`0x09`, without executable-source adjustments. The shared ANTLR lexer now
accepts message codes in numeric builtin arguments, and the GS1 AST adapter
preserves those message expressions rather than flattening them into variable
names. `strlen` and `tokenize` emit inline operations without function call
state or register caching. Numeric builtins retain the conversion around
`#v(...)`, and `log` emits its arguments in source order.

Four complete official probes protect these changes: `nations-messages`
(258 bytes), `nations-builtins` (190 bytes), `nations-numeric` (146 bytes),
and `nations-argument-order` (103 bytes). They cover nested `#s`/`#v`, indexed
`#t`/`#p`/`#I`, isolated function bodies, and numeric builtin argument order.
The shared grammar suite passes all 142 tests, including four new parser cases.

Verification at this checkpoint: 840 compiler tests pass, zero failures
(`gs1-level-cache-verified.trx`).
All historical GS2 failures are reconciled. Ninety-nine current-official
replacements retain all original
historical evidence; see `tests/official-baselines/README.md` for fixes and probes.
Goto is separately protected as a user-approved local extension by 11 jump-target
and scope regressions; it is not counted as an official match.
All new and previously locked official
weapon/feature tests pass. The ordinal corpus pass is complete; no entries remain unclassified.
Ninji Counter and Ninji Star are recorded as corrupt-source exclusions.
Knight's Shield remains an approved corrupt-source exclusion.
GC Tool remains an approved incomplete-source exclusion.
The shared grammar/AST suite passes all 184 tests, including eight new GS1
parenthesized-string cases, the modulo-precedence, shoot argument and for-update
syntax cases, and existing GS2 NULL and ternary-precedence checks
(`gs1-parenthesized-strings-verified.trx`).
All 270 GS1 weapon regressions pass.

`weapon%045Alien.gs1` was extracted from `weapons_to_test/weapon%045Alien.txt`,
with `//#CLIENTSIDE` prepended and trailing whitespace removed. Both compilers
produce identical 7,178 bytes, including flags `0x31`. SHA-256:
`964b28e1958af996f9b5e09fa0a0e06b2f99a2cb6f42ff43ba6121afcef69f74`.
Its seven functions combine tile operations, nested single-equals expressions,
array/range checks, property aliases, and register caching.

`alien-glove` (329 bytes) isolates `playerglovepower` as `player.glovepower`:
reads, writes, compound assignment, increments, comparisons, interpolation,
and repeated function references. Direct references cache the player object,
while `#v` interpolation bypasses that register as before. The GS1 property
mapping was the only compiler change required for this weapon.

`weapon%042pkave%045system2-compatible.gs1` is an adjusted version of
`weapons_to_test/weapon%042pkave%045system2.txt`, with `//#CLIENTSIDE` prepended,
trailing whitespace removed, and its single removed palette command replaced
with an empty statement. Both compilers produce identical 2,375 bytes,
including flags `0x09`, without further compiler changes. SHA-256:
`f974f5c875ab02c150f1778c7bd78b239c67152a7e6a0365021241d4a3e64147`.
The original source and official `missing semicolon` diagnostic at line 106
are saved as `evidence/weapon%042pkave%045system2.*`. The original remains
accepted locally and is included in the palette-command compatibility test.

`weapon%042pkave%045system1-compatible.gs1` is an adjusted version of
`weapons_to_test/weapon%042pkave%045system1.txt`, with `//#CLIENTSIDE` prepended,
trailing whitespace removed, and its single removed palette command replaced
with an empty statement. Both compilers produce identical 2,494 bytes,
including flags `0x09`. SHA-256:
`682c5a1a7e7e23f5439e0c58a3a21b1cc57d9453d4b1b874011154d48ef094af`.
`evidence/weapon%042pkave%045system1.*` preserves the original and its official
`missing semicolon` diagnostic at line 114 on the removed command. Our compiler
accepts that original and emits 2,524 bytes with the palette call retained;
the original-source compatibility regression covers it separately.

`pkave-trim` (290 bytes) verifies `#T` trimming literal, dynamic, and nested
substring text, including nested substring, trim, and search operations.
Leading argument spaces disappear before emission; a literal string needs no
conversion before `ObjTrim`. Repeated `#T(#c)` inside a function does not cache
`player`, while the surrounding repeated `say2` calls still use their register.
The GS1 adapter retains the embedded-expression marker for this distinction;
GS2 trim emission remains unchanged.

`weapon%042note.gs1` was extracted from `weapons_to_test/weapon%042note.txt`,
with `//#CLIENTSIDE` prepended and trailing whitespace removed. Both compilers
produce identical 498 bytes, including flags `0x81`, without further compiler
changes. SHA-256:
`c2e04b27fd9153d40f61cbcd93d3db6c9bb151bf9ba10847105d40431a3c9f92`.
Its multiline `say2` arguments retain indentation and literal `#b` markers
while removing line breaks. Final semicolons are omitted before closing braces;
the empty `playerenters` branch still contributes its event flag.

`weapon%042bank%032card.gs1` was extracted from
`weapons_to_test/weapon%042bank%032card.txt`, with `//#CLIENTSIDE` prepended
and trailing whitespace removed. Both compilers produce identical 6,973 bytes,
including flags `0x03`, without further compiler changes. SHA-256:
`f51467f90f0cb20ea90d96aafd4c4746a33c697b8a5cff693bff66f24a0668bb`.
This lowercase weapon is distinct from `*Tools/Bank Card` and has its own
official response and full-script regression fixture.

`weapon%042Warp%032Ring-compatible.gs1` is an adjusted version of
`weapons_to_test/weapon%042Warp%032Ring.txt`, with `//#CLIENTSIDE` prepended,
trailing whitespace removed, and its single removed palette command replaced
with an empty statement. Both compilers produce identical 1,317 bytes,
including flags `0x03`, without further compiler changes. SHA-256:
`b93b907b4f5a51bf02b3c479ddf28810f40da1b4e65ba6fa71593872f654e5c4`.

This is not an original-source parity claim. `evidence/weapon%042Warp%032Ring.*`
preserves the original source and official `missing semicolon` diagnostic
at line 27 on the removed palette command. The original compiles locally to 1,347
bytes, including the palette call. A separate regression verifies original
acceptance, that the variant removes only this command, and distinct output;
existing focused tests also assert the command and argument remain emitted.

`weapon%042Torch.gs1` was extracted from `weapons_to_test/weapon%042Torch.txt`,
with `//#CLIENTSIDE` prepended and trailing whitespace removed. Both compilers
produce identical 1,447 bytes, including flags `0x09`, without further
compiler changes. SHA-256:
`5a7957260dcd7d87c722ce92d75c4c8e26b95569193503ec76419e4703f2fc43`.
The full fixture covers animation changes, string predicates, player aliases,
single-equals conditions, and `callweapon` inside a loop.

`weapon%042Tools%047Bank%032Card.gs1` was extracted from
`weapons_to_test/weapon%042Tools%047Bank%032Card.txt`, with `//#CLIENTSIDE`
prepended and trailing whitespace removed. Both compilers produce identical
6,979 bytes, including flags `0x03`, without further compiler changes.
SHA-256: `e599e444d4441a5651ea2bf7a968f0d18f0e6a7e545c1504d3031ae1f8074b8b`.
Its fifteen functions exercise arrays, arithmetic, string interpolation,
register caching, and function-table ordering together.

`weapon%042Staff%032Tool-compatible.gs1` is a delimiter-corrected version of
`weapons_to_test/weapon%042Staff%032Tool.txt`, with `//#CLIENTSIDE` prepended
and trailing whitespace removed. It adds a missing closing parenthesis in a
function's condition and closes that function before the next top-level
function. Existing statements, including the unbraced `else` and following
`setplayerprop`, are preserved. Both compilers produce identical 6,619 bytes,
including flags `0x09`. SHA-256:
`5b8b493c97b059f0e9497749a02784c597c4fe77c34bd78fc01e9b99201af06b`.

This is not an original-source parity claim. `evidence/weapon%042Staff%032Tool.*`
preserves the original source and official rejection: `unexpected symbol {`
at line 285, in the affected condition.

Three saved official probes isolate the Staff Tool fixes:

- `staff-properties` (289 bytes): fixed/indexed player attributes and colors,
  `#3` head images, direct `setcharprop` chat/head assignments, and multiline
  strings. Literal CR/LF characters disappear without adding or removing
  indentation spaces; braced string delimiters remain literal.
- `staff-equals` (223 bytes): single equals compares in conditional contexts,
  while its operands switch to value contexts. Further equals form
  right-associated assignments. Negated assignment values remain available.
- `staff-contexts` (249 bytes): explicit equality and arithmetic inherit
  conditional interpretation; `for` conditions use assignment semantics,
  unlike `while`. Assignment values survive assignment/ternary nesting,
  and ternaries used as `if` conditions receive numeric conversion.

`weapon%042Shovel.gs1` was extracted from `weapons_to_test/weapon%042Shovel.txt`,
with `//#CLIENTSIDE` prepended and trailing whitespace removed. It matches all
2,468 official bytes, including flags `0x85`, without further compiler changes.
SHA-256: `c44a32479fddd359c6ab7e92bc36e764e05bc3fc208c749e46b2607430593b47`.
This is the corpus weapon, not the earlier small Shovel example containing
`setbackpal` that the official compiler rejected.

`weapon%042Rat%032Form.gs1` was extracted from
`weapons_to_test/weapon%042Rat%032Form.txt`, with `//#CLIENTSIDE` prepended
and trailing whitespace removed. Both compilers produce identical 1,621 bytes,
including flags `0x03`. SHA-256:
`be0433a47caffe33e45f35cd95bffb60807e4945763927b302774f3a3f63a824`.
`rat-properties` (233 bytes) verifies cache isolation for the entire
`setplayerprop` assignment: player chat, fixed/computed attributes, colors,
and interpolated values. Ordinary player references in the same function
continue to use cached registers.

`weapon%042Quiver-compatible.gs1` is an adjusted version of
`weapons_to_test/weapon%042Quiver.txt`, with `//#CLIENTSIDE` prepended and
trailing whitespace removed. The original multi-value range at line 181
is expanded into two explicit checks; two doubled semicolons before `else`
are reduced to one. Both compilers produce identical 9,704 bytes, including
flags `0xA9`. SHA-256:
`0c4ff7aa15c6a56b7b5001a16f2c4c6597dde6b1fb4995aacf8e372431164d56`.

This is not an original-source parity claim. `evidence/weapon%042Quiver.*`
preserves the official rejection of a multiple-subject range check at line 181.
`evidence/weapon%042Quiver-expanded-range.*` preserves the subsequent rejection
at line 324 caused by a doubled semicolon before `else`. Our parser also
rejects that terminator; normalizing only the two terminators permits the
original multi-value range locally, covered by a separate regression test.

`quiver-strings` (514 bytes) verifies sword/shield power aliases, `#n`,
`indexof`, and context-dependent cache behavior. `setstring`, dynamic string
names, `setarray`, and `indexof` bypass cached variables; ordinary `say2`
interpolation and `strequals` can use them. `quiver-multiple-range` (107 bytes)
locks the official-accepted array subject for interval and set membership.
Local tests verify the comma shorthand emits the same bytes and retains
every subject, including event references. Beta4's `visitExpressionIn` and
the engine's all-values membership operations support this interpretation.

`weapon%042Pizza%032Stack.gs1` was extracted from
`weapons_to_test/weapon%042Pizza%032Stack.txt`, with `//#CLIENTSIDE` prepended
and trailing whitespace removed. It matches all 1,348 official bytes,
including flags `0x03`, without additional compiler changes. SHA-256:
`f06da706aaf114689d1b53cda1e63116d0648b71c07944afbfb6b7e03b26f5a8`.

`weapon%042Bomb.gs1` was extracted from `weapons_to_test/weapon%042Bomb.txt`,
with `//#CLIENTSIDE` prepended. Two independent official compilations returned
identical 1,890-byte results on 2026-09-11. SHA-256:
`a15167b15f5063fa7bed499d16207ab1ae8a9fd91930935f54510b61110384cd`.

`weapon%042Bow.gs1` was extracted from `weapons_to_test/weapon%042Bow.txt`,
with `//#CLIENTSIDE` prepended. Its official result contains 4,301 bytes.
SHA-256: `964b881d95c9ea37e72dc147c069765c6366a0fa3f44f031eec1cfa1fecb30a4`.

`weapon%042Bomb%032Bag.gs1` was extracted from
`weapons_to_test/weapon%042Bomb%032Bag.txt`, with `//#CLIENTSIDE` prepended.
Its official result contains 7,295 bytes, including event flags `0x8D`.

`weapon%042Bruges%032Bird.gs1` was extracted from
`weapons_to_test/weapon%042Bruges%032Bird.txt`, with `//#CLIENTSIDE` prepended.
Two official compilations returned identical 6,985-byte results on 2026-09-12,
including event flags `0x83`. SHA-256:
`4d9f336e21ff6b5ef28d73c6c8bb1deea1cfa92b7c00948b8074b6fd3c682f6d`.

`weapon%042Clock-compatible.gs1` is an adjusted version of
`weapons_to_test/weapon%042Clock.txt`, not an original-source parity claim.
It has `//#CLIENTSIDE` prepended, nine `setbackpal` commands replaced with
empty statements, and three final assignment semicolons inserted. The same
adjusted source produces identical 8,874 bytes in both compilers, with flags
`0x10001`. SHA-256:
`611e60def93f3328e253228612d0bee5b3f8a4729561bbb714970d554d87e712`.

The original Clock and its official rejection at line 306 (`setbackpal`) are
preserved as `evidence/weapon%042Clock.*`. Removing only those commands still
produces an official missing-semicolon diagnostic at line 359, retained as
`evidence/weapon%042Clock-without-setbackpal.*`. Our compiler accepts the
original, retains `setbackpal`, and produces the same bytes with or without
those three final semicolons. These extensions are tested separately.

The probes isolate Bomb's translations, single-equals precedence, logical
branches, the four/five-call register-caching boundary, function-table order,
and table growth with collisions. They are not additional corpus weapons.

`weapon%042Crystal%032Ball.gs1` was extracted from
`weapons_to_test/weapon%042Crystal%032Ball.txt`, with `//#CLIENTSIDE` prepended
and trailing whitespace removed. Both compilers produce identical 3,243 bytes,
including event flags `0x83`. SHA-256:
`a7c4efd42409b6f5e16abb78d82c653f821d2c9b41a24da3b487b7a0e2b12aac`.

Its `crystal-strings` probe verifies `tokenscount`, literal `#b` before and
after interpolation, consecutive codes, unknown `#z`, and escaped hashes.
After interpolation, literal hashes and their following text are separate
string operands; plain constant strings remain combined. `crystal-arguments`
also locks these boundaries in trailing `triggeraction`/`setshootparams`
arguments, `setstring`, and a string comparison. Their official results contain
260 and 240 bytes respectively.

Bow probes cover legacy string lookups, `playerdarts`, `setani` arguments,
the four/five-reference cache boundary, repeated player-member access,
multi-term OR conditions, and numeric conversion after `int()`.
The complete Bow fixture also locks register ordering for mixed references.

`weapon%042NPCWDisable.gs1` was extracted from
`weapons_to_test/weapon%042NPCWDisable.txt`, with `//#CLIENTSIDE` prepended
and trailing whitespace removed. Both compilers produce identical 419 bytes,
including event flags `0x01`. SHA-256:
`c1b61b5bf838b352f08f0a3f67c9b5398e223c689b68fe9f2be3bd8784f0135e`.
`npcwdisable-spacing` (297 bytes) verifies leading argument whitespace,
preserved trailing spaces, interpolation, braced/quoted text, and builtin
string comparisons. It also locks literal `triggeraction` parameters folded
into one string while preserving spaces after commas inside its final argument.

`weapon%042Mace.gs1` was extracted from `weapons_to_test/weapon%042Mace.txt`,
with `//#CLIENTSIDE` prepended and trailing whitespace removed. Both compilers
produce identical 2,506 bytes, including event flags `0x83`. SHA-256:
`e6e99add40efc700ca3d6fb559c39596756acb395c35bc045042b24f97059ad3`.
`mace-sprite` (246 bytes) verifies that `playersprite` reads and writes
`player.sprite`, including compound assignment, increment, comparison,
and repeated references inside a function.

`weapon%042Hammer.gs1` was extracted from
`weapons_to_test/weapon%042Hammer.txt`, with `//#CLIENTSIDE` prepended and
trailing whitespace removed. Both compilers produce identical 2,823 bytes,
including event flags `0x87`. SHA-256:
`b87e1996b1666425590ae25996971a9d3e330faccdcc172ac7d803c1371b71b4`.

`hammer-negation` (418 bytes) locks negated AND/OR groups, double negation,
conditions on either side of AND, loops, and value assignments. In GS1,
short-circuiting inside a negated group must reach its `Not` instruction,
not jump directly to the enclosing condition's failure target.
`hammer-for-conditions` (384 bytes) verifies value-producing logical AND/OR
conditions in `for` loops, including mixed/nested groups, break/continue,
and contrasting `if`/`while` control-flow conditions. Both changes are
scoped to GS1; the saved GS2 fixtures retain their previous output.

`weapon%042Fishbowl.gs1` was extracted from
`weapons_to_test/weapon%042Fishbowl.txt`, with `//#CLIENTSIDE` prepended and
trailing whitespace removed. Both compilers produce identical 2,074 bytes,
including event flags `0x8B`. SHA-256:
`b2eb48a506f595322ee4a0bfa7988d8f6f1ae42e753a378678d33c21a93f06a1`.

`fishbowl-image` (269 bytes) verifies that `#f` reads the current object's
`image` in strings, comparisons, and `putnpc`, while `{#f}` remains literal.
Its repeated function reads do not create a cached register. `fishbowl-cache`
(161 bytes) proves direct `image` reads can use a cached register while `#f`
still reads the variable directly. The AST retains this message-code origin
so equal-looking variable references do not erase that distinction.

Bomb Bag probes cover indexed player/weapon properties, numeric argument
order, inclusive ranges, `strtofloat`, `elseif`, string-list mutations,
tokenization, and builtin names used as member identifiers. The complete
fixture locks their combined output and register ordering.

Bruges Bird's full fixture covers command-prefixed user-function names and
condition identifiers. Its five probes isolate the following behavior:

- `bruges-values`: collection-count/player aliases, repeated negative decimals,
  and arithmetic constant folding.
- `bruges-cache`: for/while-loop references are cached, while math and `#v`
  expressions do not use or contribute to the enclosing function's cache.
- `bruges-boundaries`: loop initializers and nested loops also qualify for
  caching; constant folding handles fractional results and negative exponents.
- `bruges-precision` and `bruges-thresholds`: near-integer encoding, nine-place
  fractional formatting, and signed integer limits, including the distinction
  between integer operands and saturated floating-point text.

Ghidra's `TString::adddouble(double, int, int, bool, bool)` at `001fe500`
explains the fractional text: truncate to nine places, increment fractions
ending in `999`, carry overflow into the integer part, and trim trailing zeros.
The saved MCP probes independently verify the compiler's resulting bytecode.

Clock probes isolate the following behavior:

- `clock-arrays`: map-coordinate/player aliases and compound assignments to
  single- and multi-dimensional array elements.
- `clock-ranges`: arithmetic binds before `in`; lower/upper exclusive bounds
  emit range flags 2/1 respectively.
- `clock-empty`: empty branch bodies evaluate and discard their condition.
- `clock-equals`: the first single equals compares; subsequent equals form a
  right-associated assignment chain. Constant short-circuit conditions fold.
- `clock-events`: event flags survive folded conditions and unreachable
  branches, even though the associated instructions and strings disappear.

`action-and-chat` covers `setplayerprop #c`, action parameters, and the
comma-joined trailing arguments of `triggeraction`. `tiledef-prototypes`
and `message-spaced` preserve official command-recognition evidence.
The successful but silently omitted `messageHey` response is kept under
`evidence/`, not used as an expected bytecode fixture.

`final-string-commas` preserves commas in the last argument, while
`braced-setstring` preserves nested braces and embedded semicolons.
Locally supported joined command names and omitted final semicolons are
tested separately in `InterfaceTests`: official rejection or omission of
valid GS1 syntax is not a behavior to copy.

All fixtures still match after updating the shared GSAst grammar to beta4
`ef8855dc38c72c2c9f1de0f1bc90f325b5384a09`. The compiler's tests use its
local grammar project reference; expected bytecode remains the saved MCP output.

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
