using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    ///
    /// Both lookups are scoped to the TARGET'S SCENE, never global: the city
    /// hands over to the runner by loading it additively and unloading itself
    /// afterwards, so for the frames the runner's Awake runs in, two main
    /// cameras and two hand-placed rigs exist. <c>Camera.main</c> and a global
    /// find answered the city's — the ship was attached to a rig and a brain
    /// that the city's unload then destroyed, and the runner's own camera was
    /// left without a brain.
    /// </summary>
    public static class CameraRigInstaller
    {
        /// <summary>Attach the chase camera to <paramref name="target"/>. Returns the rig, or null without a main camera.</summary>
        public static OrbitCameraRig Attach(ICameraTarget target, OrbitCameraSettings settings)
        {
            if (target == null || target.Transform == null) return null;
            Scene scene = target.Transform.gameObject.scene;
            var camera = FindMainCamera(scene);
            if (camera == null)
            {
                Debug.LogWarning("CameraRigInstaller: no main camera found to attach the orbit camera to.");
                return null;
            }
            if (camera.GetComponent<CinemachineBrain>() == null)
                camera.gameObject.AddComponent<CinemachineBrain>();

            var rig = FindRig(scene);
            if (rig == null)
            {
                rig = new GameObject("OrbitCameraRig").AddComponent<OrbitCameraRig>();
                if (scene.IsValid() && scene.isLoaded) SceneManager.MoveGameObjectToScene(rig.gameObject, scene);
            }
            if (settings != null) rig.settings = settings;
            rig.SetOutputCamera(camera);
            rig.SetTarget(target);
            return rig;
        }

        /// <summary>The MainCamera-tagged camera of <paramref name="scene"/>, else any camera there, else <c>Camera.main</c>.</summary>
        static Camera FindMainCamera(Scene scene)
        {
            Camera any = null;
            foreach (Camera camera in Camera.allCameras)
            {
                if (!scene.IsValid() || camera.gameObject.scene != scene) continue;
                if (camera.CompareTag("MainCamera")) return camera;
                any ??= camera;
            }
            return any != null ? any : Camera.main;
        }

        /// <summary>
        /// The rig of <paramref name="scene"/> (any rig when the scene is
        /// unknown), or null. Public for game code that holds no rig of its
        /// own but has a say over the picture (the city's air-time slow-mo
        /// cuts to the cinematic shot) — scoped to the caller's scene for the
        /// same reason Attach is.
        /// </summary>
        public static OrbitCameraRig FindRig(Scene scene)
        {
            OrbitCameraRig fallback = null;
            foreach (OrbitCameraRig rig in Object.FindObjectsByType<OrbitCameraRig>(FindObjectsSortMode.None))
            {
                if (scene.IsValid() && rig.gameObject.scene == scene) return rig;
                fallback ??= rig;
            }
            return scene.IsValid() ? null : fallback;
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
            foreach (OrbitCameraRig rig in Object.FindObjectsByType<OrbitCameraRig>(FindObjectsSortMode.None))
                if (rig.Target != null && rig.Target.Transform == target)
                    rig.NotifyWarp(delta);
        }
    }
}
