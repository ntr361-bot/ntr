using System;
using System.Collections.Generic;
using System.Linq;

namespace 六合分析软件
{
    public static class WeightOptimizationService
    {
        public enum OptimizationStrategy
        {
            Aggressive,
            Balanced,
            Stable
        }

        private static readonly string[] Zodiacs =
        {
            "鼠", "牛", "虎", "兔", "龙", "蛇",
            "马", "羊", "猴", "鸡", "狗", "猪"
        };

        public static OptimizedWeightResult FindBestWeights(int trainPeriods = 300, int testPeriods = 50)
        {
            var history = GetValidHistoryOldToNew(trainPeriods + testPeriods);
            return FindBestWeights(history, trainPeriods, testPeriods);
        }

        public static OptimizedWeightResult FindBestWeights(
            List<DatabaseHelper.HistoryRecord> oldToNewHistory,
            int trainPeriods,
            int testPeriods)
        {
            var training = oldToNewHistory.Take(trainPeriods).ToList();
            return FindBestWeightsFromTrainingData(training, Math.Min(100, Math.Max(20, trainPeriods - 1)));
        }

        public static OptimizedWeightResult FindBestWeightsFromTrainingData(
            List<DatabaseHelper.HistoryRecord> trainingHistoryOldToNew,
            int minimumTrainPeriods = 100)
        {
            return FindBestWeightsFromTrainingData(trainingHistoryOldToNew, minimumTrainPeriods, OptimizationStrategy.Balanced);
        }

        public static OptimizedWeightResult FindBestWeightsFromTrainingData(
            List<DatabaseHelper.HistoryRecord> trainingHistoryOldToNew,
            int minimumTrainPeriods,
            OptimizationStrategy strategy)
        {
            var best = new OptimizedWeightResult
            {
                ModelName = $"Ensemble V6.2 {GetStrategyName(strategy)}",
                StrategyName = GetStrategyName(strategy),
                StabilityGrade = "数据不足"
            };

            if (trainingHistoryOldToNew.Count <= minimumTrainPeriods)
                return best;

            int validationPeriods = trainingHistoryOldToNew.Count - minimumTrainPeriods;
            int tested = 0;

            foreach (var weights in GenerateWeightCombinations())
            {
                tested++;
                var score = EvaluateWeights(trainingHistoryOldToNew, minimumTrainPeriods, validationPeriods, weights);
                double optimizationScore = CalculateOptimizationScore(score, strategy);

                if (optimizationScore > best.OptimizationScore || best.TotalTests == 0)
                {
                    best = CopyToOptimized(score, weights, tested, optimizationScore, strategy);
                }
            }

            best.TestedCombinations = tested;
            return best;
        }

        public static List<OptimizedWeightResult> FindStrategyWeightsFromTrainingData(
            List<DatabaseHelper.HistoryRecord> trainingHistoryOldToNew,
            int minimumTrainPeriods = 100)
        {
            return new List<OptimizedWeightResult>
            {
                FindBestWeightsFromTrainingData(trainingHistoryOldToNew, minimumTrainPeriods, OptimizationStrategy.Aggressive),
                FindBestWeightsFromTrainingData(trainingHistoryOldToNew, minimumTrainPeriods, OptimizationStrategy.Balanced),
                FindBestWeightsFromTrainingData(trainingHistoryOldToNew, minimumTrainPeriods, OptimizationStrategy.Stable)
            };
        }

        public static ModelScoreResult EvaluateWeights(
            List<DatabaseHelper.HistoryRecord> oldToNewHistory,
            int trainPeriods,
            int testPeriods,
            ZodiacPredictEngineV2.WeightConfig weights)
        {
            return EvaluatePredictor(
                oldToNewHistory,
                trainPeriods,
                testPeriods,
                "Ensemble",
                training => RankByWeights(training, weights));
        }

