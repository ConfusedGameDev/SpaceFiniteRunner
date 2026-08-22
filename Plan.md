# Cheat Code System — State

> Snapshot of where the cheat system stands. Verified against the real project on disk in the
> Unity editor on 2026-08-22 (6000.7.0a3). Companion to `MainMenu_Plan.md`, which built the menu
> this plugs into.

---

## 0. Task in one line

The main menu's **CHEATS** page reads button sequences: type or press a code, the page glitches,
names the cheat and fires a `UnityEvent` with its id.

---

## 1. What shipped

All of it. Code compiles, assets exist, matching and view both verified live in the editor.

| File (`Assets/99.Test/Jorge/FiniteRunner/Scripts/Cheats/`) | Role |
|---|---|
| `CheatInput.cs` | `CheatKey` (A–Z, 0–9), `CheatButton` (d-pad ×4, North/South/West, LB/RB), `CheatToken`, and `CheatInputReader` (the pollers + the char→key parser) |
| `CheatDefinition.cs` | `CheatEntry` (id + keyboard string + button list) and the asset holding them, plus the console's layout/timing knobs |
| `CheatGlyphSet.cs` | token → sprite map for the two Kenney art folders |
| `CheatManager.cs` | the model: rolling buffer, tail matcher, `UnityEvent<string>` + static event + static unlock set |
| `CheatConsole.cs` | the view: glyph strip, glitch burst, reveal, input lock |
| `Editor/CheatAssetBuilder.cs` | `Tools ▸ FiniteRunner ▸ Build Cheat Assets` |

Assets created and populated (`Data/Resources/`): `FiniteRunner_Cheats.asset`,
`FiniteRunner_CheatGlyphs.asset`.

Touched elsewhere:

- `Scripts/UI/MainMenuController.cs` — `BuildCheats()` now builds the console instead of a
  placeholder label; `UpdateNavigation` gained an early-return branch for the cheats page.
- `Scripts/UI/MenuScreen.cs` — two small public hooks: `Interactive` (true only in the `Shown`
  phase) and a public `AddEntranceItem` overload for content built outside `AddRow`/`AddLabel`.
- `Scripts/UI/MenuTextLibrary.cs` — `CheatEnterCode` / `CheatUnlocked` ids, translated in all four
  languages.
- `CLAUDE.md` — architecture bullet for the system.

---

## 2. How it works

```
press  ──►  CheatConsole.CaptureTick()      (polled by MainMenuController, not Update)
              │  device = what is PLUGGED IN
              ▼
            CheatManager.Push(token)         model: buffer + matcher
              │  buffer capped at bufferLength (12), oldest dropped
              │  token from a different device than the buffer holds → wipe first
              ▼
            tail match against every cheat's sequence for that device
              │
              ├─ no  ──►  strip repaints, done
              └─ yes ──►  UnityEvent<string> + static CheatActivated + unlock set
                            │
                            ▼
                          CheatConsole.Reveal(id)
                            glitch burst · id on screen · input BLOCKED
                            └─ holdSeconds (3s) ─► second burst, wipe, unblock
```

**The manager owns no UI and reads no input; the console owns no matching.** That split is what
lets a future in-game chord or debug page enter codes without touching the view.

### Authoring

One `CheatEntry` per cheat on `FiniteRunner_Cheats.asset` (drawn inline on the `CheatManager`).
Every cheat carries **both** device codes — the two are never asked to share a sequence, because
"up up down down" has no keyboard equivalent and "RUMRUM" has no pad one. Each is 4–10 entries,
validated by an Odin error box on the entry itself. The keyboard code is a plain string a designer
types; the pad code is a reorderable list of `CheatButton`.

Shipped test codes:

| id | Controller | Keyboard |
|---|---|---|
| `MegaCar` | ↑ ↓ ↑ ↓ X A X | `RUMRUM` |
| `DebugON` | ↓ → LB RB X Y X A | `ArrayKing` |

### Consuming

Drop a `CheatManager` in a scene and wire `onCheatActivated` in the inspector, or from code use
the static `CheatManager.CheatActivated` event / `CheatManager.IsActive("MegaCar")`. Unlocks live
in a **static** set, so they survive the scene load out of the menu. With no manager in the scene
one auto-creates (the `FloatingTextSystem` pattern); a hand-placed one always wins the singleton
so its inspector wiring is never destroyed.

---

## 3. Rules the code enforces

- **B / East and Escape can never be part of a code.** They are the menu's Back. Rather than
  filtering them at read time, they are simply absent from `CheatButton` / `CheatKey`, so a
  designer cannot author them in the first place.
- **The d-pad is four ordinary buttons.** The stick is deliberately not read — a code is pressed,
  not waggled.
- **Device follows what is plugged in**, the same rule the attract prompt uses. Pad connected
  reads pad only; otherwise keyboard. Switching device wipes the buffer, so two half-codes can
  never add up to a match and the strip never mixes key caps with pad glyphs.
