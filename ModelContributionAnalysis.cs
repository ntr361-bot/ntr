using System;
using System.Collections.Generic;
using System.Linq;

namespace 六合分析软件
{
    /// <summary>
    /// v6.7 模型贡献分析（Ablation Study）
    /// 通过逐一移除子模型，分析每个模型对整体预测的真实贡献。
    /// 双方案对比：A方案（归一化）vs B方案（原始权重）。
    /// 稳定性分析：200期测试拆分为4段×50期，分别输出Top3命中率。
    /// 不修改预测数据，不调整结果，纯贡献分析。
    /// </summary>
    public static class ModelContributionAnalysis
    {
        // ===== 分段统计 =====
        public class SegmentStats
        {
            public string Label { get; set; } = "";
            public int StartIndex { get; set; }
            public int EndIndex { get; set; }
            public int Count { get; set; }
            public int Top1Hits { get; set; }
            public int Top3Hits { get; set; }
            public int Top6Hits { get; set; }
            public int MaxConsecutiveMiss { get; set; }
            public double Top1Rate => Count > 0 ? (double)Top1Hits / Count * 100 : 0;
            public double Top3Rate => Count > 0 ? (double)Top3Hits / Count * 100 : 0;
            public double Top6Rate => Count > 0 ? (double)Top6Hits / Count * 100 : 0;
        }

        // ===== 单方案结果 =====
        public class PlanResult
        {
            public string PlanName { get; set; } = "";
            public int Top1Hits { get; set; }
            public int Top3Hits { get; set; }
            public int Top6Hits { get; set; }
            public int TestPeriods { get; set; }
            public double Top1Rate => TestPeriods > 0 ? (double)Top1Hits / TestPeriods * 100 : 0;
            public double Top3Rate => TestPeriods > 0 ? (double)Top3Hits / TestPeriods * 100 : 0;
            public double Top6Rate => TestPeriods > 0 ? (double)Top6Hits / TestPeriods * 100 : 0;
            public int MaxConsecutiveMiss { get; set; }
            public int MaxConsecutiveHit { get; set; }
            public double ComprehensiveScore => Top1Rate * 1.0 + Top3Rate * 0.5 + Top6Rate * 0.1;
            public List<SegmentStats> Segments { get; set; } = new List<SegmentStats>();
        }

        // ===== 单条测试记录（A/B方案共用同一实际值，但预测排名不同）=====
        public class VariantRecord
        {
            public int TestIndex { get; set; }
            public string Period { get; set; } = "";
            public string ActualZodiac { get; set; } = "";
            public List<string> RankingA { get; set; } = new List<string>();
            public List<string> RankingB { get; set; } = new List<string>();
            public bool Top1HitA { get; set; }
            public bool Top3HitA { get; set; }
            public bool Top6HitA { get; set; }
            public bool Top1HitB { get; set; }
            public bool Top3HitB { get; set; }
            public bool Top6HitB { get; set; }
        }

        // ===== 单个实验变体结果 =====
        public class VariantResult
        {
            public string VariantName { get; set; } = "";
            public string[] DisabledModels { get; set; } = Array.Empty<string>();
            public int TrainPeriods { get; set; }
            public int TestPeriods { get; set; }
            public PlanResult PlanA { get; set; } = new PlanResult();
            public PlanResult PlanB { get; set; } = new PlanResult();
            public List<VariantRecord> Records { get; set; } = new List<VariantRecord>();
        }

        // ===== 总报告 =====
        public class AnalysisReport
        {
            public DateTime AnalysisTime { get; set; }
            public int TrainPeriods { get; set; }
            public int TestPeriods { get; set; }
            public int SegmentSize { get; set; }
            public string DatabasePath { get; set; } = "";
            public int TotalHistoryRecords { get; set; }
            public List<VariantResult> Variants { get; set; } = new List<VariantResult>();
        }

