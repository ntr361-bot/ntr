using System;
using System.Collections.Generic;
using System.Linq;

namespace 六合分析软件
{
    public static class DynamicWeightService
    {
        private static readonly string[] Zodiacs =
        {
            "鼠", "牛", "虎", "兔", "龙", "蛇",
            "马", "羊", "猴", "鸡", "狗", "猪"
        };

        public static DynamicWeightResult AnalyzeWeights(List<DatabaseHelper.HistoryRecord> newestFirstHistory)
        {
            var result = new DynamicWeightResult
            {
                FrequencyWeight = 0.40,
                TrendWeight = 0.10,
                MissingWeight = 0.40,
                PatternWeight = 0.10,
                MomentumWeight = 0,
                SimilarityWeight = 0
            };

            if (newestFirstHistory.Count < 80)
                return result;

            var features = Zodiacs
                .Select(z => BuildFeatures(newestFirstHistory, z))
                .ToList();

            double missingSignal = Clamp01(features.Max(f => f.OmissionRatio) / 2.8);
            double trendSignal = Clamp01(features.Average(f => Math.Abs(f.Recent30 - f.Previous30)) / 3.0);
            double frequencySignal = Clamp01(StdDev(features.Select(f => f.LongRate)) / 3.2);
            double patternSignal = Clamp01(features.Average(f => f.PatternRegularity) / 100.0);
            double momentumSignal = Clamp01(features.Max(f => f.Recent5) / 3.0);
            double similaritySignal = CalculateSimilaritySignal(newestFirstHistory);

            var weights = new Dictionary<string, double>
            {
                ["Frequency"] = 0.40 + frequencySignal * 0.10,
                ["Trend"] = 0.10 + trendSignal * 0.18,
                ["Missing"] = 0.40 + missingSignal * 0.16,
                ["Pattern"] = 0.10 + patternSignal * 0.10,
                ["Momentum"] = momentumSignal * 0.12,
                ["Similarity"] = similaritySignal * 0.12
            };

            Normalize(weights);

            result.FrequencyWeight = weights["Frequency"];
            result.TrendWeight = weights["Trend"];
            result.MissingWeight = weights["Missing"];
            result.PatternWeight = weights["Pattern"];
            result.MomentumWeight = weights["Momentum"];
            result.SimilarityWeight = weights["Similarity"];
            result.MissingSignal = missingSignal;
            result.TrendSignal = trendSignal;
            result.SimilaritySignal = similaritySignal;
            return result;
        }

        public static List<(string zodiac, double score)> RankByDynamicWeights(
            List<DatabaseHelper.HistoryRecord> newestFirstHistory)
        {
            var dynamic = AnalyzeWeights(newestFirstHistory);
            var similarity = dynamic.SimilarityWeight > 0
                ? HistoricalSimilarityService.Analyze(newestFirstHistory)
                : new HistoricalSimilarityResult();

            var scores = new List<(string zodiac, double score)>();
            foreach (string zodiac in Zodiacs)
            {
                double score =
                    CalculateFrequencyScore(newestFirstHistory, zodiac) * dynamic.FrequencyWeight +
                    CalculateTrendScore(newestFirstHistory, zodiac) * dynamic.TrendWeight +
                    CalculateMissingScore(newestFirstHistory, zodiac) * dynamic.MissingWeight +
                    CalculatePatternScore(newestFirstHistory, zodiac) * dynamic.PatternWeight +
                    CalculateMomentumScore(newestFirstHistory, zodiac) * dynamic.MomentumWeight +
                    similarity.SimilarityScores.GetValueOrDefault(zodiac) * dynamic.SimilarityWeight;

                scores.Add((zodiac, score));
            }

            return scores.OrderByDescending(x => x.score).ThenBy(x => x.zodiac).ToList();
        }

        private static DynamicZodiacFeatures BuildFeatures(
            List<DatabaseHelper.HistoryRecord> newestFirstHistory,
            string zodiac)
        {
            int total = newestFirstHistory.Count;
            int recent5 = newestFirstHistory.Take(Math.Min(5, total)).Count(h => h.SpecialZodiac == zodiac);
            int recent30 = newestFirstHistory.Take(Math.Min(30, total)).Count(h => h.SpecialZodiac == zodiac);
            int previous30 = newestFirstHistory.Skip(30).Take(30).Count(h => h.SpecialZodiac == zodiac);
            int appear = newestFirstHistory.Count(h => h.SpecialZodiac == zodiac);
            int omission = newestFirstHistory.TakeWhile(h => h.SpecialZodiac != zodiac).Count();
            var intervals = GetIntervals(newestFirstHistory, zodiac);
            double avgInterval = intervals.Count > 0 ? intervals.Average() : 12;
            double regularity = 45;

            if (intervals.Count >= 3)
            {
                double avg = intervals.Average();
                double variance = intervals.Select(i => Math.Pow(i - avg, 2)).Average();
                regularity = Math.Max(0, 100 - Math.Sqrt(variance) / Math.Max(1, avg) * 100);
            }

            return new DynamicZodiacFeatures
            {
                Recent5 = recent5,
                Recent30 = recent30,
                Previous30 = previous30,
                LongRate = (double)appear / total * 100,
                OmissionRatio = omission / Math.Max(1, avgInterval),
                PatternRegularity = regularity
            };
        }

