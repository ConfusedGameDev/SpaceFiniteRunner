---
description: Debug overlays — DebugManager channels, the DebugVisualizer base and its render path, road graph and AI overlays
paths:
  - "Assets/01.Scripts/Debugging/**"
  - "Assets/01.Scripts/PoliceEscape/Debugging/**"
  - "**/DebugManager.cs"
  - "**/DebugVisualizer.cs"
  - "**/RoadGraphVisualizer.cs"
  - "**/CarAiVisualizer.cs"
  - "**/AiDebug.cs"
  - "**/AiProbeLog.cs"
  - "**/DialogueTriggerVisualizer.cs"
---

# Debug visualizers

`01.Scripts/Debugging/`, namespace `…FiniteRunner.Debugging`. The AI overlays live in
`01.Scripts/PoliceEscape/Debugging/`.

## `DebugManager`

The one switch every development overlay hangs off: a `public bool isDebug` that **defaults to on**,
plus a channel bool per overlay (road graph / car paths / collision probes / perception).

**The rule it enforces centrally, rather than trusting each overlay with it: debug only exists in
the editor.** Outside `Application.isEditor` the flag is forced off every frame, the singleton
refuses to auto-create, and every registered `DebugVisualizer` is disabled — a build cannot draw an
overlay even with a debug object left in a scene.

Auto-created like `FloatingTextSystem` (**a hand-placed one wins**, since that is the copy carrying
someone's toggles), and it watches its own checkbox each frame so ticking it mid-play works.

## `DebugVisualizer`

The base every overlay derives from. It owns a `DebugLineBuffer`, rebuilds it in **`LateUpdate`**
(the AI decides in `Update` — rebuilding any earlier draws last frame's plan) and renders it through
**GL from `OnRenderObject`**, which URP invokes via its `InvokeOnRenderObjectCallbackPass`.

That is deliberate: a Gizmo-only overlay is invisible in the Game view unless the Gizmos toggle is
on, and an AI car is watched **while playing**. Only the text labels ride the gizmo pass, so they
are Scene-view only.

## The AI overlays

### `RoadGraphVisualizer`

Draws the `RoadGraph` itself, not the road meshes: a diamond per node and a **half-edge** per
connection, centre → midpoint.

**Half-edges make the graph's own rule visible** — a link is drivable only when it is *mutual*, so
two halves meeting is a real link and a lone red stub is a socket whose neighbour never answered,
which is the shape of a routing bug.

Ground / ramp / deck are coloured apart because an overpass shares its XZ with the street below. It
draws a `radius` around the player and rebuilds on an interval — the streamed graph runs to
thousands of nodes. It draws with `cutThrough: true` so a roundabout's refused directions don't read
as missing neighbours.

### `CarAiVisualizer`

Draws one car in full (route, waypoints, avoidance fan, police sight line, state label) and dims the
rest of the fleet's routes. The focused car comes from the **hierarchy selection** by default
(`FocusMode`, or nearest-to-player / all). The `VehicleIdentity` is appended to its label.

**It draws the steer aim apart from the waypoint on purpose**: the drivers aim *beside* the cell
centre, in the right-hand lane, and the gap between those two points is where every "car orbits a
junction forever" bug lives.

### Probes are recorded, never re-cast

`PoliceCarInput` / `TrafficCarInput` write each ray they fire — origin, reach, hit and the verdict it
produced — into an `AiProbeLog` while the channel is on (**nothing is allocated or logged when it is
off**), and both expose the whole picture through **`IAiDebugDriver`** (`AI/AiDebug.cs`, which also
owns the shared `ObstacleKind`).

Re-casting in the overlay would answer a different question — what the rays say *now* — and the two
differ exactly when something is wrong.

### Installation

`AiDebugInstaller` adds both overlays to the manager's object after every scene load in any scene
that has a `CityManager` — nothing to wire, and nothing for the scene builders to keep re-creating.

`DialogueTriggerVisualizer` fills all three hand-placed trigger kinds on the same debug channel:
orange dialogue, blue cinema, green challenge.
