using UnityEngine;
using UnityEngine.UI;

namespace FiniteRunner
{
    /// <summary>
    /// Pre-run ship setup: the player distributes a limited pool of points
    /// across Max Speed / Acceleration / Handling / Weight, then launches.
    /// Applies the tuning as a runtime clone of the base ShipDefinition so
    /// the asset on disk is never modified.
    /// </summary>
    public class TuningScreen : MonoBehaviour
    {
        public enum Stat { MaxSpeed = 0, Acceleration = 1, Handling = 2, Weight = 3 }

        [SerializeField] ShipMotor motor;
        [SerializeField] ShipDefinition baseDefinition;

        [Header("Point budget")]
        [SerializeField, Min(1)] int totalPoints = 8;
        [SerializeField, Min(1)] int maxPointsPerStat = 5;

        [Header("Effect per point")]
        [SerializeField] float maxSpeedPerPoint = 10f;      // m/s on the cap
        [SerializeField] float accelerationPerPoint = 10f;  // pad blend rate
        [SerializeField] float lateralSpeedPerPoint = 4f;   // steering speed
        [Tooltip("Negative = each point makes the ship lighter, so pads (boosts AND brakes) hit harder.")]
        [SerializeField] float weightPerPoint = -0.08f;
        [SerializeField, Min(0.1f)] float minWeight = 0.4f;

        [Header("UI")]
        [SerializeField] GameObject panelRoot;
        [SerializeField] Text pointsLeftText;
        [SerializeField] Text[] statValueTexts = new Text[4];
        [SerializeField] Button[] plusButtons = new Button[4];
        [SerializeField] Button[] minusButtons = new Button[4];
        [SerializeField] Button startButton;

        readonly int[] points = new int[4];
        ShipDefinition tunedDefinition;

        int PointsSpent { get { int s = 0; foreach (int p in points) s += p; return s; } }
        int PointsLeft => totalPoints - PointsSpent;

        void Awake()
        {
            for (int i = 0; i < 4; i++)
            {
                int stat = i; // capture
                if (plusButtons[i] != null) plusButtons[i].onClick.AddListener(() => Add(stat, +1));
                if (minusButtons[i] != null) minusButtons[i].onClick.AddListener(() => Add(stat, -1));
            }
            if (startButton != null) startButton.onClick.AddListener(StartRun);
        }

        void Start() => Show();

        public void Show()
        {
            if (motor != null) motor.Paused = true;
            if (panelRoot != null) panelRoot.SetActive(true);
            Refresh();
        }

        void Add(int stat, int delta)
        {
            int next = points[stat] + delta;
            if (next < 0 || next > maxPointsPerStat) return;
            if (delta > 0 && PointsLeft <= 0) return;
            points[stat] = next;
            Refresh();
        }

        void Refresh()
        {
            if (pointsLeftText != null) pointsLeftText.text = $"POINTS LEFT  {PointsLeft}";
            for (int i = 0; i < 4; i++)
                if (statValueTexts[i] != null)
                    statValueTexts[i].text = $"{points[i]}/{maxPointsPerStat}";
        }

        public void StartRun()
        {
            if (motor == null || baseDefinition == null) return;

            if (tunedDefinition == null) tunedDefinition = Instantiate(baseDefinition);

            tunedDefinition.maxSpeed = baseDefinition.maxSpeed + points[(int)Stat.MaxSpeed] * maxSpeedPerPoint;
            tunedDefinition.acceleration = baseDefinition.acceleration + points[(int)Stat.Acceleration] * accelerationPerPoint;
            tunedDefinition.lateralSpeed = baseDefinition.lateralSpeed + points[(int)Stat.Handling] * lateralSpeedPerPoint;
            tunedDefinition.weight = Mathf.Max(minWeight, baseDefinition.weight + points[(int)Stat.Weight] * weightPerPoint);

            motor.SetDefinition(tunedDefinition);
            motor.Launch();
            motor.Paused = false;
            if (panelRoot != null) panelRoot.SetActive(false);
        }
    }
}