        // ===== 常量 =====
        private static readonly string[] AllModels = { "Frequency", "Trend", "Missing", "Pattern", "Momentum" };
        private static readonly string[] AllZodiacs = { "鼠", "牛", "虎", "兔", "龙", "蛇", "马", "羊", "猴", "鸡", "狗", "猪" };

        // 原始固定权重
        private static readonly Dictionary<string, double> BaseWeights = new Dictionary<string, double>
        {
            { "frequency", 0.20 },
            { "trend", 0.25 },
            { "missing", 0.25 },
            { "pattern", 0.15 },
            { "momentum", 0.15 }
        };

        // ===== 主入口 =====
        public static AnalysisReport Run(int trainPeriods = 300, int testPeriods = 200, int segmentSize = 50)
        {
            var report = new AnalysisReport
            {
                AnalysisTime = DateTime.Now,
                TrainPeriods = trainPeriods,
                TestPeriods = testPeriods,
                SegmentSize = segmentSize,
                DatabasePath = DatabaseHelper.DatabasePath
            };

            var allHistory = DatabaseHelper.GetLatestHistory(trainPeriods + testPeriods + 100);
            var validHistory = allHistory
                .Where(r => !string.IsNullOrEmpty(r.SpecialZodiac))
                .ToList();

            report.TotalHistoryRecords = validHistory.Count;

            int needed = trainPeriods + testPeriods;
            if (validHistory.Count < needed)
            {
                throw new InvalidOperationException(
                    $"历史数据不足：需要 {needed} 期，实际只有 {validHistory.Count} 期。");
            }

            // 实验0：完整 V6（基线）
            report.Variants.Add(RunVariant("完整V6（基线）", Array.Empty<string>(), validHistory, trainPeriods, testPeriods, segmentSize));

            // 实验1-5：逐一移除各模型
            foreach (var model in AllModels)
            {
                string name = $"去除{GetModelChineseName(model)}";
                report.Variants.Add(RunVariant(name, new[] { model }, validHistory, trainPeriods, testPeriods, segmentSize));
            }

            return report;
        }

