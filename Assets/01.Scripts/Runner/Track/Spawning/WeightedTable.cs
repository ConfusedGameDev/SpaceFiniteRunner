using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>A spawn-table entry with a probability share, so every table rebalances through one rule.</summary>
    public interface IWeightedEntry
    {
        float Probability { get; set; }
    }

    /// <summary>
    /// The two rules every weighted table shares — the feature table and the
    /// speed-orb tiers: a draw normalized by the table's sum (so it stays
    /// correct mid-edit), and the inspector's 100% rebalance (the slider just
    /// moved keeps its value, the others scale into the remainder).
    /// </summary>
    public static class WeightedTable
    {
        /// <summary>
        /// Weighted draw off a 0..1 roll. An excluded entry weighs nothing and
        /// is skipped; null when nothing else has weight.
        /// </summary>
        public static IWeightedEntry Pick(IWeightedEntry[] table, float roll01, IWeightedEntry exclude = null)
        {
            if (table == null || table.Length == 0) return null;
            float total = 0f;
            IWeightedEntry last = null;
            foreach (var e in table)
            {
                if (e == exclude) continue;
                total += e.Probability;
                last = e;
            }
            if (last == null) return null;
            if (total <= 0f) return exclude == null ? table[0] : last;

            float roll = roll01 * total;
            foreach (var e in table)
            {
                if (e == exclude) continue;
                roll -= e.Probability;
                if (roll <= 0f) return e;
            }
            return last;
        }

        /// <summary>
        /// Keeps a table at 100%: whichever entry changed since <paramref name="last"/>
        /// keeps its value and the others rebalance proportionally; an added or
        /// removed entry (or a first touch) scales everything to 100.
        /// </summary>
        public static void Normalize(IWeightedEntry[] table, ref float[] last)
        {
            if (table == null || table.Length == 0) { last = null; return; }

            if (table.Length == 1)
            {
                table[0].Probability = 100f;
            }
            else if (last != null && last.Length == table.Length)
            {
                int changed = -1;
                for (int i = 0; i < table.Length; i++)
                    if (!Mathf.Approximately(table[i].Probability, last[i])) { changed = i; break; }

                if (changed >= 0)
                {
                    float kept = Mathf.Clamp(table[changed].Probability, 0f, 100f);
                    table[changed].Probability = kept;

                    float othersSum = 0f;
                    for (int i = 0; i < table.Length; i++)
                        if (i != changed) othersSum += table[i].Probability;

                    float remainder = 100f - kept;
                    for (int i = 0; i < table.Length; i++)
                    {
                        if (i == changed) continue;
                        table[i].Probability = othersSum > 0f
                            ? table[i].Probability * remainder / othersSum
                            : remainder / (table.Length - 1);
                    }
                }
            }
            else
            {
                float total = 0f;
                foreach (var e in table) total += e.Probability;
                for (int i = 0; i < table.Length; i++)
                    table[i].Probability = total > 0f
                        ? table[i].Probability * 100f / total
                        : 100f / table.Length;
            }

            last = new float[table.Length];
            for (int i = 0; i < table.Length; i++) last[i] = table[i].Probability;
        }
    }
}