- **The cheats page has no rows.** Every press on it is a token, so the d-pad no longer drives a
  (no-op) focus move alongside the capture. Only Back survives.
- **Input is polled from `CaptureTick()`, not the console's `Update`** — the owning menu decides
  when the page is really live, so the press that opened the page cannot land in the buffer.
- **The shake writes `MenuScreen.Root`** and is guarded by the new `Interactive`, so it never
  fights a slide transition for the same property.
- Glyph art lives outside Resources and cannot be loaded by path at runtime, hence `CheatGlyphSet`
  as the hand-off and an editor builder to fill it. The builder **never overwrites an existing**
  cheat asset — re-running it is safe.

---

## 4. Verified in the editor

Matching (`CheatManager`, seven cases):

- junk then `RUMRUM` → fires `MegaCar` (tail matching, so mistypes cost nothing)
- pad-half + typed-half → fires nothing, buffer holds only the 3 typed tokens
- full pad `MegaCar`, `ARRAYKING`, full pad `DebugON` → all fire
- 16 presses → buffer holds 12, oldest is `E`
- `IsActive` true for both unlocked, false for a bogus id

View (`CheatConsole`, edit-mode harness):

- `RUMRU` lights 5 of 12 slots with `keyboard_r/u/m` sprites, centred, 82px step
- a pad press repaints to `xbox_dpad_up` / `xbox_lb` (device switch wipes)
- a full 12 spans 974px — fits 1920 with room
- reveal shows `MEGACAR` + `CHEAT UNLOCKED` and sets `Blocked`
- a glitch frame displaces the page and lights all 6 tear bars

Assets: all 45 glyphs resolved, 0 missing.

**Not covered:** the 3s hold → second burst → wipe → unblock cycle is verified by state
inspection, not on screen — it needs frames, and the harness runs in edit mode. Worth one play-mode
pass on `MainMenu.unity`.

---

---

## 4a. Two things that came out of the tree, not the feature

Both were sitting in the working copy when the cheat work landed. Neither is caused by it.

- **Xbox glyph references were orphaned.** The `03.UI/Xbox Series/Double` PNGs got re-imported
  this session (`spriteMode: Single → Multiple`, plus VisionOS platform entries), which changes
  each sprite's sub-asset fileID and left all seven `MenuTheme` glyph fields NULL — footer prompts
  and the attract screen's START button silently fell back to text. **Repaired**: the seven fields
  are re-pointed at `xbox_button_a`, `xbox_button_b`, `xbox_dpad_vertical`, `xbox_dpad_horizontal`,
  `xbox_button_start`, `xbox_lb`, `xbox_rb` (the filenames its own comments name) and verified
  non-null. The re-imported `.meta`s are committed alongside so the tree stops drifting.
- **The Kenney "Keyboard & Mouse" kit exists twice**, byte-identical, 8.4 MB each:
  `03.UI/Keyboard & Mouse/` (correct — what `CheatGlyphSet` references) and `Assets/Keyboard &
  Mouse/` at the project root (a stray import). Only the first is committed. **The root copy is
  still untracked on disk and should be deleted** — left alone rather than removed unasked.

---

## 5. Next

- [ ] **Make the cheats do something.** Nothing listens to `onCheatActivated` yet — the system
      fires the id and stops there, by design. `MegaCar` and `DebugON` are test codes; wire real
      effects (or repoint them at real ones) when the gameplay hooks exist.
- [ ] **Play-mode pass** on `MainMenu.unity` — confirm the reveal→wipe timing and the glitch read
      on screen, with a pad and without.
- [ ] **Pause-menu access?** Cheats are main-menu-only right now. The console is device-agnostic
      and the manager is a singleton, so a `PauseMenu` page would be a `CheatConsole.Create` call
      and a footer hint — decide whether cheats mid-run are wanted before adding it.
- [ ] **Fullscreen glitch in the menu scene.** `CheatConsole.Burst()` already calls
      `GlitchController.Instance?.Pulse(1f)`, but `MainMenu.unity` has no `GlitchController`, so
      only the local shake/tear bars play. Add one if the menu should corrupt properly.

## 6. Open questions

1. **Should a cheat persist between sessions?** Unlocks are static — they survive the scene load
   out of the menu but not a quit. `UserSettings` is the obvious home if they should stick.
2. **Feedback for a wrong code.** There is none: an unmatched buffer just keeps rolling. That is
   the classic behaviour (you never learn you were close), but a short red flicker on the 12th
   press is an option if it reads as unresponsive in testing.
3. **Re-entering an already-unlocked cheat** currently re-fires the event. Fine for a toggle,
   wrong for a one-shot grant — listeners should guard, or the manager should gain a
   `oncePerRun` flag on the entry.
