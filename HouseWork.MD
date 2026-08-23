Lets do a bit of house work.


M1 ✅ DONE (2026-08-23)
we need to move all the stuff from testing into the appropiate folder 
Assets/99.Test/Jorge

scripts should go to 
Assets/01.Scripts

3d assets should go to 
Assets/02.Art
separated by models, materials and textures


Scriptable objects .asset files should go into
Assets/04.Data

UI assets should go into
Assets/06.UI

sprites in 
Assets/06.UI/01.Sprites

FONTS should go into 
Assets/06.UI/02.Fonts


Scenes should go into 
Assets/05.Scenes


M2 ✅ DONE (2026-08-23)

we need to separate the scripts into sub folders based on their system
and create assembly definitions to have a clean project.

The namespace should be ConfusedGameDev.FiniteRunner.System name
create a clear separation between Editor scripts.


ENSURE  to update all the classes with the correspondong namespace and ensure that the assembly definitions reference each other only when needed.
avoid cyclic assembly definition references.  if there is an instance where code needs to be modified to support this ALWAYS ask before making a change.

as much as possible do not change any of the code outside of the namespace and additional using statements.



M3 ✅ DONE (2026-08-23)

currently several systems spawn systems on runtime such as the main menu or the pause menu.
we need to be able to visualize the systems before we enter runtime so prefer to have the objects in the scene when possible.

This should be true for the 3 working scenes.

this objects should be always prefabs 
prefabs should live under 
Assets/03.Prefabs

if this prefabs are generated runtime based on scriptable objects they should 
A) have an odin button to regenerate
B) expose the parameters as an inline Scriptable Object File 


M4
Create a general Documentation File use HTML to make it easy to read for humans.
this should outline all the systems in the project.
This should have uml diagrams for all the systems to understand their relations
This should have UML diagrams for the in Scene objects to understand how the systems connect and what systems reference what objects

this should have a clear explanation on how to expand current systems

this should have a clear explanation on how we can adjust the parameters and what is possible to achieve for each of the systems (city generation, player car, patrol system, spaceship controls, finite runner track generation, finite runner patrol system)











