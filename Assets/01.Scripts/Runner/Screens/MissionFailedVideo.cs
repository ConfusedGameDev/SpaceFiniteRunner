using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

using ConfusedGameDev.FiniteRunner.GameFlow;
namespace ConfusedGameDev.FiniteRunner.Screens
{
    /// <summary>
    /// The loss's reaction shot: <see cref="GameSettings.failVideo"/> played
    /// once, with its sound, in a framed square under the MISSION FAILED
    /// banner. Like the banner it is a picture of one ending, not a scene
    /// system: <see cref="Show"/> builds it on its own overlay canvas (sorting
    /// 21 — above the HUD and the story box, just under the banner's 22) and
    /// <see cref="Kill"/> takes it down (the GameManager's EndRun and
    /// Restart). It pops in, plays on UNSCALED time (the wind-down is real
    /// time, and a panel freezing the clock must not freeze the clip) and
    /// holds its last frame. <see cref="Length"/> lets the wind-down wait for
    /// the whole clip. Returns null when no clip is set.
    /// </summary>
    public class MissionFailedVideo : MonoBehaviour
    {
        const float PopSeconds = 0.22f;

        VideoPlayer player;
        RenderTexture target;
        RectTransform frame;
        float age;

        /// <summary>The clip's length in seconds (0 before it is known).</summary>
        public float Length => player != null && player.clip != null ? (float)player.clip.length : 0f;

        /// <summary>Builds the overlay and starts the clip. Null (nothing shown) when the settings carry no clip.</summary>
        public static MissionFailedVideo Show(GameSettings settings)
        {
            if (settings == null || settings.failVideo == null) return null;
            var go = new GameObject("MissionFailedVideo");
            var video = go.AddComponent<MissionFailedVideo>();
            video.Build(settings);
            return video;
        }

        /// <summary>Takes the overlay down at once. Safe on a destroyed instance.</summary>
        public void Kill()
        {
            if (this != null) Destroy(gameObject);
        }

        void Build(GameSettings settings)
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 21; // under the banner (22), over the HUD and the story box
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            VideoClip clip = settings.failVideo;
            float height = settings.failVideoSize;
            float width = clip.height > 0 ? height * clip.width / clip.height : height;

            // A dark frame a little larger than the picture, so any clip reads on any backdrop.
            var frameGo = new GameObject("Frame", typeof(RectTransform), typeof(Image));
            frame = (RectTransform)frameGo.transform;
            frame.SetParent(transform, false);
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.anchoredPosition = new Vector2(0f, settings.failVideoOffsetY);
            frame.sizeDelta = new Vector2(width + 16f, height + 16f);
            frameGo.GetComponent<Image>().color = new Color(settings.failBannerColor.r, settings.failBannerColor.g, settings.failBannerColor.b, 0.9f);
            frameGo.GetComponent<Image>().raycastTarget = false;

            target = new RenderTexture(Mathf.Max(16, (int)clip.width), Mathf.Max(16, (int)clip.height), 0) { name = "MissionFailedVideo" };
            var pictureGo = new GameObject("Picture", typeof(RectTransform), typeof(RawImage));
            var picture = (RectTransform)pictureGo.transform;
            picture.SetParent(frame, false);
            picture.anchorMin = picture.anchorMax = new Vector2(0.5f, 0.5f);
            picture.sizeDelta = new Vector2(width, height);
            var raw = pictureGo.GetComponent<RawImage>();
            raw.texture = target;
            raw.raycastTarget = false;

            player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.isLooping = false;
            player.clip = clip;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = target;
            player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            player.audioOutputMode = VideoAudioOutputMode.Direct;
            for (ushort track = 0; track < clip.audioTrackCount; track++)
            {
                player.EnableAudioTrack(track, true);
                player.SetDirectAudioVolume(track, settings.failVideoVolume);
            }
            player.Play();

            frame.localScale = Vector3.zero;
        }

        void Update()
        {
            if (frame == null) return;
            age += Time.unscaledDeltaTime;
            // Pop: overshoot to 1.12, settle to 1.
            float t = Mathf.Clamp01(age / PopSeconds);
            float scale = t < 1f ? Mathf.Sin(t * Mathf.PI * 0.5f) * 1.12f : Mathf.Lerp(1.12f, 1f, Mathf.Clamp01((age - PopSeconds) / 0.12f));
            frame.localScale = new Vector3(scale, scale, 1f);
        }

        void OnDestroy()
        {
            if (player != null) player.Stop();
            if (target != null)
            {
                target.Release();
                Destroy(target);
            }
        }
    }
}
