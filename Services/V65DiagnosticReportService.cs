using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace 六合分析软件
{
    public static class V65DiagnosticReportService
    {
        private static readonly ZodiacPredictEngineV2.WeightConfig V6Weights = new ZodiacPredictEngineV2.WeightConfig
        {
            FrequencyWeight = 0.40,
            RecentTrendWeight = 0.10,
            OmissionWeight = 0.40,
            HotColdWeight = 0,
            PeriodPatternWeight = 0.10,
            ConsecutiveWeight = 0
        };

        public static string GenerateReport(int totalPeriods = 500)
        {
            var history = WeightOptimizationService.GetValidHistoryOldToNew(totalPeriods);
            var sb = new StringBuilder();
            sb.AppendLine("六合分析软件 v6.5 V6错误诊断报告");
            sb.AppendLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"数据库：{DatabaseHelper.DatabasePath}");
            sb.AppendLine($"真实历史数据：{history.Count}期");
            sb.AppendLine("说明：本模块只解释和统计V6预测，不改变V6权重、评分、排序或历史数据。");
            sb.AppendLine();

            if (history.Count < totalPeriods || totalPeriods < 500)
            {
                sb.AppendLine("数据不足：真实500期诊断要求读取完整500期记录。");
                return sb.ToString();
            }

            const int trainPeriods = 300;
            const int testPeriods = 200;
            var baseline = WeightOptimizationService.EvaluateWeights(history, trainPeriods, testPeriods, V6Weights);
            var explanations = BuildExplanations(history, trainPeriods, testPeriods);
            var errors = ErrorAnalysisService.Analyze(explanations);
            bool consistent = IsConsistentWithBaseline(baseline, explanations);

            sb.AppendLine("验证范围：");
            sb.AppendLine("数据：500期");
            sb.AppendLine("起始训练：300期");
            sb.AppendLine("逐期测试：200期（每期只使用当期之前的数据）");
            sb.AppendLine();
            sb.AppendLine("V6原始结果：");
            sb.AppendLine($"Top1：{baseline.Top1HitRate:F2}% ({baseline.Top1Hits}/{baseline.TotalTests})");
            sb.AppendLine($"Top3：{baseline.Top3HitRate:F2}% ({baseline.Top3Hits}/{baseline.TotalTests})");
            sb.AppendLine($"Top6：{baseline.Top6HitRate:F2}% ({baseline.Top6Hits}/{baseline.TotalTests})");
            sb.AppendLine($"最大连续命中：{baseline.MaxConsecutiveHits}");
            sb.AppendLine($"最大连续失败：{baseline.MaxConsecutiveMisses}");
            sb.AppendLine($"综合评分：{baseline.CombinedScore:F2}");
            sb.AppendLine($"诊断一致性校验：{(consistent ? "通过（200期排序与V6基准完全一致）" : "失败（诊断结果与V6基准不一致）")}");
            sb.AppendLine();

            sb.AppendLine("Top3失败原因统计：");
            sb.AppendLine($"失败总数：{errors.Top3Failures}/{errors.TotalPredictions}");
            foreach (string type in new[] { "热门误判", "遗漏失效", "趋势反转", "模式失效" })
                sb.AppendLine($"{type}：{errors.FailureTypeCounts[type]}次，占失败 {errors.FailureTypeRates[type]:F2}%");
            sb.AppendLine("归因口径：比较预测Top3平均贡献与实际生肖贡献，差值最大的模型作为该次失败主因；每次失败只统计一次。");
            sb.AppendLine();

            sb.AppendLine("各模型平均贡献（全部200期Top3候选）：");
            AppendAverageContributions(sb, explanations.SelectMany(x => x.Contributions.Take(3)).ToList());
            sb.AppendLine();

            sb.AppendLine("贡献差距最大的失败样本（前10条）：");
            foreach (var detail in errors.Details.OrderByDescending(x => x.ContributionGap).Take(10))
            {
                sb.AppendLine($"{detail.Period}期：预测 {string.Join("、", detail.PredictedTop3)}，实际 {detail.ActualZodiac}（第{detail.ActualRank}名），主因 {detail.ErrorType}，贡献差 {detail.ContributionGap:F2}");
            }
            sb.AppendLine();
            sb.AppendLine("结论：v6.5仅增加分析诊断能力，V6继续作为默认预测模型，预测结果未改变。");
            return sb.ToString();
        }

        private static List<PredictionExplainResult> BuildExplanations(
            List<DatabaseHelper.HistoryRecord> oldToNewHistory,
            int trainPeriods,
            int testPeriods)
        {
            var results = new List<PredictionExplainResult>();
            int end = Math.Min(oldToNewHistory.Count, trainPeriods + testPeriods);
            for (int i = trainPeriods; i < end; i++)
            {
                var priorHistory = oldToNewHistory.Take(i).Reverse().ToList();
                var actual = oldToNewHistory[i];
                results.Add(PredictionExplainService.Explain(priorHistory, actual.Period, actual.SpecialZodiac));
            }
            return results;
        }

        private static bool IsConsistentWithBaseline(
            ModelScoreResult baseline,
            List<PredictionExplainResult> explanations)
        {
            if (baseline.Records.Count != explanations.Count) return false;
            for (int i = 0; i < explanations.Count; i++)
            {
                var expected = baseline.Records[i];
                var actual = explanations[i];
                if (expected.Period != actual.Period ||
                    !expected.Top3.SequenceEqual(actual.Top3) ||
                    !expected.Top6.SequenceEqual(actual.Top6))
                    return false;
            }
            return true;
        }

        private static void AppendAverageContributions(
            StringBuilder sb,
            List<PredictionContribution> contributions)
        {
            if (contributions.Count == 0) return;
            sb.AppendLine($"频率：{contributions.Average(x => x.FrequencyContribution):F2}");
            sb.AppendLine($"趋势：{contributions.Average(x => x.TrendContribution):F2}");
            sb.AppendLine($"遗漏：{contributions.Average(x => x.MissingContribution):F2}");
            sb.AppendLine($"模式：{contributions.Average(x => x.PatternContribution):F2}");
            sb.AppendLine($"动量：{contributions.Average(x => x.MomentumContribution):F2}（V6权重为0）");
        }
    }
}
