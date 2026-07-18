using System;
using System.Collections.Generic;
using System.Linq;

namespace 六合分析软件
{
    /// <summary>
    /// v6.8 权重敏感性分析（Weight Sensitivity Analysis）
    /// 对集成学习的5个子模型权重进行系统性扰动（±10%, ±20%, ±30%），
    /// 观察 Top1/Top3/Top6 命中率和综合评分的变化，评估模型的权重敏感度。
    /// 不修改预测数据，不调整结果，纯敏感性分析。
    /// </summary>
    public static class WeightSensitivityAnalysis
    {
        // ===== 数据结构 =====

        /// <summary>单次扰动结果</summary>
        public class PerturbationResult
        {
            public string Label { get; set; } = "";            // 如 "Trend +20%"
            public string TargetModel { get; set; } = "";      // 被扰动的模型名
            public double DeltaPercent { get; set; }           // 扰动幅度 %
            public Dictionary<string, double> Weights { get; set; } = new();  // 实际权重

            public int TestPeriods { get; set; }
            public int Top1Hits { get; set; }
            public int Top3Hits { get; set; }
            public int Top6Hits { get; set; }
            public int MaxConsecutiveMiss { get; set; }
            public int MaxConsecutiveHit { get; set; }

            public double Top1Rate => TestPeriods > 0 ? (double)Top1Hits / TestPeriods * 100 : 0;
            public double Top3Rate => TestPeriods > 0 ? (double)Top3Hits / TestPeriods * 100 : 0;
            public double Top6Rate => TestPeriods > 0 ? (double)Top6Hits / TestPeriods * 100 : 0;
            public double ComprehensiveScore => Top1Rate * 1.0 + Top3Rate * 0.5 + Top6Rate * 0.1;
        }

        /// <summary>单模型完整扰动系列</summary>
        public class ModelSensitivity
        {
            public string ModelName { get; set; } = "";
            public string ChineseName { get; set; } = "";
            public double OriginalWeight { get; set; }
            public List<PerturbationResult> Perturbations { get; set; } = new();

            // 敏感度指标
            public double Top1Sensitivity { get; set; }       // Top1对权重变化的最大波动
            public double Top3Sensitivity { get; set; }
            public double ComprehensiveSensitivity { get; set; }
            public string SensitivityLevel { get; set; } = ""; // 低/中/高
        }

        /// <summary>总报告</summary>
        public class SensitivityReport
        {
            public DateTime AnalysisTime { get; set; }
            public int TrainPeriods { get; set; }
            public int TestPeriods { get; set; }
            public string DatabasePath { get; set; } = "";
            public int TotalHistoryRecords { get; set; }
            public PerturbationResult Baseline { get; set; } = new();
            public List<ModelSensitivity> Models { get; set; } = new();
            public List<string> OverallConclusions { get; set; } = new();
        }

        // ===== 常量 =====
        private static readonly string[] AllModels = { "frequency", "trend", "missing", "pattern", "momentum" };
        private static readonly string[] AllZodiacs = { "鼠", "牛", "虎", "兔", "龙", "蛇", "马", "羊", "猴", "鸡", "狗", "猪" };

        private static readonly Dictionary<string, double> BaseWeights = new()
        {
            { "frequency", 0.20 },
            { "trend", 0.25 },
            { "missing", 0.25 },
            { "pattern", 0.15 },
            { "momentum", 0.15 }
        };

        // 扰动幅度
        private static readonly double[] PerturbDeltas = { -0.30, -0.20, -0.10, +0.10, +0.20, +0.30 };