        public static ModelScoreResult EvaluatePredictor(
            List<DatabaseHelper.HistoryRecord> oldToNewHistory,
            int trainPeriods,
            int testPeriods,
            string modelName,
            Func<List<DatabaseHelper.HistoryRecord>, List<(string zodiac, double score)>> predictor)
        {
            var result = new ModelScoreResult { ModelName = modelName };
            if (oldToNewHistory.Count < trainPeriods + 1) return FinalizeScore(result);

            int end = Math.Min(oldToNewHistory.Count, trainPeriods + testPeriods);
            int consecutiveHit = 0;
            int consecutiveMiss = 0;

            for (int i = trainPeriods; i < end; i++)
            {
                var training = oldToNewHistory.Take(i).Reverse().ToList();
                var actual = oldToNewHistory[i].SpecialZodiac;
                var ranked = predictor(training);
                var top3 = ranked.Take(3).Select(x => x.zodiac).ToList();
                var top6 = ranked.Take(6).Select(x => x.zodiac).ToList();
                bool top1Hit = top3.Count > 0 && top3[0] == actual;
                bool top3Hit = top3.Contains(actual);
                bool top6Hit = top6.Contains(actual);

                result.TotalTests++;
                if (top1Hit) result.Top1Hits++;
                if (top3Hit)
                {
                    result.Top3Hits++;
                    consecutiveHit++;
                    consecutiveMiss = 0;
                }
                else
                {
                    consecutiveMiss++;
                    consecutiveHit = 0;
                }

                if (top6Hit) result.Top6Hits++;
                result.MaxConsecutiveHits = Math.Max(result.MaxConsecutiveHits, consecutiveHit);
                result.MaxConsecutiveMisses = Math.Max(result.MaxConsecutiveMisses, consecutiveMiss);
                result.Records.Add(new BacktestPredictionRecord
                {
                    Period = oldToNewHistory[i].Period,
                    ActualZodiac = actual,
                    Top3 = top3,
                    Top6 = top6,
                    Top3Hit = top3Hit,
                    Top6Hit = top6Hit
                });
            }

            return FinalizeScore(result);
        }

        public static List<(string zodiac, double score)> RankByWeights(
            List<DatabaseHelper.HistoryRecord> newestFirstHistory,
            ZodiacPredictEngineV2.WeightConfig weights)
        {
            return Zodiacs
                .Select(z => (zodiac: z, score: CalculateWeightedScore(newestFirstHistory, z, weights)))
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.zodiac)
                .ToList();
        }

        public static List<ZodiacPredictEngineV2.WeightConfig> GenerateWeightCombinations()
        {
            var values = Enumerable.Range(2, 9).Select(i => i / 20.0).ToArray();
            var list = new List<ZodiacPredictEngineV2.WeightConfig>();

            foreach (double frequency in values)
            foreach (double trend in values)
            foreach (double omission in values)
            foreach (double pattern in values)
            foreach (double momentum in values)
            {
                double sum = frequency + trend + omission + pattern + momentum;
                if (Math.Abs(sum - 1.0) > 0.0001) continue;

                list.Add(new ZodiacPredictEngineV2.WeightConfig
                {
                    FrequencyWeight = frequency,
                    RecentTrendWeight = trend,
                    OmissionWeight = omission,
                    HotColdWeight = momentum,
                    PeriodPatternWeight = pattern,
                    ConsecutiveWeight = 0
                });
            }

            return list;
        }

        internal static List<DatabaseHelper.HistoryRecord> GetValidHistoryOldToNew(int periods)
        {
            return DatabaseHelper.GetLatestHistory(periods)
                .Where(r => !string.IsNullOrWhiteSpace(r.SpecialZodiac))
                .Reverse()
                .ToList();
        }

        internal static ModelScoreResult FinalizeScore(ModelScoreResult result)
        {
            result.Top3HitRate = result.TotalTests > 0 ? (double)result.Top3Hits / result.TotalTests * 100 : 0;
            result.Top6HitRate = result.TotalTests > 0 ? (double)result.Top6Hits / result.TotalTests * 100 : 0;
            result.StabilityScore = CalculateStabilityScore(result);
            result.CombinedScore = CalculateOptimizationScore(result, OptimizationStrategy.Balanced);
            result.StabilityGrade = ToGrade(result.StabilityScore);
            return result;
        }

        internal static double CalculateStabilityScore(ModelScoreResult result)
        {
            if (result.TotalTests == 0) return 0;

            double missScore = Math.Max(0, 100 - result.MaxConsecutiveMisses * 7);
            double hitScore = Math.Min(100, result.MaxConsecutiveHits * 20);
            double volatilityScore = CalculateVolatilityScore(result.Records);

            return missScore * 0.45 + hitScore * 0.25 + volatilityScore * 0.30;
        }

        public static double CalculateOptimizationScore(ModelScoreResult result, OptimizationStrategy strategy)
        {
            return strategy switch
            {
                OptimizationStrategy.Aggressive =>
                    result.Top1HitRate * 0.55 +
                    result.Top3HitRate * 0.30 +
                    result.Top6HitRate * 0.10 +
                    result.StabilityScore * 0.05,

                OptimizationStrategy.Stable =>
                    result.Top1HitRate * 0.15 +
                    result.Top3HitRate * 0.20 +
                    result.Top6HitRate * 0.25 +
                    result.StabilityScore * 0.40,

                _ =>
                    result.Top1HitRate * 0.40 +
                    result.Top3HitRate * 0.30 +
                    result.Top6HitRate * 0.20 +
                    result.StabilityScore * 0.10
            };
        }

        public static string GetStrategyName(OptimizationStrategy strategy)
        {
            return strategy switch
            {
                OptimizationStrategy.Aggressive => "攻击型权重",
                OptimizationStrategy.Stable => "稳健型权重",
                _ => "均衡型权重"
            };
        }