        // ===== 运行单个实验变体（同时计算A/B两方案）=====
        private static VariantResult RunVariant(
            string variantName,
            string[] disabledModels,
            List<DatabaseHelper.HistoryRecord> allHistory,
            int trainPeriods,
            int testPeriods,
            int segmentSize)
        {
            var result = new VariantResult
            {
                VariantName = variantName,
                DisabledModels = disabledModels,
                TrainPeriods = trainPeriods,
                TestPeriods = testPeriods
            };

            // 初始化 A/B 方案
            result.PlanA = new PlanResult { PlanName = "A方案(归一化)", TestPeriods = testPeriods };
            result.PlanB = new PlanResult { PlanName = "B方案(原始权重)", TestPeriods = testPeriods };

            // 初始化分段
            int segmentCount = (testPeriods + segmentSize - 1) / segmentSize;
            for (int s = 0; s < segmentCount; s++)
            {
                int start = s * segmentSize + 1;
                int end = Math.Min((s + 1) * segmentSize, testPeriods);
                string label = $"{start}-{end}期";
                result.PlanA.Segments.Add(new SegmentStats { Label = label, StartIndex = start, EndIndex = end });
                result.PlanB.Segments.Add(new SegmentStats { Label = label, StartIndex = start, EndIndex = end });
            }

            var chronological = allHistory.AsEnumerable().Reverse().ToList();

            // A方案统计变量
            int consecMissA = 0, consecHitA = 0, maxMissA = 0, maxHitA = 0;
            // B方案统计变量
            int consecMissB = 0, consecHitB = 0, maxMissB = 0, maxHitB = 0;

            for (int i = 0; i < testPeriods; i++)
            {
                int testPos = trainPeriods + i;
                var actual = chronological[testPos];
                string actualZodiac = actual.SpecialZodiac;
                string period = actual.Period;

                var trainData = chronological.Take(testPos).ToList();

                // A方案预测（归一化 + 动态权重）
                var rankingA = PredictRankingPlanA(trainData, disabledModels);
                // B方案预测（原始权重，不调整）
                var rankingB = PredictRankingPlanB(trainData, disabledModels);

                var top1A = rankingA.Take(1).Select(r => r.zodiac).ToList();
                var top3A = rankingA.Take(3).Select(r => r.zodiac).ToList();
                var top6A = rankingA.Take(6).Select(r => r.zodiac).ToList();

                var top1B = rankingB.Take(1).Select(r => r.zodiac).ToList();
                var top3B = rankingB.Take(3).Select(r => r.zodiac).ToList();
                var top6B = rankingB.Take(6).Select(r => r.zodiac).ToList();

                bool t1A = top1A.Contains(actualZodiac);
                bool t3A = top3A.Contains(actualZodiac);
                bool t6A = top6A.Contains(actualZodiac);
                bool t1B = top1B.Contains(actualZodiac);
                bool t3B = top3B.Contains(actualZodiac);
                bool t6B = top6B.Contains(actualZodiac);

                // A方案统计
                if (t3A) { result.PlanA.Top3Hits++; consecHitA++; consecMissA = 0; if (consecHitA > maxHitA) maxHitA = consecHitA; }
                else { consecMissA++; consecHitA = 0; if (consecMissA > maxMissA) maxMissA = consecMissA; }
                if (t1A) result.PlanA.Top1Hits++;
                if (t6A) result.PlanA.Top6Hits++;

                // B方案统计
                if (t3B) { result.PlanB.Top3Hits++; consecHitB++; consecMissB = 0; if (consecHitB > maxHitB) maxHitB = consecHitB; }
                else { consecMissB++; consecHitB = 0; if (consecMissB > maxMissB) maxMissB = consecMissB; }
                if (t1B) result.PlanB.Top1Hits++;
                if (t6B) result.PlanB.Top6Hits++;

                // 分段统计
                int segIdx = i / segmentSize;
                if (segIdx < result.PlanA.Segments.Count)
                {
                    var segA = result.PlanA.Segments[segIdx];
                    segA.Count++;
                    if (t1A) segA.Top1Hits++;
                    if (t3A) segA.Top3Hits++;
                    if (t6A) segA.Top6Hits++;
                }
                if (segIdx < result.PlanB.Segments.Count)
                {
                    var segB = result.PlanB.Segments[segIdx];
                    segB.Count++;
                    if (t1B) segB.Top1Hits++;
                    if (t3B) segB.Top3Hits++;
                    if (t6B) segB.Top6Hits++;
                }

                result.Records.Add(new VariantRecord
                {
                    TestIndex = i + 1,
                    Period = period,
                    ActualZodiac = actualZodiac,
                    RankingA = rankingA.Select(r => r.zodiac).ToList(),
                    RankingB = rankingB.Select(r => r.zodiac).ToList(),
                    Top1HitA = t1A, Top3HitA = t3A, Top6HitA = t6A,
                    Top1HitB = t1B, Top3HitB = t3B, Top6HitB = t6B
                });
            }

            result.PlanA.MaxConsecutiveMiss = maxMissA;
            result.PlanA.MaxConsecutiveHit = maxHitA;
            result.PlanB.MaxConsecutiveMiss = maxMissB;
            result.PlanB.MaxConsecutiveHit = maxHitB;

            // 计算分段内最大连续失败
            foreach (var seg in result.PlanA.Segments)
                seg.MaxConsecutiveMiss = CalcSegmentMaxConsecMiss(result.Records, seg.StartIndex, seg.EndIndex, true);
            foreach (var seg in result.PlanB.Segments)
                seg.MaxConsecutiveMiss = CalcSegmentMaxConsecMiss(result.Records, seg.StartIndex, seg.EndIndex, false);

            return result;
        }

        private static int CalcSegmentMaxConsecMiss(List<VariantRecord> records, int start, int end, bool usePlanA)
        {
            int max = 0, cur = 0;
            foreach (var r in records)
            {
                if (r.TestIndex < start || r.TestIndex > end) continue;
                bool hit = usePlanA ? r.Top3HitA : r.Top3HitB;
                if (!hit) { cur++; if (cur > max) max = cur; }
                else cur = 0;
            }
            return max;
        }

