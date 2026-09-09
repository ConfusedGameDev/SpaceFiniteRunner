using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.Cameras;
using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.HUD;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.Screens
{
    /// <summary>
    /// The MISSION ACCOMPLISHED banner: the win's exclamation mark, slammed
    /// onto the screen over the planted fly-past shot the moment the ship is
    /// back on the track, and torn apart with the picture as the glitch
    /// ramps toward the debrief. It is the only text the win prints before
    /// the Mission Complete panel, and it never outlives that panel's opening
    /// — <see cref="GameManager"/> kills it on the frame the run ends.
    ///
    /// Every letter is its own object so the word can land one character at
    /// a time: each drops from 3× onto its slot (ease-in, so the impact is the
    /// fast part), bounces once past rest, and hits with an RGB split that
    /// converges over a few frames, a rising-pitch blip and a kick that dips
    /// the whole word. The last letter also fires the settings' camera shake,
    /// a glitch pulse, a haptic thump, a white flash and the accent underline
    /// wiping out beneath the word. While it holds, one random letter now and
    /// then flickers off-register, so the banner reads as part of the
    /// glitching picture rather than a sticker on top of it. Dismiss tears the
    /// letters apart sideways behind a growing split while the glitch fills.
    ///
    /// Runs on unscaled time (the sim is live for the fly-past but the panel
    /// freezes the clock behind the teardown). Its widths come off the theme's
    /// title font per character, so the word auto-fits in all four languages;
    /// tunables live on <see cref="GameSettings"/>' Mission complete group.
    /// </summary>
    public sealed class MissionAccomplishedBanner : MonoBehaviour
    {
        const int SortingOrder = 22;          // above the HUD (10), messages (15) and pause (20); below the debrief and game over (25)
        const int FontSize = 118;
        const float MaxWordWidth = 1720f;     // the word shrinks to fit inside this (1920 reference width)
        const float BannerY = 190f;           // above centre: the ship shrinks toward the horizon under it
        const float TrackingFraction = 0.05f; // extra spacing between letters, as a fraction of the font size
        const float SlamFromScale = 3.2f;
        const float ImpactPoint = 0.55f;      // fraction of a letter's slam spent in the drop; the rest is the bounce
        const float BounceOvershoot = 0.2f;
        const float SplitPixels = 16f;        // RGB split at impact
        const float SplitSeconds = 0.18f;
        const float KickPixels = 12f;
        const float KickSeconds = 0.14f;
        const float BandHeight = 210f;
        const float BandWipeSeconds = 0.25f;
        const float UnderlineSeconds = 0.3f;
        const float UnderlineHeight = 7f;
        const float FlashSeconds = 0.22f;
        const float FlashAlpha = 0.45f;
        const float IdleFlickerMinGap = 0.35f;
        const float IdleFlickerMaxGap = 0.9f;
        const float IdleFlickerSeconds = 0.08f;
        const float BreatheAmplitude = 0.012f;
        const float BreatheHz = 0.8f;
        const float TearSpreadPixels = 220f;
        const float TearSplitPixels = 40f;
        const float MinDismissSeconds = 0.25f;
        static readonly Color GhostRed = new(1f, 0.25f, 0.25f, 0.8f);
        static readonly Color GhostCyan = new(0.25f, 0.9f, 1f, 0.8f);

        /// <summary>The banner currently on screen, if any — one per win.</summary>
        public static MissionAccomplishedBanner Current { get; private set; }

        sealed class Letter
        {
            public RectTransform slot;   // the resting place, laid out once
            public RectTransform body;   // what animates: scale, offset
            public Text main, red, cyan;
            public float start;          // banner time the letter begins its drop
            public bool landed;
            public float split;          // current RGB split in pixels
            public float flickerLeft;    // idle off-register flicker time left
            public float tearDir;        // -1 / +1, the side it tears toward
            public float tearY;          // the small vertical drift of the tear
        }

        GameSettings settings;
        MenuTheme theme;
        AudioSource ui;
        AudioClip blip;
        readonly List<Letter> letters = new();
        RectTransform root;
        CanvasGroup rootGroup;
        RectTransform band;
        RectTransform underline;
        Image flash;
        float wordWidth;

        float time;              // unscaled seconds since Show
        float entranceEnd;       // banner time the last letter has landed and bounced
        float kick;              // 0..1, decays
        float flashLeft;
        float underlineStart = -1f;
        float nextFlicker;
        bool lastLanded;
        float dismissAt = -1f;   // banner time the tear begins; < 0 = not dismissed
        float dismissSeconds;

        /// <summary>
        /// Raises the banner over the win beat. A banner already up is replaced.
        /// </summary>
        public static MissionAccomplishedBanner Show(GameSettings settings)
        {
            if (Current != null) Current.Kill();
            var banner = new GameObject("MissionAccomplishedBanner").AddComponent<MissionAccomplishedBanner>();
            banner.settings = settings;
            banner.theme = MenuTheme.Load();
            banner.Build();
            Current = banner;
            return banner;
        }

        /// <summary>
        /// Tears the word apart over <paramref name="seconds"/> and destroys
        /// the banner. A word still entering finishes its entrance first, so
        /// a zero camera hold still shows the whole word before it goes.
        /// </summary>
        public void Dismiss(float seconds)
        {
            if (dismissAt >= 0f) return;
            dismissSeconds = Mathf.Max(MinDismissSeconds, seconds);
            dismissAt = Mathf.Max(time, entranceEnd);
            for (int i = 0; i < letters.Count; i++)
            {
                letters[i].tearDir = Random.value < 0.5f ? -1f : 1f;
                letters[i].tearY = Random.Range(-40f, 40f);
            }
        }

        /// <summary>Drops the banner this frame — a retry mid-beat, or the debrief opening.</summary>
        public void Kill()
        {
            if (Current == this) Current = null;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        // ------------------------------------------------------------- build

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            // No raycaster: the banner is a picture, the fly-past has no buttons.

            ui = gameObject.AddComponent<AudioSource>();
            ui.playOnAwake = false;
            ui.outputAudioMixerGroup = theme.UiOutput;
            blip = RpgMessageSystem.PlaceholderBlip();

            // The flash sits over everything, full screen.
            var flashGo = new GameObject("Flash", typeof(RectTransform));
            var flashRect = (RectTransform)flashGo.transform;
            flashRect.SetParent(transform, false);
            flashRect.anchorMin = Vector2.zero;
            flashRect.anchorMax = Vector2.one;
            flashRect.offsetMin = flashRect.offsetMax = Vector2.zero;
            flash = flashGo.AddComponent<Image>();
            flash.color = new Color(1f, 1f, 1f, 0f);
            flash.raycastTarget = false;

            // The root: everything that kicks and breathes together.
            var rootGo = new GameObject("Root", typeof(RectTransform));
            root = (RectTransform)rootGo.transform;
            root.SetParent(transform, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = new Vector2(0f, BannerY);
            root.sizeDelta = new Vector2(1920f, BandHeight);
            rootGroup = rootGo.AddComponent<CanvasGroup>();
            rootGroup.blocksRaycasts = false;
            rootGroup.interactable = false;
            flashRect.SetAsLastSibling();

            // The band: a dark strip the width of the screen, wiped out from the centre.
            var bandGo = new GameObject("Band", typeof(RectTransform));
            band = (RectTransform)bandGo.transform;
            band.SetParent(root, false);
            band.anchorMin = new Vector2(0f, 0.5f);
            band.anchorMax = new Vector2(1f, 0.5f);
            band.pivot = new Vector2(0.5f, 0.5f);
            band.offsetMin = new Vector2(0f, -BandHeight * 0.5f);
            band.offsetMax = new Vector2(0f, BandHeight * 0.5f);
            band.localScale = new Vector3(0f, 1f, 1f);
            var bandImage = bandGo.AddComponent<Image>();
            var bandColor = theme.Backdrop;
            bandColor.a = 0.6f;
            bandImage.color = bandColor;
            bandImage.raycastTarget = false;

            BuildLetters(MenuTextLibrary.Load().Get(MenuTextId.MissionAccomplished));

            // The underline: the accent bar that wipes out under the word on the last impact.
            var lineGo = new GameObject("Underline", typeof(RectTransform));
            underline = (RectTransform)lineGo.transform;
            underline.SetParent(root, false);
            underline.anchorMin = underline.anchorMax = new Vector2(0.5f, 0.5f);
            underline.pivot = new Vector2(0.5f, 0.5f);
            underline.anchoredPosition = new Vector2(0f, -BandHeight * 0.5f + 22f);
            underline.sizeDelta = new Vector2(wordWidth + 80f, UnderlineHeight);
            underline.localScale = new Vector3(0f, 1f, 1f);
            var lineImage = lineGo.AddComponent<Image>();
            lineImage.color = theme.Accent;
            lineImage.raycastTarget = false;

            nextFlicker = float.MaxValue; // armed once the word is in
        }

        // One slot per character, laid out on the title font's own advances so
        // the word fits in every language; the font shrinks if the word would
        // not fit the screen.
        void BuildLetters(string word)
        {
            word = (word ?? string.Empty).ToUpperInvariant();
            Font font = theme.TitleFont;
            int fontSize = FontSize;

            float spaceWidth = MenuTextLibrary.MeasureWidth("A A", font, fontSize) - MenuTextLibrary.MeasureWidth("AA", font, fontSize);
            if (spaceWidth <= 0f) spaceWidth = fontSize * 0.3f;
            var widths = new List<float>(word.Length);
            float total = 0f;
            for (int i = 0; i < word.Length; i++)
            {
                float w = word[i] == ' ' ? spaceWidth : MenuTextLibrary.MeasureWidth(word[i].ToString(), font, fontSize);
                if (w <= 0f) w = fontSize * 0.6f;
                widths.Add(w);
                total += w;
            }
            float tracking = fontSize * TrackingFraction;
            total += tracking * Mathf.Max(0, word.Length - 1);

            if (total > MaxWordWidth)
            {
                float shrink = MaxWordWidth / total;
                fontSize = Mathf.Max(24, Mathf.FloorToInt(fontSize * shrink));
                for (int i = 0; i < widths.Count; i++) widths[i] *= shrink;
                tracking *= shrink;
                total = MaxWordWidth;
            }
            wordWidth = total;

            float stagger = settings != null ? settings.winBannerLetterStaggerSeconds : 0.045f;
            float delay = settings != null ? settings.winBannerDelaySeconds : 0.2f;
            float slam = settings != null ? settings.winBannerLetterSlamSeconds : 0.32f;

            float x = -total * 0.5f;
            int visible = 0;
            for (int i = 0; i < word.Length; i++)
            {
                float w = widths[i];
                if (word[i] != ' ')
                {
                    var slotGo = new GameObject($"Letter {i}", typeof(RectTransform));
                    var slot = (RectTransform)slotGo.transform;
                    slot.SetParent(root, false);
                    slot.anchorMin = slot.anchorMax = new Vector2(0.5f, 0.5f);
                    slot.pivot = new Vector2(0.5f, 0.5f);
                    slot.anchoredPosition = new Vector2(x + w * 0.5f, 8f);
                    slot.sizeDelta = new Vector2(w, fontSize * 1.2f);

                    var bodyGo = new GameObject("Body", typeof(RectTransform));
                    var body = (RectTransform)bodyGo.transform;
                    body.SetParent(slot, false);
                    body.anchorMin = body.anchorMax = new Vector2(0.5f, 0.5f);
                    body.pivot = new Vector2(0.5f, 0.5f);
                    body.sizeDelta = slot.sizeDelta;
                    body.localScale = Vector3.zero;

                    string glyph = word[i].ToString();
                    Vector2 size = slot.sizeDelta;
                    // Ghosts first so the main glyph draws on top of them.
                    Text red = MenuScreen.MakeText("Red", body, Vector2.zero, size, glyph, fontSize, GhostRed, font, TextAnchor.MiddleCenter);
                    Text cyan = MenuScreen.MakeText("Cyan", body, Vector2.zero, size, glyph, fontSize, GhostCyan, font, TextAnchor.MiddleCenter);
                    Text main = MenuScreen.MakeText("Main", body, Vector2.zero, size, glyph, fontSize, theme.TextPrimary, font, TextAnchor.MiddleCenter);
                    red.enabled = cyan.enabled = false;

                    letters.Add(new Letter
                    {
                        slot = slot, body = body, main = main, red = red, cyan = cyan,
                        start = delay + visible * stagger
                    });
                    visible++;
                }
                x += w + tracking;
            }
            entranceEnd = delay + Mathf.Max(0, visible - 1) * stagger + slam;
        }

        // ------------------------------------------------------------ update

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            time += dt;
            float slam = settings != null ? Mathf.Max(0.05f, settings.winBannerLetterSlamSeconds) : 0.32f;

            // The band wipes out from the centre first, so the letters land on something.
            band.localScale = new Vector3(theme.Ease(time / BandWipeSeconds), 1f, 1f);

            bool tearing = dismissAt >= 0f && time >= dismissAt;
            float tear = tearing ? Mathf.Clamp01((time - dismissAt) / dismissSeconds) : 0f;

            for (int i = 0; i < letters.Count; i++)
                TickLetter(letters[i], slam, dt, tear);

            // The kick: the whole word dips on every impact and springs back.
            kick = Mathf.MoveTowards(kick, 0f, dt / KickSeconds);
            float breathe = lastLanded ? 1f + BreatheAmplitude * Mathf.Sin(time * BreatheHz * Mathf.PI * 2f) : 1f;
            root.anchoredPosition = new Vector2(0f, BannerY - KickPixels * kick * kick);
            root.localScale = Vector3.one * breathe;

            // The last impact's flash and underline.
            if (flashLeft > 0f)
            {
                flashLeft -= dt;
                flash.color = new Color(1f, 1f, 1f, FlashAlpha * Mathf.Clamp01(flashLeft / FlashSeconds));
            }
            if (underlineStart >= 0f)
                underline.localScale = new Vector3(theme.Ease((time - underlineStart) / UnderlineSeconds), 1f, 1f);

            // Idle: a random letter flickers off-register now and then.
            if (lastLanded && !tearing && time >= nextFlicker)
            {
                Letter l = letters[Random.Range(0, letters.Count)];
                l.flickerLeft = IdleFlickerSeconds;
                l.split = SplitPixels * 0.5f;
                nextFlicker = time + Random.Range(IdleFlickerMinGap, IdleFlickerMaxGap);
            }

            if (tearing)
            {
                rootGroup.alpha = 1f - theme.Ease(tear);
                if (tear >= 1f) Kill();
            }
        }

        void TickLetter(Letter l, float slam, float dt, float tear)
        {
            float t = (time - l.start) / slam;
            if (t < 0f)
            {
                l.body.localScale = Vector3.zero;
                return;
            }

            float p = Mathf.Clamp01(t);
            float scale;
            float alpha;
            if (p < ImpactPoint)
            {
                // The drop: ease-in, so it is fastest at the moment it hits.
                float q = p / ImpactPoint;
                scale = Mathf.Lerp(SlamFromScale, 1f, q * q);
                alpha = Mathf.Clamp01(q * 2f);
            }
            else
            {
                if (!l.landed) Land(l);
                // The bounce: one overshoot past rest, damped to nothing.
                float q = (p - ImpactPoint) / (1f - ImpactPoint);
                scale = 1f + BounceOvershoot * Mathf.Sin(q * Mathf.PI) * (1f - q);
                alpha = 1f;
            }

            // The RGB split converges after an impact (or an idle flicker).
            if (l.flickerLeft > 0f) l.flickerLeft -= dt;
            else l.split = Mathf.MoveTowards(l.split, 0f, dt * SplitPixels / SplitSeconds);
            float split = l.split;
            Vector2 offset = Vector2.zero;
            if (l.flickerLeft > 0f) offset.x = Random.Range(-4f, 4f);

            if (tear > 0f)
            {
                // The tear: the letter slides out sideways behind a growing split.
                float e = tear * tear;
                offset += new Vector2(l.tearDir * TearSpreadPixels * e, l.tearY * e);
                split = Mathf.Max(split, TearSplitPixels * tear);
                scale *= 1f + 0.15f * tear;
            }

            l.body.localScale = Vector3.one * scale;
            l.body.anchoredPosition = offset;
            Color c = l.main.color;
            c.a = alpha;
            l.main.color = c;

            bool ghosts = split > 0.5f;
            l.red.enabled = l.cyan.enabled = ghosts;
            if (ghosts)
            {
                float ghostAlpha = 0.8f * Mathf.Clamp01(split / SplitPixels) * alpha;
                l.red.rectTransform.anchoredPosition = new Vector2(-split, 0f);
                l.cyan.rectTransform.anchoredPosition = new Vector2(split, 0f);
                Color r = GhostRed; r.a = ghostAlpha; l.red.color = r;
                Color b = GhostCyan; b.a = ghostAlpha; l.cyan.color = b;
            }
        }

        // The frame a letter hits its slot.
        void Land(Letter l)
        {
            l.landed = true;
            l.split = SplitPixels;
            kick = 1f;

            int index = letters.IndexOf(l);
            bool last = index == letters.Count - 1;
            if (blip != null && ui != null)
            {
                ui.pitch = Mathf.Lerp(0.85f, 1.45f, letters.Count > 1 ? index / (float)(letters.Count - 1) : 1f);
                ui.PlayOneShot(blip, theme.UiVolume * 0.7f);
            }

            if (!last) return;

            // The whole word is in: the big hit.
            lastLanded = true;
            flashLeft = FlashSeconds;
            underlineStart = time;
            nextFlicker = time + Random.Range(IdleFlickerMinGap, IdleFlickerMaxGap);
            if (ui != null)
            {
                ui.pitch = 1f;
                if (theme.ConfirmClip != null) ui.PlayOneShot(theme.ConfirmClip, theme.UiVolume);
            }
            if (settings != null)
            {
                CameraShake.Shake(settings.winBannerShake);
                if (settings.winBannerGlitchPunch > 0f && GlitchController.Instance != null)
                    GlitchController.Instance.Pulse(settings.winBannerGlitchPunch);
            }
            HapticsSystem.Instance.Pulse(0.7f, 1f, 0.25f);
        }
    }
}
