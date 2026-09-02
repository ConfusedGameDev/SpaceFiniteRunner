using Unity.Cinemachine;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Cameras
{
    /// <summary>
    /// The one way a scene hands its vehicle to the chase camera: ensures a
    /// CinemachineBrain on the main camera and an <see cref="OrbitCameraRig"/>
    /// in the scene (a hand-placed one wins — that is the copy carrying the
    /// designer's settings; otherwise a bare object is created), pushes the
    /// settings asset and retargets the rig. Both games call it at their
    /// spawn point (the city's CarFactory, the runner's GameManager), so the
    /// rig itself never has to know who spawns what.
    /// </summary>
    public static class CameraRigInstaller
    {
        /// <summary>Attach the chase camera to <paramref name="target"/>. Returns the rig, or null without a main camera.</summary>
        public static OrbitCameraRig Attach(ICameraTarget target, OrbitCameraSettings settings)
        {
            if (target == null) return null;
            var camera = Camera.main;
            if (camera == null)
            {
                Debug.LogWarning("CameraRigInstaller: no main camera found to attach the orbit camera to.");
                return null;
            }
            if (camera.GetComponent<CinemachineBrain>() == null)
                camera.gameObject.AddComponent<CinemachineBrain>();

            var rig = Object.FindAnyObjectByType<OrbitCameraRig>();
            if (rig == null) rig = new GameObject("OrbitCameraRig").AddComponent<OrbitCameraRig>();
            if (settings != null) rig.settings = settings;
            rig.SetTarget(target);
            return rig;
        }

        /// <summary>
        /// Tell the camera a followed transform was teleported by
        /// <paramref name="delta"/>, so the cut lands with it instead of
        /// swooping across the world. The rig follows its own anchor and eye
        /// rather than the vehicle transform, so those are warped too when the
        /// rig is on this target.
        /// </summary>
        public static void Warp(Transform target, Vector3 delta)
        {
            if (target == null) return;
            CinemachineCore.OnTargetObjectWarped(target, delta);
            var rig = Object.FindAnyObjectByType<OrbitCameraRig>();
            if (rig != null && rig.Target != null && rig.Target.Transform == target)
                rig.NotifyWarp(delta);
        }
    }
}