        internal static string ToGrade(double score)
        {
            if (score >= 80) return "A";
            if (score >= 70) return "B";
            if (score >= 60) return "C";
            return "D";
        }

        private static OptimizedWeightResult CopyToOptimized(
            ModelScoreResult score,
            ZodiacPredictEngineV2.WeightConfig weights,
            int tested,
            double optimizationScore,
            OptimizationStrategy strategy)
        {
            return new OptimizedWeightResult
            {
                ModelName = $"Ensemble V6.2 {GetStrategyName(strategy)}",
                StrategyName = GetStrategyName(strategy),
                Weights = weights,
                TestedCombinations = tested,
                OptimizationScore = optimizationScore,
                TotalTests = score.TotalTests,
                Top1Hits = score.Top1Hits,
                Top3Hits = score.Top3Hits,
                Top6Hits = score.Top6Hits,
                MaxConsecutiveHits = score.MaxConsecutiveHits,
                MaxConsecutiveMisses = score.MaxConsecutiveMisses,
                StabilityScore = score.StabilityScore,
                CombinedScore = score.CombinedScore,
                StabilityGrade = score.StabilityGrade,
                Records = score.Records
            };
        }

        private static double CalculateVolatilityScore(List<BacktestPredictionRecord> records)
        {
            if (records.Count < 20) return 50;

            int chunkSize = Math.Max(10, records.Count / 4);
            var rates = new List<double>();
            for (int i = 0; i < records.Count; i += chunkSize)
            {
                var chunk = records.Skip(i).Take(chunkSize).ToList();
                if (chunk.Count == 0) continue;
                rates.Add(chunk.Count(r => r.Top3Hit) * 100.0 / chunk.Count);
            }

            if (rates.Count <= 1) return 50;

            double avg = rates.Average();
            double variance = rates.Select(r => Math.Pow(r - avg, 2)).Average();
            double stdDev = Math.Sqrt(variance);
            return Math.Max(0, 100 - stdDev * 2);
        }

        private static double CalculateWeightedScore(
            List<DatabaseHelper.HistoryRecord> newestFirstHistory,
            string zodiac,
            ZodiacPredictEngineV2.WeightConfig weights)
        {
            int total = newestFirstHistory.Count;
            if (total == 0) return 0;

            int appear = newestFirstHistory.Count(h => h.SpecialZodiac == zodiac);
            double frequencyScore = Math.Min(100, (double)appear / total * 12 * 100);

            int recent10 = newestFirstHistory.Take(Math.Min(10, total)).Count(h => h.SpecialZodiac == zodiac);
            int recent30 = newestFirstHistory.Take(Math.Min(30, total)).Count(h => h.SpecialZodiac == zodiac);
            int previous30 = newestFirstHistory.Skip(30).Take(30).Count(h => h.SpecialZodiac == zodiac);
            double trendScore = Math.Min(100, ((double)recent10 / Math.Min(10, total) * 0.55 +
                (double)recent30 / Math.Min(30, total) * 0.30 +
                Math.Max(0, recent30 - previous30) / 30.0 * 0.15) * 12 * 100);

            int currentOmission = newestFirstHistory.TakeWhile(h => h.SpecialZodiac != zodiac).Count();
            var intervals = GetIntervals(newestFirstHistory, zodiac);
            double avgInterval = intervals.Count > 0 ? intervals.Average() : 12;
            double omissionRatio = currentOmission / Math.Max(1, avgInterval);
            double omissionScore = omissionRatio >= 0.8 && omissionRatio <= 1.6
                ? 90
                : Math.Max(10, 90 - Math.Abs(omissionRatio - 1.2) * 40);

            double patternScore = 45;
            if (intervals.Count >= 3)
            {
                double avg = intervals.Average();
                double variance = intervals.Select(i => Math.Pow(i - avg, 2)).Average();
                double regularity = Math.Max(0, 100 - Math.Sqrt(variance) / Math.Max(1, avg) * 100);
                double match = Math.Max(0, 100 - Math.Abs(currentOmission - avg) / Math.Max(1, avg) * 100);
                patternScore = regularity * 0.45 + match * 0.55;
            }

            int recent10Momentum = newestFirstHistory.Take(Math.Min(10, total)).Count(h => h.SpecialZodiac == zodiac);
            int recent20Momentum = newestFirstHistory.Take(Math.Min(20, total)).Count(h => h.SpecialZodiac == zodiac);
            double momentumScore = recent20Momentum > 0
                ? Math.Max(0, Math.Min(100, (double)recent10Momentum / recent20Momentum * 100))
                : 50;

            return frequencyScore * weights.FrequencyWeight +
                trendScore * weights.RecentTrendWeight +
                omissionScore * weights.OmissionWeight +
                patternScore * (weights.PeriodPatternWeight + weights.ConsecutiveWeight) +
                momentumScore * weights.HotColdWeight;
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
    }
}
