Lets create a Map system

Trigger whith Tab/ back button 

it should pause the game and display a full screen overview of the city.

we should be able to navigate with the wasd/left sctick

we should be able to zoom with scroll/　LT RT

we should be able to set an interest point marker


we should be able to generate a route for the user to go from current position to point marker


we should be able to delete a point marker 


lets implement it in a milestone based format

the user should be able to see the current missions and if they are completed or not if they are in order mark greyed out the ones we cannot do just yet


M0 [x] DONE - change the City builder system to be persistent. we should always have the same city unless we MANUALLY CLICK CLEAR AND GENERATE A NEW ONE.
    - Seed is pinned in PlayerPrefs (`CitySaveData`), so it survives play sessions, editor restarts AND builds.
    - `CityManager.Awake` resolves the seed once and never rolls; `Recalculate` rebuilds the SAME city.
    - `CityManager` > Actions > "Clear & Generate New City" is the only thing that rolls a new one (and pins it).
    - `CityGenerationSettings.PrepareSeedForRecalculate()` removed; "Randomize Seed" is now preview-only.
    - Side effect fixed: CityTestSettings.asset no longer gets dirtied on every play session.


M1 [x] DONE - create a new branch for this feature/map and implement the pause map menu
    - Branch `feature/map`.
    - `CityMapScreen` (InfiniteCity/Scripts/UI/Map/) - full-screen overlay, opens on Tab / gamepad Back, freezes the game.
    - Drawn as a SCHEMATIC from generated chunk data, not a camera render: the city streams and unloads,
      so a camera would show a small island in a void. `CityMapModel` generates ChunkData on demand
      (pure function of seed+coords, no GameObjects) and `CityMapRenderer` paints it one texel per cell.
    - Mission list side panel: done / active / greyed-out-locked, straight off LevelManager.
    - PauseMenu and the map lock each other out, both directions.

M2 [x] DONE - Implement the controllers to move around the map on the map menu
    - Pan: WASD / arrows / left stick. Authored in screen px/sec so it feels the same at every zoom.
    - Zoom: mouse wheel, LT/RT, +/- keys. Multiplicative, clamped to the settings' range.
    - Zoom never repaints (it rescales the RawImage); only panning into new cells does.
    - Chunks are generated a bounded number per frame, so a fast pan reveals the city without hitching.

M3 [x] DONE - Implement the Point marker system (should persist trough sessions)
    - One marker; placing again moves it. ENTER / gamepad A places, X / DELETE / gamepad West removes.
    - Aimed with a crosshair pinned at the view centre - one mechanism that works the same
      on stick, keyboard and mouse, so the map needs no second focus system.
    - Snaps to the nearest road (rings outward), so a marker is always somewhere you can drive to.
    - Persisted in PlayerPrefs as a CELL, tagged with the seed of the city it was placed in:
      it survives relaunches but is discarded when you generate a new city, rather than
      pointing at a junction that no longer exists.
    - Also fixed: the map's cached chunk data is rebuilt when the city is regenerated.

M4 [x] DONE - Implement the Navigation system make sure that it is visible in both map and mini map
    - Routes over the MAP's own road graph, not CityManager.Graph: that one only holds streamed
      chunks, so it does not even contain the far end of the city. The corridor between car and
      marker is generated first, then A* runs.
    - RoadGraph.TryFindPath upgraded from a linear-scan open list to a binary heap. The old one was
      fine for short police chases but quadratic across town; a 1575 m route over a 31,888-node
      graph now paths in ~0.8 ms. The police AI gets the same speedup for free.
    - Bounded: a marker beyond the corridor budget reports NO ROUTE - TOO FAR rather than stalling.
    - Drawn on the FULL MAP as route-coloured cells in the schematic (so it scales with zoom for free)
      and on the MINIMAP as pooled dots, resampled at a fixed metre spacing and projected with exactly
      the same maths as the police blips, so it rotates and rim-clamps like everything else there.
    - Route lives on a shared MapRoute.Current, so the minimap needs no reference to the map screen
      and the route survives closing the map - which is the point, since you close it to drive.


IMPLEMENT MILESTONE BY MILESTONE AND MARK THEM AS COMPLETE IN THIS MAPPLAN.md file