        private static double CalculateFrequencyScore(List<DatabaseHelper.HistoryRecord> history, string zodiac)
        {
            int total = history.Count;
            if (total == 0) return 0;
            int appear = history.Count(h => h.SpecialZodiac == zodiac);
            return Math.Min(100, (double)appear / total * 12 * 100);
        }

        private static double CalculateTrendScore(List<DatabaseHelper.HistoryRecord> history, string zodiac)
        {
            int total = history.Count;
            if (total == 0) return 0;
            int recent10 = history.Take(Math.Min(10, total)).Count(h => h.SpecialZodiac == zodiac);
            int recent30 = history.Take(Math.Min(30, total)).Count(h => h.SpecialZodiac == zodiac);
            int previous30 = history.Skip(30).Take(30).Count(h => h.SpecialZodiac == zodiac);

            return Math.Min(100, ((double)recent10 / Math.Min(10, total) * 0.55 +
                (double)recent30 / Math.Min(30, total) * 0.30 +
                Math.Max(0, recent30 - previous30) / 30.0 * 0.15) * 12 * 100);
        }

        private static double CalculateMissingScore(List<DatabaseHelper.HistoryRecord> history, string zodiac)
        {
            int omission = history.TakeWhile(h => h.SpecialZodiac != zodiac).Count();
            var intervals = GetIntervals(history, zodiac);
            double avgInterval = intervals.Count > 0 ? intervals.Average() : 12;
            double ratio = omission / Math.Max(1, avgInterval);

            return ratio >= 0.8 && ratio <= 1.6
                ? 90
                : Math.Max(10, 90 - Math.Abs(ratio - 1.2) * 40);
        }

        private static double CalculatePatternScore(List<DatabaseHelper.HistoryRecord> history, string zodiac)
        {
            int omission = history.TakeWhile(h => h.SpecialZodiac != zodiac).Count();
            var intervals = GetIntervals(history, zodiac);
            if (intervals.Count < 3) return 45;

            double avg = intervals.Average();
            double variance = intervals.Select(i => Math.Pow(i - avg, 2)).Average();
            double regularity = Math.Max(0, 100 - Math.Sqrt(variance) / Math.Max(1, avg) * 100);
            double match = Math.Max(0, 100 - Math.Abs(omission - avg) / Math.Max(1, avg) * 100);
            return regularity * 0.45 + match * 0.55;
        }

        private static double CalculateMomentumScore(List<DatabaseHelper.HistoryRecord> history, string zodiac)
        {
            int total = history.Count;
            int recent10 = history.Take(Math.Min(10, total)).Count(h => h.SpecialZodiac == zodiac);
            int recent20 = history.Take(Math.Min(20, total)).Count(h => h.SpecialZodiac == zodiac);
            return recent20 > 0 ? Math.Max(0, Math.Min(100, (double)recent10 / recent20 * 100)) : 50;
        }

        private static double CalculateSimilaritySignal(List<DatabaseHelper.HistoryRecord> newestFirstHistory)
        {
            var similarity = HistoricalSimilarityService.Analyze(newestFirstHistory);
            if (similarity.MatchedCycles <= 0) return 0;

            var ordered = similarity.SimilarityScores.Values.OrderByDescending(x => x).Take(2).ToList();
            if (ordered.Count < 2) return 0;

            return Clamp01((ordered[0] - ordered[1]) / 18.0);
        }

        private static void Normalize(Dictionary<string, double> weights)
        {
            double sum = weights.Values.Sum();
            if (sum <= 0) return;

            foreach (string key in weights.Keys.ToList())
                weights[key] /= sum;
        }

        private static double StdDev(IEnumerable<double> values)
        {
            var list = values.ToList();
            if (list.Count == 0) return 0;
            double avg = list.Average();
            return Math.Sqrt(list.Select(v => Math.Pow(v - avg, 2)).Average());
        }

        private static double Clamp01(double value)
        {
            return Math.Max(0, Math.Min(1, value));
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

        private class DynamicZodiacFeatures
        {
            public int Recent5 { get; set; }
            public int Recent30 { get; set; }
            public int Previous30 { get; set; }
            public double LongRate { get; set; }
            public double OmissionRatio { get; set; }
            public double PatternRegularity { get; set; }
        }
    }

    public class DynamicWeightResult
    {
        public double FrequencyWeight { get; set; }
        public double TrendWeight { get; set; }
        public double MissingWeight { get; set; }
        public double PatternWeight { get; set; }
        public double MomentumWeight { get; set; }
        public double SimilarityWeight { get; set; }
        public double MissingSignal { get; set; }
        public double TrendSignal { get; set; }
        public double SimilaritySignal { get; set; }
    }
}
