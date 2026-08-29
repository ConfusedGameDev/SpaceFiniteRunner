using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Decoration
{
    /// <summary>
    /// A <see cref="DecorationSet"/> flavoured for green space: the pool a
    /// district's park lots draw from (trees, bushes, rocks, fences) plus the
    /// ground and path tiles that turn a building lot into a park. Inheriting
    /// the decoration set means every nature prop rides the same
    /// <see cref="DecorationProp"/> impact contract — masses alone decide
    /// whether a bush flies or a tree stops the car, no new physics code.
    /// Build from the Kenney NaturePack with
    /// Tools → Police Escape → Create Kenney Nature Sets.
    /// </summary>
    [CreateAssetMenu(fileName = "NatureSet", menuName = "PoliceEscape/Nature Set")]
    public class NatureSet : DecorationSet
    {
        [TitleGroup("Park ground")]
        [Tooltip("Tile stamped on every cell of a park lot (grass slab). Empty leaves the block's bare ground showing.")]
        public GameObject groundTilePrefab;

        [TitleGroup("Park ground")]
        [Tooltip("Tile used for the walking path run across larger park lots. Empty = no paths.")]
        public GameObject pathTilePrefab;

        [TitleGroup("Park placement")]
        [Tooltip("Chance per interior cell of a park lot to host a prop from the LotInterior pool.")]
        [PropertyRange(0f, 1f)]
        public float interiorDensity = 0.35f;

        [TitleGroup("Park placement")]
        [Tooltip("Chance per lot-edge cell to host a prop from the LotPerimeter pool (fences, palms), facing outward.")]
        [PropertyRange(0f, 1f)]
        public float perimeterDensity = 0.5f;

        [TitleGroup("Park placement")]
        [Tooltip("Prop-free radius kept around the lot centre and any path, as a fraction of a cell — parks need somewhere to stand (or drive through).")]
        [PropertyRange(0f, 2f)]
        public float clearingRadius = 0.6f;
    }
}
