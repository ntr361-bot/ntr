using System;
using System.Collections.Generic;
using System.Linq;

namespace 六合分析软件
{
    public static class MissingReliabilityService
    {
        private const double BaseProbability = 1.0 / 12.0;

        public static MissingReliabilityResult Analyze(
            List<DatabaseHelper.HistoryRecord> newestFirstHistory,
            string zodiac,
            double originalMissingScore)
        {
            int omission = newestFirstHistory.TakeWhile(h => h.SpecialZodiac != zodiac).Count();
            var estimate = EstimateRecoveryProbability(newestFirstHistory, zodiac, omission);
            double reliability = Math.Min(1.0, estimate.probability / BaseProbability);

            return new MissingReliabilityResult
            {
                Zodiac = zodiac,
                OmissionLength = omission,
                EffectiveSamples = estimate.samples,
                RecoveryProbability = estimate.probability,
                ReliabilityScore = reliability,
                OriginalMissingScore = originalMissingScore,
                AdjustedMissingScore = originalMissingScore * reliability
            };
        }

        public static MissingReliabilityResult AnalyzeLength(
            List<DatabaseHelper.HistoryRecord> newestFirstHistory,
            int omissionLength)
        {
            var estimates = Zodiacs.Select(z => EstimateRecoveryProbability(
                newestFirstHistory, z, omissionLength)).ToList();
            double samples = estimates.Sum(x => x.samples);
            double probability = samples > 0
                ? estimates.Sum(x => x.probability * x.samples) / samples
                : BaseProbability;

            return new MissingReliabilityResult
            {
                OmissionLength = omissionLength,
                EffectiveSamples = samples,
                RecoveryProbability = probability,
                ReliabilityScore = Math.Min(1.0, probability / BaseProbability)
            };
        }

        public static List<(string zodiac, double score)> RankWithReliability(
            List<DatabaseHelper.HistoryRecord> newestFirstHistory)
        {
            // Reuse the exact V6 component calculations, replacing only MissingScore.
            var original = PredictionExplainService.Explain(newestFirstHistory, "", "").Contributions;
            return original.Select(item =>
            {
                var reliability = Analyze(newestFirstHistory, item.Zodiac, item.MissingScore);
                double score = item.FrequencyContribution + item.TrendContribution +
                    reliability.AdjustedMissingScore * 0.40 + item.PatternContribution +
                    item.MomentumContribution;
                return (zodiac: item.Zodiac, score);
            })
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.zodiac)
            .ToList();
        }

        private static (double probability, double samples) EstimateRecoveryProbability(
            List<DatabaseHelper.HistoryRecord> newestFirstHistory,
            string zodiac,
            int targetOmission)
        {
            var oldToNew = newestFirstHistory.AsEnumerable().Reverse().ToList();
            int bandwidth = Math.Max(1, Math.Min(4, targetOmission / 5 + 1));
            int omission = 0;
            double weightedSamples = 0;
            double weightedHits = 0;

            foreach (var record in oldToNew)
            {
                int distance = Math.Abs(omission - targetOmission);
                if (distance <= bandwidth)
                {
                    double weight = 1.0 - distance / (double)(bandwidth + 1);
                    weightedSamples += weight;
                    if (record.SpecialZodiac == zodiac) weightedHits += weight;
                }

                omission = record.SpecialZodiac == zodiac ? 0 : omission + 1;
            }

            // Twelve prior-equivalent samples keep sparse long omissions from overfitting.
            double probability = (weightedHits + 1.0) / (weightedSamples + 12.0);
            return (probability, weightedSamples);
        }

        private static readonly string[] Zodiacs =
        {
            "鼠", "牛", "虎", "兔", "龙", "蛇",
            "马", "羊", "猴", "鸡", "狗", "猪"
        };
    }
}
