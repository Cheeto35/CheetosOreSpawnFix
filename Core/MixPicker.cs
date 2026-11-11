using System;
using System.Collections.Generic;

namespace CheetosOreSpawnFix
{
    // Chooses which node type to spawn so a level's mix tracks its target shares.
    public static class MixPicker
    {
        // Weights to shares that add up to 1. Negative weights count as 0; all-zero input gives all zeros.
        public static double[] Normalize(double[] weights)
        {
            double[] r = new double[weights.Length];
            double sum = 0;
            for (int i = 0; i < weights.Length; i++) { r[i] = weights[i] > 0 ? weights[i] : 0; sum += r[i]; }
            if (sum <= 0) return new double[weights.Length];
            for (int i = 0; i < r.Length; i++) r[i] /= sum;
            return r;
        }

        // The type furthest below its share once one more node is added: max of shares[i]*(total+1) - counts[i].
        // Only types with a share above 0 are eligible. Exact ties pick among the tied types by tieBreak in [0,1).
        // Returns -1 when no type is eligible.
        public static int PickMostUnder(double[] shares, int[] counts, double tieBreak)
        {
            int total = 0;
            foreach (int c in counts) total += c;
            double best = double.NegativeInfinity;
            List<int> tied = new List<int>();
            for (int i = 0; i < shares.Length; i++)
            {
                if (shares[i] <= 0) continue;
                double deficit = shares[i] * (total + 1) - (i < counts.Length ? counts[i] : 0);
                if (deficit > best + 1e-9) { best = deficit; tied.Clear(); tied.Add(i); }
                else if (Math.Abs(deficit - best) <= 1e-9) tied.Add(i);
            }
            if (tied.Count == 0) return -1;
            int k = (int)(Math.Max(0, Math.Min(0.999999, tieBreak)) * tied.Count);
            return tied[k];
        }
    }
}