        // ===== A方案：归一化 + 动态权重（复刻 EnsemblePredictionService）=====
        private static List<(string zodiac, double score)> PredictRankingPlanA(
            List<DatabaseHelper.HistoryRecord> history,
            string[] disabledModels)
        {
            var disabled = new HashSet<string>(disabledModels);
            var weights = CalcWeightsPlanA(history, disabled);
            return ScoreWithWeights(history, weights, disabled);
        }

        private static Dictionary<string, double> CalcWeightsPlanA(
            List<DatabaseHelper.HistoryRecord> history,
            HashSet<string> disabled)
        {
            var weights = new Dictionary<string, double>(BaseWeights);

            // 禁用模型归零
            foreach (var model in disabled)
            {
                string key = model.ToLower();
                if (weights.ContainsKey(key)) weights[key] = 0;
            }

            // 归一化
            double total = weights.Values.Sum();
            if (total > 0)
            {
                foreach (var key in weights.Keys.ToList())
                    weights[key] /= total;
            }

            // 动态调整（基于最近50期准确率）
            var recentHistory = history.Take(50).ToList();
            if (recentHistory.Count < 20) return weights;

            var activeKeys = weights.Keys.Where(k => weights[k] > 0).ToList();
            if (activeKeys.Count == 0) return weights;

            double totalAccuracy = 0;
            var accuracies = new Dictionary<string, double>();
            foreach (var key in activeKeys)
            {
                double acc = EvaluateModelAccuracy(recentHistory, key);
                accuracies[key] = acc;
                totalAccuracy += acc;
            }

            if (totalAccuracy > 0)
            {
                var baseline = new Dictionary<string, double>(weights);
                foreach (var key in activeKeys)
                    weights[key] = baseline[key] * 0.5 + accuracies[key] / totalAccuracy * 0.5;
            }

            return weights;
        }

        // ===== B方案：保持原始权重，不做归一化和动态调整 =====
        private static List<(string zodiac, double score)> PredictRankingPlanB(
            List<DatabaseHelper.HistoryRecord> history,
            string[] disabledModels)
        {
            var disabled = new HashSet<string>(disabledModels);

            // 直接使用原始权重，禁用模型归零，不做任何调整
            var weights = new Dictionary<string, double>(BaseWeights);
            foreach (var model in disabled)
            {
                string key = model.ToLower();
                if (weights.ContainsKey(key)) weights[key] = 0;
            }

            return ScoreWithWeights(history, weights, disabled);
        }

        // ===== 通用评分（给定权重，计算排名）=====
        private static List<(string zodiac, double score)> ScoreWithWeights(
            List<DatabaseHelper.HistoryRecord> history,
            Dictionary<string, double> weights,
            HashSet<string> disabled)
        {
            var scores = new List<(string zodiac, double score)>();

            foreach (var zodiac in AllZodiacs)
            {
                double freqScore = disabled.Contains("Frequency") ? 0 : CalcFrequency(history, zodiac);
                double trendScore = disabled.Contains("Trend") ? 0 : CalcTrend(history, zodiac);
                double missingScore = disabled.Contains("Missing") ? 0 : CalcMissing(history, zodiac);
                double patternScore = disabled.Contains("Pattern") ? 0 : CalcPattern(history, zodiac);
                double momentumScore = disabled.Contains("Momentum") ? 0 : CalcMomentum(history, zodiac);

                double finalScore =
                    freqScore * weights.GetValueOrDefault("frequency", 0) +
                    trendScore * weights.GetValueOrDefault("trend", 0) +
                    missingScore * weights.GetValueOrDefault("missing", 0) +
                    patternScore * weights.GetValueOrDefault("pattern", 0) +
                    momentumScore * weights.GetValueOrDefault("momentum", 0);

                scores.Add((zodiac, finalScore));
            }

            return scores.OrderByDescending(s => s.score).ThenBy(s => s.zodiac).ToList();
        }

