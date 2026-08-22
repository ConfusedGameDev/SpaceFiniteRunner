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


M1 create a new branch for this feature/map  and implement the pause map menu  

M2 Implement the controllers to move around the map on the map menu 

M3  Implement the Point marker system  (should persist trough sessions)

M4 Implement the Navigation system make sure that it is visible in both map and mini map 


IMPLEMENT MILESTONE BY MILESTONE AND MARK THEM AS COMPLETE IN THIS MAPPLAN.md file