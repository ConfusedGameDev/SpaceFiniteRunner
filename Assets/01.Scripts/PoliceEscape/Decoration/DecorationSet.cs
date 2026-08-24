using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Decoration
{
    /// <summary>
    /// A themed pool of street props the decorator draws from — the sibling of
    /// <see cref="Population.BuildingSet"/> for road furniture. Holds the
    /// shared placement geometry (spot insets, density) and the shared impact
    /// model every <see cref="DecorationProp"/> uses: one hit budget in
    /// momentum terms, so the per-prop masses alone decide who flies and who
    /// stands firm. Build it from the Kenney decorator FBXs with
    /// Tools → Police Escape → Create Kenney Decoration Set.
    /// </summary>
    [CreateAssetMenu(fileName = "DecorationSet", menuName = "PoliceEscape/Decoration Set")]
    public class DecorationSet : ScriptableObject
    {
        [TitleGroup("Placement")]
        [Tooltip("Meters one grid cell measures in the models' own space — prop instances are scaled by cellSize ÷ this. 1 for the Kenney kit.")]
        [PropertyRange(0.1f, 60f), SuffixLabel("m", true)]
        public float nativeCellSize = 1f;

        [TitleGroup("Placement")]
        [Tooltip("Chance an eligible spot actually gets a prop — below 1 keeps the streets from looking like a furniture catalogue.")]
        [PropertyRange(0f, 1f)]
        public float density = 0.35f;

        [TitleGroup("Placement")]
        [Tooltip("How far corner props sit in from the tile corner, as a fraction of a cell — keeps them on the corner sidewalk quad.")]
        [PropertyRange(0.02f, 0.35f)]
        public float cornerInset = 0.1f;

        [TitleGroup("Placement")]
        [Tooltip("How far edge props sit in from the tile edge, as a fraction of a cell — keeps them on the sidewalk strip, off the lane.")]
        [PropertyRange(0.02f, 0.35f)]
        public float edgeInset = 0.08f;

        [TitleGroup("Impact")]
        [Tooltip("Padding of the wake trigger around each prop. Props lighter than the incoming player car flip dynamic when the car gets this close, so the contact itself is resolved by mass ratio and a cone never brakes the car. Must exceed the distance the car covers in one physics step at top speed (0.02 s × 50 m/s = 1 m).")]
        [PropertyRange(0.5f, 8f), SuffixLabel("m", true)]
        public float wakeDistance = 2.5f;

        [TitleGroup("Impact")]
        [Tooltip("Effective momentum (kg) the player's car hands a prop on contact: velocity change = car speed × this ÷ prop mass. 150 kg means a 300 kg light post picks up half the car's speed while a 3000 kg barrier barely moves.")]
        [PropertyRange(0f, 2000f), SuffixLabel("kg", true)]
        public float impactMomentum = 150f;

        [Tooltip("Caps how fast a featherweight prop may launch, as a multiple of the car's speed at impact — without it a 2 kg cone would leave at hundreds of m/s.")]
        [TitleGroup("Impact")]
        [PropertyRange(0.5f, 4f)]
        public float maxLaunchFactor = 2f;

        [TitleGroup("Impact")]
        [Tooltip("Upward bias mixed into the impulse so light props hop into the air instead of skidding flat along the ground.")]
        [PropertyRange(0f, 1f)]
        public float impactUpBias = 0.25f;

        // ---------------------------------------------------------- explosive
        [TitleGroup("Explosive")]
        [Tooltip("Normalized damage dealt to every IDamageable caught in the blast — 1 is a full NPC health bar (an outright kill). The player's receiver scales it down by their plating before it hits the corruption meter; another barrel detonates at any amount.")]
        [PropertyRange(0f, 1f)]
        public float blastDamage = 1f;

        [TitleGroup("Explosive")]
        [Tooltip("Everything inside this radius is caught: damageables take the damage above, loose props are thrown.")]
        [PropertyRange(1f, 30f), SuffixLabel("m", true)]
        public float blastRadius = 7f;

        [TitleGroup("Explosive")]
        [Tooltip("Impulse handed to a body at the centre of the blast, falling off to nothing at the radius.")]
        [PropertyRange(0f, 5000f)]
        public float blastForce = 900f;

        [TitleGroup("Explosive")]
        [Tooltip("Metres the blast's origin is sunk below itself when throwing bodies — the lower it sits, the more the blast lifts rather than shoves.")]
        [PropertyRange(0f, 5f), SuffixLabel("m", true)]
        public float blastUpModifier = 1.2f;

        [TitleGroup("Explosive")]
        [Tooltip("Contact slower than this is a nudge, not a detonation — traffic brushing a barrel in the gutter must not level the street.")]
        [PropertyRange(0.5f, 40f), SuffixLabel("m/s", true)]
        public float detonationSpeed = 3f;

        [TitleGroup("Explosive")]
        [Tooltip("Size of the fireball, in metres. Independent of the blast radius so the look and the damage can be tuned apart.")]
        [PropertyRange(0.5f, 20f), SuffixLabel("m", true)]
        public float explosionScale = 6f;

        [TitleGroup("Explosive")]
        [Tooltip("How long a fireball billboard lives.")]
        [PropertyRange(0.1f, 4f), SuffixLabel("s", true)]
        public float explosionLifetime = 0.9f;

        [TitleGroup("Explosive")]
        [Tooltip("Billboards in one blast.")]
        [PropertyRange(1, 40)]
        public int explosionParticles = 14;

        [TitleGroup("Explosive")]
        [Tooltip("Fireball sprites — one is picked at random per blast, so no two barrels look alike. Filled from 02.Art/05.Particles/SmokeAndExplosions/Explosion by the decoration-set builder.")]
        public List<Texture2D> explosionTextures = new();

        [TableList(AlwaysExpanded = true)]
        public List<DecorationDefinition> decorations = new();
    }
}
