Lets create a Store screen 

The store should be the first scene which loads after the main menu 

The Store should be the scene we return after completing sucessfully a mission Police escape+ finite run and selecting next mission on the debrief screen

The Store will feature 3 main sections

1)Car Selection and Upgrade
2)Ship Selection and Upgrade
3)Character Selection and Upgrade 


each section will feature a model viewer where we can rotate the model 360 degrees in the Y axis.

we can change sections with the bumpers

we can change the model car/ship/character with up or down after a threshold (prepare the system to support different cars/ships/characters but do not implement this just yet)

under the current model we will have 5 different categories this are sliders where the player can buy upgrades each upgrade costs more than the one before and it adds to the save data for the car we should have
Speed
acceleration
weight 
resistance
handling

for the ship we should have
handling
dash power
speed multiplier
Jump Strenght

For the player (this have no real effects right now just save them to the savedata)
Hacking Speed
Hack value 
Strength
Range
Accuracy 

to the right of the model we need a small  panel with a video holder/ image holder again just place holder for displaying the piece or item we are purchasign each upgrade should have their own piece  some will have videos some will be a static image.

we need a table for the upgrades each upgrade can be upgraded 10 times in total 


the upgrades take from the total money so we need to display the money in the upper right corner and as we purchase the upgrades we substract from there as in the debrief we should animate how the money changes.

this upgrades should be saved and load when we start the police escape scene (car and player) finite runner (ship)

the defaults models prefabs are

Player Assets/03.Prefabs/Characters/PF_ROB.prefab
Car Assets/Cyberpunk_Megapolis/Prefabs/Car/CP_Quadron.prefab
Ship Assets/99.Test/Diego/3DModels/nabucodonosor.fbx









