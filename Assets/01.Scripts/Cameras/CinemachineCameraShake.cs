using Unity.Cinemachine;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Cameras
{
    /// <summary>
    /// Applies the <see cref="CameraShake"/> bank's offsets to a vcam as a
    /// Cinemachine extension — the successor of the runner's old
    /// <c>CameraShaker</c>, which rewrote the camera's local pose in
    /// LateUpdate and cannot coexist with a CinemachineBrain owning that
    /// transform. The <see cref="OrbitCameraRig"/> puts one on each of its
    /// vcams; the offsets are added at the Finalize stage, in camera space,
    /// like the old local-pose shake.
    /// </summary>
    [AddComponentMenu("")]
    public class CinemachineCameraShake : CinemachineExtension
    {
        protected override void PostPipelineStageCallback(
            CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
        {
            if (stage != CinemachineCore.Stage.Finalize) return;
            CameraShake.Tick();
            Vector3 pos = CameraShake.PositionOffset;
            Vector3 rot = CameraShake.RotationOffset;
            if (pos == Vector3.zero && rot == Vector3.zero) return;
            state.PositionCorrection += state.RawOrientation * pos;
            state.OrientationCorrection = state.OrientationCorrection * Quaternion.Euler(rot);
        }
    }
}