        // ===== 主入口 =====
        public static SensitivityReport Run(int trainPeriods = 300, int testPeriods = 200)
        {
            var report = new SensitivityReport
            {
                AnalysisTime = DateTime.Now,
                TrainPeriods = trainPeriods,
                TestPeriods = testPeriods,
                DatabasePath = DatabaseHelper.DatabasePath
            };

            var allHistory = DatabaseHelper.GetLatestHistory(trainPeriods + testPeriods + 100);
            var validHistory = allHistory
                .Where(r => !string.IsNullOrEmpty(r.SpecialZodiac))
                .ToList();
            report.TotalHistoryRecords = validHistory.Count;

            int needed = trainPeriods + testPeriods;
            if (validHistory.Count < needed)
                throw new InvalidOperationException($"历史数据不足：需要 {needed} 期，实际 {validHistory.Count} 期。");

            var chronological = validHistory.AsEnumerable().Reverse().ToList();

            // 基线
            report.Baseline = RunPerturbation("基线(原始权重)", "", 0, BaseWeights, chronological, trainPeriods, testPeriods);

            // 对每个模型逐一扰动
            foreach (var model in AllModels)
            {
                var sensitivity = new ModelSensitivity
                {
                    ModelName = model,
                    ChineseName = GetModelChineseName(model),
                    OriginalWeight = BaseWeights[model]
                };

                foreach (var delta in PerturbDeltas)
                {
                    var perturbedWeights = ComputePerturbedWeights(model, delta);
                    string label = $"{GetModelChineseName(model)} {(delta > 0 ? "+" : "")}{delta * 100:F0}%";
                    var result = RunPerturbation(label, model, delta, perturbedWeights, chronological, trainPeriods, testPeriods);
                    sensitivity.Perturbations.Add(result);
                }

                // 计算敏感度指标
                ComputeSensitivityMetrics(sensitivity, report.Baseline);
                report.Models.Add(sensitivity);
            }

            // 生成结论
            report.OverallConclusions = GenerateConclusions(report);

            return report;
        }

        // ===== 计算扰动后的权重 =====
        private static Dictionary<string, double> ComputePerturbedWeights(string targetModel, double delta)
        {
            var weights = new Dictionary<string, double>(BaseWeights);

            // 扰动目标权重
            double newTargetWeight = weights[targetModel] * (1 + delta);
            weights[targetModel] = Math.Max(0.01, Math.Min(0.90, newTargetWeight));

            // 重新归一化其他权重（按原始比例分配剩余）
            double rest = 1.0 - weights[targetModel];
            double otherTotal = BaseWeights.Where(kv => kv.Key != targetModel).Sum(kv => kv.Value);

            foreach (var key in weights.Keys.ToList())
            {
                if (key == targetModel) continue;
                weights[key] = rest * (BaseWeights[key] / otherTotal);
            }

            return weights;
        }

        // ===== 运行单次扰动实验 =====
        private static PerturbationResult RunPerturbation(
            string label,
            string targetModel,
            double delta,
            Dictionary<string, double> weights,
            List<DatabaseHelper.HistoryRecord> chronological,
            int trainPeriods,
            int testPeriods)
        {
            var result = new PerturbationResult
            {
                Label = label,
                TargetModel = targetModel,
                DeltaPercent = delta * 100,
                Weights = new Dictionary<string, double>(weights),
                TestPeriods = testPeriods
            };

            int consecMiss = 0, consecHit = 0;
            int top1Hits = 0, top3Hits = 0, top6Hits = 0;

            for (int i = 0; i < testPeriods; i++)
            {
                int testPos = trainPeriods + i;
                var actual = chronological[testPos];
                string actualZodiac = actual.SpecialZodiac;

                var trainData = chronological.Take(testPos).ToList();
                var ranking = PredictRanking(trainData, weights);

                var top3 = ranking.Take(3).Select(r => r).ToList();
                var top6 = ranking.Take(6).Select(r => r).ToList();

                bool t1Hit = ranking.First() == actualZodiac;
                bool t3Hit = top3.Contains(actualZodiac);
                bool t6Hit = top6.Contains(actualZodiac);

                if (t1Hit) top1Hits++;
                if (t3Hit) { top3Hits++; consecHit++; consecMiss = 0; if (consecHit > result.MaxConsecutiveHit) result.MaxConsecutiveHit = consecHit; }
                else { consecMiss++; consecHit = 0; if (consecMiss > result.MaxConsecutiveMiss) result.MaxConsecutiveMiss = consecMiss; }
                if (t6Hit) top6Hits++;
            }

            result.Top1Hits = top1Hits;
            result.Top3Hits = top3Hits;
            result.Top6Hits = top6Hits;

            return result;
        }

        // ===== 通用预测排名 =====
        private static List<string> PredictRanking(
            List<DatabaseHelper.HistoryRecord> history,
            Dictionary<string, double> weights)
        {
            var scores = new List<(string zodiac, double score)>();

            foreach (var zodiac in AllZodiacs)
            {
                double finalScore =
                    CalcFrequency(history, zodiac) * weights.GetValueOrDefault("frequency", 0) +
                    CalcTrend(history, zodiac) * weights.GetValueOrDefault("trend", 0) +
                    CalcMissing(history, zodiac) * weights.GetValueOrDefault("missing", 0) +
                    CalcPattern(history, zodiac) * weights.GetValueOrDefault("pattern", 0) +
                    CalcMomentum(history, zodiac) * weights.GetValueOrDefault("momentum", 0);

                scores.Add((zodiac, finalScore));
            }

            return scores.OrderByDescending(s => s.score).ThenBy(s => s.zodiac).Select(s => s.zodiac).ToList();
        }

