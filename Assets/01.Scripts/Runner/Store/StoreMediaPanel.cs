using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.Store
{
    /// <summary>
    /// The Store's piece holder at the right of the model: the Mission
    /// Complete panel's video plate, made swappable. <see cref="Show"/> puts
    /// up a looping video (one <see cref="VideoPlayer"/> on the plate,
    /// rendering into one RenderTexture, on DSP time so a frozen clock could
    /// never stall it), else a still, else the NO SIGNAL dead screen — the
    /// holder never changes size, so the layout holds whatever is on offer.
    /// Plain C# built by the screen; <see cref="Release"/> frees the texture.
    /// </summary>
    public class StoreMediaPanel
    {
        const float Inset = 12f;

        readonly RawImage raw;
        readonly Image still;
        readonly Text noSignal;
        readonly VideoPlayer video;
        readonly RenderTexture texture;
        VideoClip current;

        public StoreMediaPanel(RectTransform parent, MenuTheme theme, Vector2 position, Vector2 size)
        {
            Image plate = MenuScreen.MakeImage("MediaPlate", parent, position, size, theme.RowPlate, theme.PlateIdle);
            Vector2 inner = size - Vector2.one * (Inset * 2f);

            var rawGo = new GameObject("Video", typeof(RectTransform));
            var rawRect = (RectTransform)rawGo.transform;
            rawRect.SetParent(plate.rectTransform, false);
            rawRect.anchorMin = rawRect.anchorMax = new Vector2(0.5f, 0.5f);
            rawRect.sizeDelta = inner;
            raw = rawGo.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.color = new Color(0.01f, 0.02f, 0.03f, 0.92f);

            still = MenuScreen.MakeImage("Still", plate.rectTransform, Vector2.zero, inner, null, Color.white);
            still.preserveAspect = true;
            still.enabled = false;

            noSignal = MenuScreen.MakeText("NoSignal", rawRect, Vector2.zero, new Vector2(inner.x, 60f),
                                           "— NO SIGNAL —", 30, theme.TextDim, theme.BodyFont, TextAnchor.MiddleCenter);

            texture = new RenderTexture(1024, 576, 0);
            video = plate.gameObject.AddComponent<VideoPlayer>();
            video.playOnAwake = false;
            video.source = VideoSource.VideoClip;
            video.isLooping = true;
            video.renderMode = VideoRenderMode.RenderTexture;
            video.targetTexture = texture;
            video.audioOutputMode = VideoAudioOutputMode.None;
            video.timeUpdateMode = VideoTimeUpdateMode.DSPTime;
        }

        /// <summary>Shows the piece: the clip when there is one, else the still, else NO SIGNAL.</summary>
        public void Show(VideoClip clip, Sprite image)
        {
            if (clip != null)
            {
                if (clip != current)
                {
                    current = clip;
                    video.Stop();
                    video.clip = clip;
                    ClearTexture();
                    video.Play();
                }
                raw.texture = texture;
                raw.color = Color.white;
                still.enabled = false;
                noSignal.enabled = false;
                return;
            }

            if (current != null)
            {
                video.Stop();
                current = null;
            }
            raw.texture = null;
            raw.color = new Color(0.01f, 0.02f, 0.03f, 0.92f);
            still.sprite = image;
            still.enabled = image != null;
            noSignal.enabled = image == null;
        }

        // A fresh RenderTexture reads black until the first frame lands;
        // clearing it keeps a stale frame of the previous clip off the plate.
        void ClearTexture()
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = previous;
        }

        public void Release()
        {
            if (video != null) video.Stop();
            if (texture != null) texture.Release();
        }
    }
}
