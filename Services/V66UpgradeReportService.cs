using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace 六合分析软件
{
    public static class V66UpgradeReportService
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
            sb.AppendLine("六合分析软件 v6.6 MissingReliability真实A/B回测报告");
            sb.AppendLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"数据库：{DatabaseHelper.DatabasePath}");
            sb.AppendLine($"读取真实历史：{history.Count}期");
            sb.AppendLine("说明：不修改历史数据；每期可靠度只根据当期之前的数据计算；除遗漏评分外，其他模型及权重保持V6原样。");
            sb.AppendLine();

            if (history.Count < 500)
            {
                sb.AppendLine("数据不足：本次验收需要完整500期真实历史数据。");
                return sb.ToString();
            }

            const int trainPeriods = 300;
            const int testPeriods = 200;
            var baseline = WeightOptimizationService.EvaluateWeights(history, trainPeriods, testPeriods, V6Weights);
            var adjusted = WeightOptimizationService.EvaluatePredictor(
                history,
                trainPeriods,
                testPeriods,
                "V6 + MissingReliability",
                MissingReliabilityService.RankWithReliability);

            sb.AppendLine("验证范围：500期；起始训练300期；逐期测试200期");
            sb.AppendLine("A：原 Ensemble V6");
            sb.AppendLine("B：V6 + MissingReliability");
            sb.AppendLine();
            AppendScore(sb, "A 原V6", baseline);
            AppendScore(sb, "B V6.6", adjusted);
            sb.AppendLine();

            sb.AppendLine("遗漏长度回补概率（500期描述统计，不参与前面各期的未来预测）：");
            foreach (int length in new[] { 5, 10, 20 })
            {
                var item = MissingReliabilityService.AnalyzeLength(history.AsEnumerable().Reverse().ToList(), length);
                sb.AppendLine($"遗漏{length}期：历史下一期回补概率 {item.RecoveryProbability:P2}，有效样本 {item.EffectiveSamples:F1}，可靠度 {item.ReliabilityScore:P2}");
            }
            sb.AppendLine();

            AppendDifference(sb, "Top1", baseline.Top1HitRate, adjusted.Top1HitRate);
            AppendDifference(sb, "Top3", baseline.Top3HitRate, adjusted.Top3HitRate);
            AppendDifference(sb, "Top6", baseline.Top6HitRate, adjusted.Top6HitRate);
            AppendDifference(sb, "综合评分", baseline.CombinedScore, adjusted.CombinedScore);
            sb.AppendLine($"最大连续失败：{baseline.MaxConsecutiveMisses} -> {adjusted.MaxConsecutiveMisses}");
            sb.AppendLine();

            if (adjusted.CombinedScore > baseline.CombinedScore)
                sb.AppendLine("结论：V6.6综合评分超过V6，可作为候选默认模型；仍建议继续做独立窗口验证后再正式替换。");
            else
                sb.AppendLine("结论：V6.6未超过V6，不替换默认模型；原V6继续作为默认预测模型。");
            return sb.ToString();
        }

        private static void AppendScore(StringBuilder sb, string name, ModelScoreResult score)
        {
            sb.AppendLine(name);
            sb.AppendLine($"Top1：{score.Top1HitRate:F2}% ({score.Top1Hits}/{score.TotalTests})");
            sb.AppendLine($"Top3：{score.Top3HitRate:F2}% ({score.Top3Hits}/{score.TotalTests})");
            sb.AppendLine($"Top6：{score.Top6HitRate:F2}% ({score.Top6Hits}/{score.TotalTests})");
            sb.AppendLine($"最大连续命中：{score.MaxConsecutiveHits}");
            sb.AppendLine($"最大连续失败：{score.MaxConsecutiveMisses}");
            sb.AppendLine($"综合评分：{score.CombinedScore:F2}");
        }

        private static void AppendDifference(StringBuilder sb, string label, double before, double after)
        {
            sb.AppendLine($"{label}变化：{before:F2} -> {after:F2}（{after - before:+0.00;-0.00;0.00}）");
        }
    }
}
