using System;
using System.Collections.Generic;
using System.Linq;

namespace 六合分析软件
{
    public static class HistoricalSimilarityService
    {
        private static readonly string[] Zodiacs =
        {
            "鼠", "牛", "虎", "兔", "龙", "蛇",
            "马", "羊", "猴", "鸡", "狗", "猪"
        };

        public static HistoricalSimilarityResult Analyze(List<DatabaseHelper.HistoryRecord> newestFirstHistory, int maxMatches = 100)
        {
            var result = new HistoricalSimilarityResult();
            foreach (string zodiac in Zodiacs)
            {
                result.SimilarityScores[zodiac] = 0;
                result.SimilarAppearCounts[zodiac] = 0;
            }

            if (newestFirstHistory.Count < 80) return result;

            var oldToNew = newestFirstHistory.AsEnumerable().Reverse().ToList();
            var currentFeatures = Zodiacs.ToDictionary(z => z, z => BuildFeatures(newestFirstHistory, z));
            var candidates = new List<(int nextIndex, double similarity, string actual)>();

            for (int nextIndex = 60; nextIndex < oldToNew.Count; nextIndex++)
            {
                var priorNewestFirst = oldToNew.Take(nextIndex).Reverse().ToList();
                if (priorNewestFirst.Count < 60) continue;

                double similarity = 0;
                foreach (string zodiac in Zodiacs)
                {
                    var past = BuildFeatures(priorNewestFirst, zodiac);
                    similarity += CompareFeatures(currentFeatures[zodiac], past);
                }

                similarity /= Zodiacs.Length;
                candidates.Add((nextIndex, similarity, oldToNew[nextIndex].SpecialZodiac));
            }

            foreach (var item in candidates.OrderByDescending(c => c.similarity).Take(maxMatches))
            {
                if (!result.SimilarityScores.ContainsKey(item.actual)) continue;
                result.MatchedCycles++;
                result.SimilarAppearCounts[item.actual]++;
                result.SimilarityScores[item.actual] += item.similarity;
            }

            if (result.MatchedCycles == 0) return result;

            double maxScore = result.SimilarityScores.Values.DefaultIfEmpty(0).Max();
            foreach (string zodiac in Zodiacs)
            {
                double countScore = result.SimilarAppearCounts[zodiac] * 100.0 / result.MatchedCycles;
                double weightedScore = maxScore > 0 ? result.SimilarityScores[zodiac] / maxScore * 100 : 0;
                result.SimilarityScores[zodiac] = countScore * 0.65 + weightedScore * 0.35;
            }

            return result;
        }

        private static SimilarityFeatures BuildFeatures(List<DatabaseHelper.HistoryRecord> newestFirstHistory, string zodiac)
        {
            int total = newestFirstHistory.Count;
            int recent5 = newestFirstHistory.Take(Math.Min(5, total)).Count(h => h.SpecialZodiac == zodiac);
            int recent10 = newestFirstHistory.Take(Math.Min(10, total)).Count(h => h.SpecialZodiac == zodiac);
            int recent30 = newestFirstHistory.Take(Math.Min(30, total)).Count(h => h.SpecialZodiac == zodiac);
            int previous30 = newestFirstHistory.Skip(30).Take(30).Count(h => h.SpecialZodiac == zodiac);
            int omission = newestFirstHistory.TakeWhile(h => h.SpecialZodiac != zodiac).Count();
            var intervals = GetIntervals(newestFirstHistory, zodiac);
            double avgInterval = intervals.Count > 0 ? intervals.Average() : 12;
            double hotCold = recent30 - previous30;
            int adjacentChanges = 0;
            for (int i = 0; i < Math.Min(20, newestFirstHistory.Count - 1); i++)
            {
                if (newestFirstHistory[i].SpecialZodiac == zodiac && newestFirstHistory[i + 1].SpecialZodiac != zodiac)
                    adjacentChanges++;
            }

            return new SimilarityFeatures
            {
                Recent5 = recent5,
                Recent10 = recent10,
                Recent30 = recent30,
                Omission = omission,
                OmissionRatio = omission / Math.Max(1, avgInterval),
                HotCold = hotCold,
                Consecutive = recent5,
                AdjacentChanges = adjacentChanges
            };
        }

        private static double CompareFeatures(SimilarityFeatures current, SimilarityFeatures past)
        {
            double score = 0;
            score += Similarity(current.Recent5, past.Recent5, 5) * 0.15;
            score += Similarity(current.Recent10, past.Recent10, 10) * 0.15;
            score += Similarity(current.Recent30, past.Recent30, 30) * 0.15;
            score += Similarity(current.OmissionRatio, past.OmissionRatio, 3) * 0.25;
            score += Similarity(current.HotCold, past.HotCold, 8) * 0.15;
            score += Similarity(current.Consecutive, past.Consecutive, 5) * 0.08;
            score += Similarity(current.AdjacentChanges, past.AdjacentChanges, 10) * 0.07;
            return score;
        }

        private static double Similarity(double a, double b, double scale)
        {
            return Math.Max(0, 100 - Math.Abs(a - b) / Math.Max(1, scale) * 100);
        }

        private static List<int> GetIntervals(List<DatabaseHelper.HistoryRecord> newestFirstHistory, string zodiac)
        {
            var intervals = new List<int>();
            int last = -1;
            for (int i = 0; i < newestFirstHistory.Count; i++)
            {
                if (newestFirstHistory[i].SpecialZodiac != zodiac) continue;
                if (last >= 0) intervals.Add(i - last);
                last = i;
            }
            return intervals;
        }

        private class SimilarityFeatures
        {
            public int Recent5 { get; set; }
            public int Recent10 { get; set; }
            public int Recent30 { get; set; }
            public int Omission { get; set; }
            public double OmissionRatio { get; set; }
            public double HotCold { get; set; }
            public int Consecutive { get; set; }
            public int AdjacentChanges { get; set; }
        }
    }
}