        // ===== 评估单模型准确率 =====
        private static double EvaluateModelAccuracy(List<DatabaseHelper.HistoryRecord> history, string modelName)
        {
            int correct = 0;
            int total = Math.Min(20, history.Count - 1);

            for (int i = 0; i < total; i++)
            {
                var actual = history[i].SpecialZodiac;
                var trainSubset = history.Skip(i + 1).ToList();
                if (trainSubset.Count == 0) continue;

                var predicted = GetSingleModelTopZodiac(trainSubset, modelName);
                if (predicted == actual) correct++;
            }

            return total > 0 ? (double)correct / total : 0.5;
        }

        private static string GetSingleModelTopZodiac(List<DatabaseHelper.HistoryRecord> history, string modelName)
        {
            return AllZodiacs.Select(z => (zodiac: z, score: modelName switch
            {
                "frequency" => CalcFrequency(history, z),
                "trend" => CalcTrend(history, z),
                "missing" => CalcMissing(history, z),
                "pattern" => CalcPattern(history, z),
                "momentum" => CalcMomentum(history, z),
                _ => 0.0
            })).OrderByDescending(x => x.score).ThenBy(x => x.zodiac).First().zodiac;
        }

        // ===== 子模型实现（完全复刻 EnsemblePredictionService）=====

        private static double CalcFrequency(List<DatabaseHelper.HistoryRecord> history, string zodiac)
        {
            int count = history.Count(h => h.SpecialZodiac == zodiac);
            double frequency = history.Count > 0 ? (double)count / history.Count : 0;
            return Math.Min(1.0, frequency * 12);
        }

        private static double CalcTrend(List<DatabaseHelper.HistoryRecord> history, string zodiac)
        {
            int recent30 = history.Take(30).Count(h => h.SpecialZodiac == zodiac);
            int previous30 = history.Skip(30).Take(30).Count(h => h.SpecialZodiac == zodiac);
            double trend = (recent30 - previous30) / 30.0;
            return Math.Max(0, Math.Min(1.0, 0.5 + trend * 2));
        }

        private static double CalcMissing(List<DatabaseHelper.HistoryRecord> history, string zodiac)
        {
            int missing = 0;
            foreach (var h in history)
            {
                if (h.SpecialZodiac == zodiac) break;
                missing++;
            }

            var allMissings = new List<int>();
            int currentMissing = 0;
            foreach (var h in history)
            {
                if (h.SpecialZodiac == zodiac)
                {
                    if (currentMissing > 0) allMissings.Add(currentMissing);
                    currentMissing = 0;
                }
                else currentMissing++;
            }

            double avgMissing = allMissings.Count > 0 ? allMissings.Average() : 10;
            double ratio = missing / avgMissing;

            if (ratio >= 0.8 && ratio <= 1.5) return 0.9;
            else if (ratio >= 0.5 && ratio <= 2.0) return 0.7;
            else if (ratio < 0.3) return 0.3;
            else return 0.4;
        }

        private static double CalcPattern(List<DatabaseHelper.HistoryRecord> history, string zodiac)
        {
            var intervals = new List<int>();
            int lastSeen = -1;

            for (int i = 0; i < history.Count; i++)
            {
                if (history[i].SpecialZodiac == zodiac)
                {
                    if (lastSeen >= 0) intervals.Add(i - lastSeen);
                    lastSeen = i;
                }
            }

            if (intervals.Count < 3) return 0.5;

            double avgInterval = intervals.Average();
            double variance = intervals.Select(x => Math.Pow(x - avgInterval, 2)).Average();
            double stdDev = Math.Sqrt(variance);

            double regularity = 1.0 - Math.Min(1.0, stdDev / 10.0);

            int currentMissing = 0;
            foreach (var h in history)
            {
                if (h.SpecialZodiac == zodiac) break;
                currentMissing++;
            }

            double matchScore = 1.0 - Math.Min(1.0, Math.Abs(currentMissing - avgInterval) / avgInterval);

            return (regularity + matchScore) / 2.0;
        }

