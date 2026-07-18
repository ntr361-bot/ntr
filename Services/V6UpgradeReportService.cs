using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace 六合分析软件
{
    public static class V6UpgradeReportService
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

            sb.AppendLine("六合分析软件 v6.4 动态权重 A/B 测试真实回测报告");
            sb.AppendLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("数据来源：统一根目录 history.db 真实历史特码生肖");
            sb.AppendLine($"读取期数：{history.Count}");
            sb.AppendLine("说明：本报告只读取历史数据，不修改开奖记录；只有v6.4综合评分超过V6才建议替换默认模型。");
            sb.AppendLine();

            if (history.Count < 500)
            {
                sb.AppendLine("历史数据不足：本次验收需要500期。");
                return sb.ToString();
            }

            int trainPeriods = 300;
            int testPeriods = 200;
            var reports = new List<(string name, ModelScoreResult score)>
            {
                ("V6", EvaluateV64Variant(history, trainPeriods, testPeriods, V64Variant.V6)),
                ("V6.4动态权重", EvaluateV64Variant(history, trainPeriods, testPeriods, V64Variant.DynamicWeights))
            };

            sb.AppendLine("固定验收口径：");
            sb.AppendLine("数据：500期");
            sb.AppendLine("训练：300期");
            sb.AppendLine("测试：200期");
            sb.AppendLine("模型A：原 Ensemble V6");
            sb.AppendLine("模型B：Ensemble V6.4 DynamicWeightService");
            sb.AppendLine("训练说明：每一期测试只使用该期之前历史数据；第301-500期仅用于最终验证。");
            sb.AppendLine();

            foreach (var report in reports)
                AppendScore(sb, report.name, report.score);

            sb.AppendLine();
            sb.AppendLine("与V6对比：");
            var baseline = reports[0].score;
            foreach (var report in reports.Skip(1))
            {
                sb.AppendLine(report.name);
                AppendLift(sb, "Top1", baseline.Top1HitRate, report.score.Top1HitRate);
                AppendLift(sb, "Top3", baseline.Top3HitRate, report.score.Top3HitRate);
                AppendLift(sb, "Top6", baseline.Top6HitRate, report.score.Top6HitRate);
                AppendLift(sb, "综合评分", baseline.CombinedScore, report.score.CombinedScore);
                sb.AppendLine($"最大连续失败：{baseline.MaxConsecutiveMisses} -> {report.score.MaxConsecutiveMisses}");
            }

            var best = reports.OrderByDescending(r => r.score.CombinedScore).First();
            sb.AppendLine();
            sb.AppendLine($"本次最好版本：{best.name}");
            if (best.name != "V6")
                sb.AppendLine("结论：v6.4本次综合评分超过V6，可作为候选默认模型，但建议继续做多窗口验证。");
            else
                sb.AppendLine("结论：V6仍保持最佳，不合并v6.4为默认版本。");

            return sb.ToString();
        }

        private static ModelScoreResult EvaluateV64Variant(
            List<DatabaseHelper.HistoryRecord> oldToNewHistory,
            int trainPeriods,
            int testPeriods,
            V64Variant variant)
        {
            return WeightOptimizationService.EvaluatePredictor(
                oldToNewHistory,
                trainPeriods,
                testPeriods,
                variant.ToString(),
                training => RankVariant(training, variant));
        }

        private static List<(string zodiac, double score)> RankVariant(
            List<DatabaseHelper.HistoryRecord> newestFirstHistory,
            V64Variant variant)
        {
            if (variant == V64Variant.DynamicWeights)
                return DynamicWeightService.RankByDynamicWeights(newestFirstHistory);

            return WeightOptimizationService.RankByWeights(newestFirstHistory, V6Weights);
        }

        private static void AppendScore(StringBuilder sb, string title, ModelScoreResult score)
        {
            sb.AppendLine($"{title}：Top1 {score.Top1HitRate:F2}% ({score.Top1Hits}/{score.TotalTests})，Top3 {score.Top3HitRate:F2}% ({score.Top3Hits}/{score.TotalTests})，Top6 {score.Top6HitRate:F2}% ({score.Top6Hits}/{score.TotalTests})，最大连续命中 {score.MaxConsecutiveHits}，最大连续失败 {score.MaxConsecutiveMisses}，综合评分 {score.CombinedScore:F2}");
        }

        private static void AppendLift(StringBuilder sb, string label, double before, double after)
        {
            double lift = before > 0 ? (after - before) / before * 100 : 0;
            sb.AppendLine($"{label}：{before:F2} -> {after:F2}，变化 {lift:F2}%");
        }

        private enum V64Variant
        {
            V6,
            DynamicWeights
        }
    }
}
