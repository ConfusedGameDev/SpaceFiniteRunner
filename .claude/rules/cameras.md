---
description: Chase camera — OrbitCameraRig, ICameraTarget, view modes, look-back, camera shake
paths:
  - "Assets/01.Scripts/Cameras/**"
  - "**/OrbitCameraRig.cs"
  - "**/OrbitCameraSettings.cs"
  - "**/CameraRigInstaller.cs"
  - "**/ICameraTarget.cs"
  - "**/CameraShake.cs"
  - "**/CinemachineCameraShake.cs"
  - "**/ShakeOnPad.cs"
---

# Chase camera

`Assets/01.Scripts/Cameras/`, its own asmdef `ConfusedGameDev.FiniteRunner.Cameras` — the only
game assembly besides `PoliceEscape` that references Cinemachine. Namespace
`…FiniteRunner.Cameras`. **Shared by both games.**

`OrbitCameraRig` is a code-built Cinemachine rig (OrbitalFollow locked to the target's heading, so
horizontal axis 0 is always straight behind it) that re-applies its settings asset live every
frame.

## `ICameraTarget`

It follows an `ICameraTarget` — transform, speed km/h, optional chassis box for the eye, and two
gates: `BlockPanInput` (the car mid-jump owns the stick) and `BlockModeCycle` (a menu, or the ship
mid-jump/mid-fall, owns Tab). Implemented by `CarController` and `ShipMotor`. **The rig never
references a vehicle type.**

## Installation

`CameraRigInstaller.Attach(target, settings)` is the one entry: brain on the main camera,
hand-placed rig wins, else created — **both resolved inside the TARGET'S scene, never through
`Camera.main` or a global find.**

The city → runner handoff is an additive load with the city unloaded afterwards, so during the
runner's `Awake` two main cameras and two rigs exist; the global lookups attached the ship to the
city's rig and brain, which the unload then destroyed ("A CinemachineBrain is required in the
scene"). The rig remembers that camera as its `outputCamera` for the authored far clip and the
brain's default blend.

The city calls it from `CarFactory.Spawn`, the runner from `GameManager.Awake` with
`GameSettings.cameraSettings` (`Data/Fighter_CameraSettings.asset`; empty = the scene keeps its
camera). `CameraRigInstaller.Warp` is what `CarFactory.Teleport` tells about a teleport.

**A hand-placed rig can be pre-built in edit mode** with the inspector's Odin **Setup** button
(`OrbitCameraRig.Setup`, `#if UNITY_EDITOR`): it find-or-adds the orbit vcam's components
(CinemachineCamera, OrbitalFollow, RotationComposer, Deoccluder, `CinemachineCameraShake`) on the
rig, creates the `FirstPersonCamera` **sibling** with its own, and pushes the fixed config plus the
settings asset's default framing — all in one Undo step. The button is greyed out in play mode and
once it has run, and re-enables only when the `settings` asset is swapped or a set-up component
goes missing (`setupSettings` / `setupDone` are serialized, `SetupPending`). Play-mode `Build`
shares `EnsureComponents` / `ConfigureComponents`, so a set-up rig is configured in place, never
doubled. The `CameraAnchor` and `CameraEye` stay per-run objects.

## It follows a `CameraAnchor` sibling, not the vehicle

Seated in the rig's `LateUpdate` (`DefaultExecutionOrder(-100)`, before the brain):

- `upBinding` = **WorldUp** (car): the anchor is the target pose and the orbit keeps the horizon.
- `upBinding` = **TargetUp** (ship): the binding is LockToTarget so "behind" stays behind through a
  loop or under a tube, and the anchor's up trails the vehicle's by `rollLagSeconds` — roll-only
  lag, position rigid.

Per-asset knobs: `deoccluder` (off on the ship — nothing to look through), `arrowKeysPan` (off on
the ship — it steers with them), `eyeFromChassis` / `firstPersonEyeOffset` (the ship's box is a
trigger volume, so its eye is authored).

Mouse / right stick / arrows pan; after `recenterDelay` of idle it swings home.

## Three views on one button

Tab / gamepad Back (`MenuNavigator.CameraCyclePressed`, read only while `Time.timeScale > 0` and
the main menu is shut) cycle **Far → Close → First person**. `OrbitCameraSettings.defaultMode`
(Far) is where a fresh car starts.

Far and Close are the *same* orbit vcam at two framings — the Framing group is the far one,
`closeDistance` / `closeLookHeight` / `closePitch` the close one — slid between over
`modeBlendSeconds`. **The pitch change is fed to the live axis as a delta**, so the tilt is visible
at once instead of waiting for recenter, and the pan the player holds survives.

**First person is a second vcam on a *sibling* object — never a child of the rig**, because
Cinemachine skips the priority queue for any vcam whose parent transform carries a vcam, so a child
would never go live. HardLockToTarget + RotateWithFollowTarget, `firstPersonDamping`, no
deoccluder; destroyed with the rig. It follows a `CameraEye` child the rig seats on the car every
frame off its chassis BoxCollider (`firstPersonForward` from the box centre, `firstPersonHeight`
above its top). The cut is the brain's default blend, which the rig sets to the same
`modeBlendSeconds`.

Holding look-back in first person hands the picture to the orbit for the hold (a rear-view glance)
and cuts back on release.

**Because Tab/Back belong to the camera, the city map is on M / d-pad Up**
(`MapTogglePressed`). The debug menu's second camera page, **Camera Modes**, holds the
close/first-person/blend sliders.

## Look-back

Holding the right stick button (R3) or Right Shift swings the orbit to a **fixed rear-view pose in
the vehicle's frame** — yaw `lookBackAngle` (180 = dead along the car's axis at the road behind),
`lookBackPitch` and radius `lookBackDistance`, **all absolute, never an offset from the pan the
player held**. Releasing eases it back.

The swing is driven by a 0..1 blend off a *remembered* orbit rather than by nudging the live axis:
the axis wraps at ±180 exactly where "behind" is, and the release has to land on the pan the player
had before the glance, not on wherever the wrap left it. The idle timer is pinned at 0 for the
whole swing so auto-recenter can't fight the return halfway home. The radius lerps from the one
`ApplyFraming` wrote that frame, so the release lands on the live Far/Close framing.

Position damping blends to `lookBackDamping` (0) over the same swing: the follow's lag trails the
car, and with the camera in front of it that trail pulled the rear view into the bonnet by more the
faster the car went.

## Camera shake

`CameraShake.Shake(CameraShakeSettings)` is the static bank every shake is fired at — a plain
static class, so callers need no Cinemachine reference. `CinemachineCameraShake` is the extension
the rig puts on both vcams; it adds the bank's Perlin offsets at the **Finalize** stage, in camera
space, ticked once per frame however many vcams sample it.

It replaced the runner's `CameraShaker`, which rewrote the camera transform in `LateUpdate` and
cannot coexist with a `CinemachineBrain`.

`ShakeOnPad` (`Runner/CameraFX/`) listens to `ShipMotor.PadImpulse` and picks the boost/brake
`CameraShakeSettings` asset; it no longer needs to sit on the camera.

**The runner's `Main Camera` is a scene root with a runtime-added brain — never parent it under
the ship again.**
