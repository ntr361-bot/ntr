using System;
using System.Collections.Generic;
using System.Linq;

namespace 六合分析软件
{
    public static class ErrorAnalysisService
    {
        private static readonly string[] ErrorTypes =
        {
            "热门误判", "遗漏失效", "趋势反转", "模式失效"
        };

        public static ErrorAnalysisResult Analyze(List<PredictionExplainResult> predictions)
        {
            var result = new ErrorAnalysisResult
            {
                TotalPredictions = predictions.Count,
                Top3Failures = predictions.Count(x => !x.Top3Hit),
                FailureTypeCounts = ErrorTypes.ToDictionary(x => x, _ => 0)
            };

            foreach (var prediction in predictions.Where(x => !x.Top3Hit))
            {
                var actual = prediction.Contributions.First(x => x.Zodiac == prediction.ActualZodiac);
                var predicted = prediction.Contributions.Take(3).ToList();
                var gaps = new Dictionary<string, double>
                {
                    ["热门误判"] = predicted.Average(x => x.FrequencyContribution) - actual.FrequencyContribution,
                    ["遗漏失效"] = predicted.Average(x => x.MissingContribution) - actual.MissingContribution,
                    ["趋势反转"] = predicted.Average(x => x.TrendContribution) - actual.TrendContribution,
                    ["模式失效"] = predicted.Average(x => x.PatternContribution) - actual.PatternContribution
                };
                var primary = gaps.OrderByDescending(x => x.Value).ThenBy(x => x.Key).First();

                result.FailureTypeCounts[primary.Key]++;
                result.Details.Add(new PredictionErrorDetail
                {
                    Period = prediction.Period,
                    ActualZodiac = prediction.ActualZodiac,
                    PredictedTop3 = prediction.Top3,
                    ActualRank = actual.Rank,
                    ErrorType = primary.Key,
                    ContributionGap = primary.Value
                });
            }

            foreach (string type in ErrorTypes)
            {
                result.FailureTypeRates[type] = result.Top3Failures > 0
                    ? result.FailureTypeCounts[type] * 100.0 / result.Top3Failures
                    : 0;
            }
            return result;
        }
    }
}