        private static double CalcMomentum(List<DatabaseHelper.HistoryRecord> history, string zodiac)
        {
            int recent10 = history.Take(10).Count(h => h.SpecialZodiac == zodiac);
            int recent20 = history.Take(20).Count(h => h.SpecialZodiac == zodiac);
            double momentum = recent20 > 0 ? (double)recent10 / recent20 : 0.5;
            return Math.Max(0, Math.Min(1.0, momentum));
        }

        // ===== 辅助 =====
        private static string GetModelChineseName(string model)
        {
            return model switch
            {
                "Frequency" => "FrequencyModel(频率)",
                "Trend" => "TrendModel(趋势)",
                "Missing" => "MissingModel(遗漏)",
                "Pattern" => "PatternModel(模式)",
                "Momentum" => "MomentumModel(动量)",
                _ => model
            };
        }

        // ===== 生成报告文本 =====
        public static string GenerateReport(AnalysisReport report)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════");
            sb.AppendLine("       v6.7 模型贡献分析报告 (Model Contribution Analysis)");
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════");
            sb.AppendLine($"分析时间：{report.AnalysisTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"数据库：{report.DatabasePath}");
            sb.AppendLine($"历史数据总量：{report.TotalHistoryRecords} 期");
            sb.AppendLine($"训练期数：{report.TrainPeriods} 期");
            sb.AppendLine($"测试期数：{report.TestPeriods} 期");
            sb.AppendLine($"分段大小：{report.SegmentSize} 期/段");
            sb.AppendLine();
            sb.AppendLine("方案说明：");
            sb.AppendLine("  A方案 = 移除模型后剩余权重重新归一化 + 动态权重调整");
            sb.AppendLine("  B方案 = 移除模型后保持原始权重不变（固定比例，无动态调整）");
            sb.AppendLine();

            // ===== 汇总表 =====
            sb.AppendLine("═══════════════════════════ A方案（归一化）═══════════════════════════");
            sb.AppendLine($"{"实验变体",-35} {"Top1",7} {"Top3",7} {"Top6",7} {"最大连败",8} {"综合评分",8}");
            sb.AppendLine(new string('─', 80));

            foreach (var v in report.Variants)
            {
                var p = v.PlanA;
                sb.AppendLine(
                    $"{v.VariantName,-35} " +
                    $"{p.Top1Rate,6:F1}% " +
                    $"{p.Top3Rate,6:F1}% " +
                    $"{p.Top6Rate,6:F1}% " +
                    $"{p.MaxConsecutiveMiss,6} " +
                    $"{p.ComprehensiveScore,7:F2}");
            }

            sb.AppendLine();
            sb.AppendLine("═══════════════════════════ B方案（原始权重）═══════════════════════════");
            sb.AppendLine($"{"实验变体",-35} {"Top1",7} {"Top3",7} {"Top6",7} {"最大连败",8} {"综合评分",8}");
            sb.AppendLine(new string('─', 80));

            foreach (var v in report.Variants)
            {
                var p = v.PlanB;
                sb.AppendLine(
                    $"{v.VariantName,-35} " +
                    $"{p.Top1Rate,6:F1}% " +
                    $"{p.Top3Rate,6:F1}% " +
                    $"{p.Top6Rate,6:F1}% " +
                    $"{p.MaxConsecutiveMiss,6} " +
                    $"{p.ComprehensiveScore,7:F2}");
            }

            sb.AppendLine();

            // ===== 稳定性分析 =====
            sb.AppendLine("═══════════════════════════ 稳定性分析（Top3命中率分段）═══════════════════════════");
            sb.AppendLine();

            sb.AppendLine("── A方案（归一化）──");
            PrintSegmentTable(sb, report, true);

            sb.AppendLine();
            sb.AppendLine("── B方案（原始权重）──");
            PrintSegmentTable(sb, report, false);

            sb.AppendLine();

            // ===== 贡献度对比（A方案）=====
            if (report.Variants.Count > 1)
            {
                var baseline = report.Variants[0];

                sb.AppendLine("═══════════════════════════ A方案 贡献度分析 ═══════════════════════════");
                sb.AppendLine($"{"移除模型",-35} {"Top1变化",9} {"Top3变化",9} {"Top6变化",9} {"综合变化",9}");
                sb.AppendLine(new string('─', 80));

                for (int i = 1; i < report.Variants.Count; i++)
                {
                    var v = report.Variants[i];
                    double dTop1 = v.PlanA.Top1Rate - baseline.PlanA.Top1Rate;
                    double dTop3 = v.PlanA.Top3Rate - baseline.PlanA.Top3Rate;
                    double dTop6 = v.PlanA.Top6Rate - baseline.PlanA.Top6Rate;
                    double dScore = v.PlanA.ComprehensiveScore - baseline.PlanA.ComprehensiveScore;

                    sb.AppendLine(
                        $"{v.VariantName,-35} " +
                        $"{dTop1,+8:F1}% " +
                        $"{dTop3,+8:F1}% " +
                        $"{dTop6,+8:F1}% " +
                        $"{dScore,+8:F2}");
                }

                sb.AppendLine();

                // 贡献度排名
                var contributionsA = report.Variants.Skip(1)
                    .Select(v => new { Model = v.DisabledModels[0], Impact = baseline.PlanA.ComprehensiveScore - v.PlanA.ComprehensiveScore })
                    .OrderByDescending(x => x.Impact).ToList();

                sb.AppendLine("── A方案 贡献度排名 ──");
                for (int i = 0; i < contributionsA.Count; i++)
                {
                    var c = contributionsA[i];
                    string impact = c.Impact > 0 ? $"正向贡献 +{c.Impact:F2}" :
                                    c.Impact < 0 ? $"负向贡献 {c.Impact:F2}" : "无影响";
                    sb.AppendLine($"  #{i + 1} {GetModelChineseName(c.Model),-25} {impact}");
                }

                sb.AppendLine();

                // ===== 贡献度对比（B方案）=====
                sb.AppendLine("═══════════════════════════ B方案 贡献度分析 ═══════════════════════════");
                sb.AppendLine($"{"移除模型",-35} {"Top1变化",9} {"Top3变化",9} {"Top6变化",9} {"综合变化",9}");
                sb.AppendLine(new string('─', 80));

                for (int i = 1; i < report.Variants.Count; i++)
                {
                    var v = report.Variants[i];
                    double dTop1 = v.PlanB.Top1Rate - baseline.PlanB.Top1Rate;
                    double dTop3 = v.PlanB.Top3Rate - baseline.PlanB.Top3Rate;
                    double dTop6 = v.PlanB.Top6Rate - baseline.PlanB.Top6Rate;
                    double dScore = v.PlanB.ComprehensiveScore - baseline.PlanB.ComprehensiveScore;

                    sb.AppendLine(
                        $"{v.VariantName,-35} " +
                        $"{dTop1,+8:F1}% " +
                        $"{dTop3,+8:F1}% " +
                        $"{dTop6,+8:F1}% " +
                        $"{dScore,+8:F2}");
                }

                sb.AppendLine();

                var contributionsB = report.Variants.Skip(1)
                    .Select(v => new { Model = v.DisabledModels[0], Impact = baseline.PlanB.ComprehensiveScore - v.PlanB.ComprehensiveScore })
                    .OrderByDescending(x => x.Impact).ToList();

                sb.AppendLine("── B方案 贡献度排名 ──");
                for (int i = 0; i < contributionsB.Count; i++)
                {
                    var c = contributionsB[i];
                    string impact = c.Impact > 0 ? $"正向贡献 +{c.Impact:F2}" :
                                    c.Impact < 0 ? $"负向贡献 {c.Impact:F2}" : "无影响";
                    sb.AppendLine($"  #{i + 1} {GetModelChineseName(c.Model),-25} {impact}");
                }
            }

            sb.AppendLine();

            // ===== 稳定性结论 =====
            sb.AppendLine("═══════════════════════════ 稳定性结论 ═══════════════════════════");
            PrintStabilityConclusion(sb, report);

            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════");
            sb.AppendLine("注：综合评分 = Top1×1.0 + Top3×0.5 + Top6×0.1");
            sb.AppendLine("注：所有实验使用同一 history.db，训练300期，测试200期。");
            sb.AppendLine("注：未修改任何预测数据，未调整任何结果。");
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════");

            return sb.ToString();
        }