        // ===== 计算敏感度指标 =====
        private static void ComputeSensitivityMetrics(ModelSensitivity sensitivity, PerturbationResult baseline)
        {
            if (sensitivity.Perturbations.Count == 0) return;

            // Top1敏感度 = 最大Top1率 - 最小Top1率
            var top1Rates = sensitivity.Perturbations.Select(p => p.Top1Rate).ToList();
            sensitivity.Top1Sensitivity = top1Rates.Max() - top1Rates.Min();

            var top3Rates = sensitivity.Perturbations.Select(p => p.Top3Rate).ToList();
            sensitivity.Top3Sensitivity = top3Rates.Max() - top3Rates.Min();

            var scores = sensitivity.Perturbations.Select(p => p.ComprehensiveScore).ToList();
            sensitivity.ComprehensiveSensitivity = scores.Max() - scores.Min();

            // 判定级别
            if (sensitivity.ComprehensiveSensitivity < 3.0)
                sensitivity.SensitivityLevel = "低 🟢";
            else if (sensitivity.ComprehensiveSensitivity < 10.0)
                sensitivity.SensitivityLevel = "中 🟡";
            else
                sensitivity.SensitivityLevel = "高 🔴";
        }

        // ===== 生成结论 =====
        private static List<string> GenerateConclusions(SensitivityReport report)
        {
            var conclusions = new List<string>();

            // 最敏感模型
            var mostSensitive = report.Models.OrderByDescending(m => m.ComprehensiveSensitivity).First();
            conclusions.Add($"最敏感的模型是 [{mostSensitive.ChineseName}]，综合评分波动 {mostSensitive.ComprehensiveSensitivity:F2}（{mostSensitive.SensitivityLevel}），说明该模型的权重设置对整体预测影响最大。");

            // 最不敏感模型
            var leastSensitive = report.Models.OrderBy(m => m.ComprehensiveSensitivity).First();
            conclusions.Add($"最不敏感的模型是 [{leastSensitive.ChineseName}]，综合评分波动 {leastSensitive.ComprehensiveSensitivity:F2}（{leastSensitive.SensitivityLevel}），说明权重变化对结果影响较小。");

            // 是否有"最佳权重"组合
            foreach (var model in report.Models)
            {
                var bestPerturb = model.Perturbations.OrderByDescending(p => p.ComprehensiveScore).First();
                if (bestPerturb.ComprehensiveScore > report.Baseline.ComprehensiveScore + 1.0)
                {
                    conclusions.Add($"发现改进点：{bestPerturb.Label} 的综合评分 ({bestPerturb.ComprehensiveScore:F2}) 优于基线 ({report.Baseline.ComprehensiveScore:F2})，提升 {bestPerturb.ComprehensiveScore - report.Baseline.ComprehensiveScore:F2}。");
                }
            }

            // 权重稳定性总体评估
            double avgSensitivity = report.Models.Average(m => m.ComprehensiveSensitivity);
            if (avgSensitivity < 3.0)
                conclusions.Add("总体评估：权重系统非常稳定，±30%的扰动对预测结果影响有限。当前权重选择合理。");
            else if (avgSensitivity < 10.0)
                conclusions.Add("总体评估：权重系统基本稳定，但个别模型对权重变化较敏感，建议在敏感模型上谨慎调参。");
            else
                conclusions.Add("总体评估：权重系统不够稳定，权重的微小变化会导致预测结果显著波动，建议对集成策略进行结构性优化。");

            return conclusions;
        }

