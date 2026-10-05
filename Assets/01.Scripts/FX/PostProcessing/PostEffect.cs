namespace ConfusedGameDev.FiniteRunner.FX
{
    /// <summary>
    /// Every full-screen effect the <see cref="PostProcessManager"/> gates,
    /// one channel per float it lets gameplay move. The first block are
    /// overrides on the scene's global Volume (the override's intensity, plus
    /// the motion blur's clamp), written by the manager alone; the second
    /// block are the full-screen drivers' 0..1 intensities, which each driver
    /// passes through the manager before writing its material. Serialized by
    /// index on <see cref="PostProcessSettings"/> lookups — APPEND ONLY.
    /// </summary>
    public enum PostEffect
    {
        // Volume overrides
        LensDistortion,
        MotionBlur,
        MotionBlurClamp,
        Bloom,
        Vignette,
        ChromaticAberration,
        FilmGrain,

        // Full-screen drivers
        Glitch,
        DistanceFog,
        SpeedLines,
        VhsTape,
        PsxLook,
        CrtScreen,
        HyperspaceSky,
        RainAtmosphere,
    }

    /// <summary>Helpers over <see cref="PostEffect"/>.</summary>
    public static class PostEffects
    {
        /// <summary>Number of channels.</summary>
        public const int Count = (int)PostEffect.RainAtmosphere + 1;

        /// <summary>True for the channels that are overrides on the global Volume.</summary>
        public static bool IsVolume(PostEffect effect) => effect <= PostEffect.FilmGrain;

        /// <summary>What a Volume channel reads when its override is absent or off.</summary>
        public static float Neutral(PostEffect effect) => effect == PostEffect.MotionBlurClamp ? 0.05f : 0f;
    }
}