        // ===== 分段表格输出 =====
        private static void PrintSegmentTable(System.Text.StringBuilder sb, AnalysisReport report, bool usePlanA)
        {
            // 表头：变体名 + 各段Top3 + 段间波动(标准差)
            var segLabels = report.Variants[0].PlanA.Segments.Select(s => s.Label).ToList();
            string header = $"{"实验变体",-30}";
            foreach (var label in segLabels) header += $" {label,10}";
            header += $" {"波动σ",8}";
            sb.AppendLine(header);
            sb.AppendLine(new string('─', 30 + segLabels.Count * 11 + 10));

            foreach (var v in report.Variants)
            {
                var plan = usePlanA ? v.PlanA : v.PlanB;
                string line = $"{v.VariantName,-30}";
                var rates = new List<double>();
                foreach (var seg in plan.Segments)
                {
                    line += $" {seg.Top3Rate,9:F1}%";
                    rates.Add(seg.Top3Rate);
                }
                // 计算段间标准差
                double mean = rates.Average();
                double variance = rates.Select(r => Math.Pow(r - mean, 2)).Average();
                double stdDev = Math.Sqrt(variance);
                line += $" {stdDev,7:F2}";
                sb.AppendLine(line);
            }
        }

        // ===== 稳定性结论 =====
        private static void PrintStabilityConclusion(System.Text.StringBuilder sb, AnalysisReport report)
        {
            if (report.Variants.Count <= 1) return;

            var baseline = report.Variants[0];

            sb.AppendLine("各模型贡献稳定性评估（基于A方案分段Top3命中率波动）：");
            sb.AppendLine();

            for (int i = 1; i < report.Variants.Count; i++)
            {
                var v = report.Variants[i];
                string modelName = GetModelChineseName(v.DisabledModels[0]);

                // 基线各段Top3
                var baseRates = baseline.PlanA.Segments.Select(s => s.Top3Rate).ToList();
                var removeRates = v.PlanA.Segments.Select(s => s.Top3Rate).ToList();

                // 各段影响
                var segImpacts = new List<double>();
                for (int s = 0; s < Math.Min(baseRates.Count, removeRates.Count); s++)
                    segImpacts.Add(baseRates[s] - removeRates[s]);

                double avgImpact = segImpacts.Average();
                double impactVariance = segImpacts.Select(x => Math.Pow(x - avgImpact, 2)).Average();
                double impactStdDev = Math.Sqrt(impactVariance);

                string stability;
                if (impactStdDev < 3.0) stability = "稳定 ✅";
                else if (impactStdDev < 6.0) stability = "一般 ⚠️";
                else stability = "不稳定 ❌";

                sb.AppendLine($"  {modelName,-25} 平均影响:{avgImpact,+6:F2}%  段间波动σ:{impactStdDev:F2}  → {stability}");

                // 各段详情
                for (int s = 0; s < segImpacts.Count; s++)
                {
                    string segLabel = baseline.PlanA.Segments[s].Label;
                    sb.AppendLine($"    {segLabel}: 基线{baseRates[s]:F1}% → 移除{removeRates[s]:F1}% (影响{segImpacts[s]:+F2;-F2;0})");
                }
                sb.AppendLine();
            }
        }
    }
}
