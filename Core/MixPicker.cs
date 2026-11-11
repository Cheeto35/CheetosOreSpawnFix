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

        // A two-step roll as one set of shares: outer[j] is the weight of sub-table j, inner[j][i] the weight of type i
        // in sub-table j. A sub-table whose weights are all zero can't produce anything and is left out.
        public static double[] Mixture(double[] outer, double[][] inner)
        {
            int n = 0;
            foreach (double[] t in inner) n = Math.Max(n, t.Length);
            double[] o = new double[outer.Length];
            for (int j = 0; j < outer.Length && j < inner.Length; j++)
            {
                double s = 0;
                foreach (double w in inner[j]) if (w > 0) s += w;
                o[j] = s > 0 ? outer[j] : 0;
            }
            o = Normalize(o);
            double[] r = new double[n];
            for (int j = 0; j < o.Length; j++)
            {
                if (o[j] <= 0) continue;
                double[] t = Normalize(inner[j]);
                for (int i = 0; i < t.Length; i++) r[i] += o[j] * t[i];
            }
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
