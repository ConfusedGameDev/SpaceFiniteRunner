---
description: Cheat codes — CheatDefinition assets, CheatManager buffer matching, the CheatConsole page, glyph assets
paths:
  - "Assets/01.Scripts/Cheats/**"
  - "**/CheatManager.cs"
  - "**/CheatDefinition.cs"
  - "**/CheatConsole.cs"
  - "**/CheatGlyphSet.cs"
---

# Cheat codes

`Scripts/Cheats/`. The main menu's CHEATS page reads button sequences.
**Cheats references UI, never the reverse.**

## Data

A `CheatDefinition` asset (`Data/Resources/FiniteRunner_Cheats.asset`, drawn inline on the
`CheatManager`) holds one `CheatEntry` per cheat: an `id`, a **keyboard code** authored as a plain
string ("RUMRUM", letters and digits only) and a **controller code** as a list of `CheatButton`.
**Every cheat carries both**, 4–10 entries each, validated by an Odin `InfoBox` on the entry.

**B / East and Escape can never appear in a code** — they are the menu's Back, which is why
`CheatKey` has no Escape and `CheatButton` no East. The d-pad is four ordinary buttons; the stick is
deliberately not read.

## `CheatManager` — the model

A rolling `bufferLength` (12) buffer of `CheatToken`s, tail-matched on every push. It exposes both a
scene-wirable `UnityEvent<string>` (`onCheatActivated`) and a **static `CheatActivated`** for
objects that spawn later. Unlocks live in a static set (`CheatManager.IsActive(id)`) so they survive
the scene load out of the menu.

Auto-created like `FloatingTextSystem`, but **a hand-placed one always wins the singleton** so its
inspector wiring is never destroyed.

**A token from a different device than the buffer holds wipes the buffer first** — the strip can't
mix key caps and pad glyphs, and two half-codes must never add up to a match.

## `CheatConsole` — the view

Built into the cheats `MenuScreen` by `MainMenuController`. It echoes the buffer as Kenney key caps
/ Xbox glyphs, and on a match slams the page with a glitch (shake on `MenuScreen.Root`, guarded by
`Interactive` so it never fights a slide transition, plus tear bars and colour flicker), names the
cheat, blocks input for `holdSeconds`, then glitches again while it wipes itself.

It reads whichever device is **plugged in** (the attract prompt's rule), and is polled from
`CaptureTick()` by the menu rather than its own `Update`, so the press that opened the page can't
land in the buffer.

**The page has no rows on purpose — every press on it is a token.**

## Glyph assets

Glyph art lives outside Resources, so `CheatGlyphSet` (`Data/Resources/FiniteRunner_CheatGlyphs.asset`)
is the hand-off. **`Tools → FiniteRunner → Build Cheat Assets`** fills it from
`03.UI/Keyboard & Mouse/Double` and `03.UI/Xbox Series/Double`, and creates the cheat asset with the
test codes if it is missing — **it never overwrites an existing one.**