        // ===== 生成报告文本 =====
        public static string GenerateReport(SensitivityReport report)
        {
            var sb = new System.Text.StringBuilder();
            int padding = 24;

            sb.AppendLine("═══════════════════════════════════════════════════════════════════════");
            sb.AppendLine("           v6.8 权重敏感性分析报告 (Weight Sensitivity Analysis)");
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════");
            sb.AppendLine($"分析时间：{report.AnalysisTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"数据库：{report.DatabasePath}");
            sb.AppendLine($"历史数据总量：{report.TotalHistoryRecords} 期");
            sb.AppendLine($"训练期数：{report.TrainPeriods} 期");
            sb.AppendLine($"测试期数：{report.TestPeriods} 期");
            sb.AppendLine();
            sb.AppendLine("原始权重：");
            foreach (var kv in BaseWeights)
                sb.AppendLine($"  {GetModelChineseName(kv.Key),-20} {kv.Value * 100,4:F0}%");
            sb.AppendLine();
            sb.AppendLine("扰动方案：对每个模型分别进行 ±10%, ±20%, ±30% 权重扰动后重新归一化");
            sb.AppendLine();

            // ===== 基线结果 =====
            sb.AppendLine("═══════════════════════════ 基线结果 ═══════════════════════════");
            sb.AppendLine($"Top1率:{report.Baseline.Top1Rate,8:F1}%  Top3率:{report.Baseline.Top3Rate,8:F1}%  " +
                         $"Top6率:{report.Baseline.Top6Rate,8:F1}%  综合:{report.Baseline.ComprehensiveScore,8:F2}  " +
                         $"最大连败:{report.Baseline.MaxConsecutiveMiss}");
            sb.AppendLine();

            // ===== 各模型详细分析 =====
            foreach (var model in report.Models)
            {
                sb.AppendLine($"══════════ {model.ChineseName} (原始权重 {model.OriginalWeight * 100:F0}%) ══════════");
                sb.AppendLine($"{"扰动",-15} {"权重",7} {"Top1",7} {"Top3",7} {"Top6",7} {"综合",8} {"连败",5} {"Δ综合vs基线",10}");
                sb.AppendLine(new string('─', 75));

                // 基线行
                double baseScore = report.Baseline.ComprehensiveScore;

                foreach (var p in model.Perturbations)
                {
                    double weightPct = p.Weights.GetValueOrDefault(model.ModelName, 0) * 100;
                    double deltaScore = p.ComprehensiveScore - baseScore;
                    sb.AppendLine(
                        $"{p.Label,-15} {weightPct,6:F0}% " +
                        $"{p.Top1Rate,6:F1}% {p.Top3Rate,6:F1}% {p.Top6Rate,6:F1}% " +
                        $"{p.ComprehensiveScore,7:F2} {p.MaxConsecutiveMiss,4} " +
                        $"{deltaScore,+9:F2}");
                }

                sb.AppendLine(new string('─', 75));
                sb.AppendLine($"  敏感度指标：Top1波动 {model.Top1Sensitivity:F1}% | Top3波动 {model.Top3Sensitivity:F1}% | 综合波动 {model.ComprehensiveSensitivity:F2} → {model.SensitivityLevel}");
                sb.AppendLine();
            }

            // ===== 对比汇总 =====
            sb.AppendLine("═══════════════════════════ 模型敏感度对比 ═══════════════════════════");
            sb.AppendLine($"{"模型",-20} {"原始权重",7} {"Top1波动",8} {"Top3波动",8} {"综合波动",8} {"级别",-6}");
            sb.AppendLine(new string('─', 65));

            foreach (var model in report.Models.OrderByDescending(m => m.ComprehensiveSensitivity))
            {
                sb.AppendLine(
                    $"{model.ChineseName,-20} {model.OriginalWeight * 100,6:F0}% " +
                    $"{model.Top1Sensitivity,7:F1}% {model.Top3Sensitivity,7:F1}% " +
                    $"{model.ComprehensiveSensitivity,7:F2}   {model.SensitivityLevel}");
            }

            sb.AppendLine();

            // ===== 结论 =====
            sb.AppendLine("═══════════════════════════ 分析结论 ═══════════════════════════");
            foreach (var c in report.OverallConclusions)
                sb.AppendLine($"  • {c}");

            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════");
            sb.AppendLine("注：综合评分 = Top1×1.0 + Top3×0.5 + Top6×0.1");
            sb.AppendLine("注：所有实验使用同一 history.db，训练300期，测试200期。");
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════");

            return sb.ToString();
        }

        // ===== 子模型评分（与 EnsemblePredictionService 一致）=====

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
            return model.ToLower() switch
            {
                "frequency" => "FrequencyModel(频率)",
                "trend" => "TrendModel(趋势)",
                "missing" => "MissingModel(遗漏)",
                "pattern" => "PatternModel(模式)",
                "momentum" => "MomentumModel(动量)",
                _ => model
            };
        }
    }
}
