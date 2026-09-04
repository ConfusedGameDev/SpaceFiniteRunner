currently we only have 1 city scene and 1 set of objectives  we need to do some expansions each city will have a finite number of  Levels each with their own objectives to  unlock the next city we need to complete all of this levels.

we will from now on categorize into 3 big categories
Mission = Level Complete in both escape the police and finite runner 
Levels = Level definition with objectives
Worlds = Scenes with a city prefab.

The Finite runner will always be the same World but with different Levels

each Level in the City has a matching Level in the finite runner.

 for the time being we need to have a structure which holds all this definitions
  and unlock them each time we clear a level clear a level = beat the mandatory objectives in city and finite runner  after doing this we go back to the store menu and when we
  click start mision we load the city scene and the next set of missions if we clear all the misions then we will change the scene we load (for the time being an empty scene
  with a coming soon text)  

  aditionally we need to have a mission select menu inside the main menu every time we clear a mission we add it to this screen the user can select it 
  and replay the selected mission. still starting the game from the start button should take us to the last unlocked mission.

  some missions will be unlocked automatically when beating the past mission some will require an extra objective such as a minimum level of some of the upgrades  or a minimum amount of money in the bank

  the select mission menu should remain hidden until we have completed at least one mission.