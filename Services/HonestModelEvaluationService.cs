using System.Text;

namespace 六合分析软件;

/// <summary>
/// 严格按时间顺序评估模型。候选方案只在开发集内选择，最后100期只验收一次。
/// </summary>
public static class HonestModelEvaluationService
{
    private const int AnalysisPeriods = 100;

    private static readonly ZodiacPredictEngineV2.WeightConfig Balanced = new()
    {
        FrequencyWeight = 0.20,
        RecentTrendWeight = 0.20,
        OmissionWeight = 0.20,
        HotColdWeight = 0.15,
        PeriodPatternWeight = 0.15,
        ConsecutiveWeight = 0.10
    };

    private static readonly ZodiacPredictEngineV2.WeightConfig HeavyMissing = new()
    {
        FrequencyWeight = 0.15,
        RecentTrendWeight = 0.15,
        OmissionWeight = 0.30,
        HotColdWeight = 0.15,
        PeriodPatternWeight = 0.15,
        ConsecutiveWeight = 0.10
    };

    private static readonly ZodiacPredictEngineV2.WeightConfig HeavyTrend = new()
    {
        FrequencyWeight = 0.15,
        RecentTrendWeight = 0.30,
        OmissionWeight = 0.15,
        HotColdWeight = 0.15,
        PeriodPatternWeight = 0.15,
        ConsecutiveWeight = 0.10
    };

    private static readonly ZodiacPredictEngineV2.WeightConfig HeavyHotCold = new()
    {
        FrequencyWeight = 0.15,
        RecentTrendWeight = 0.15,
        OmissionWeight = 0.15,
        HotColdWeight = 0.25,
        PeriodPatternWeight = 0.20,
        ConsecutiveWeight = 0.10
    };

    private static readonly ZodiacPredictEngineV2.WeightConfig HeavyPattern = new()
    {
        FrequencyWeight = 0.10,
        RecentTrendWeight = 0.15,
        OmissionWeight = 0.15,
        HotColdWeight = 0.15,
        PeriodPatternWeight = 0.30,
        ConsecutiveWeight = 0.15
    };

    private static readonly ZodiacPredictEngineV2.WeightConfig LegacyV6 = new()
    {
        FrequencyWeight = 0.40,
        RecentTrendWeight = 0.10,
        OmissionWeight = 0.40,
        HotColdWeight = 0,
        PeriodPatternWeight = 0.10,
        ConsecutiveWeight = 0
    };

    public static HonestEvaluationReport Run(int totalPeriods = 500, int holdoutPeriods = 100)
    {
        var history = WeightOptimizationService.GetValidHistoryOldToNew(totalPeriods);
        if (history.Count < 400)
            throw new InvalidOperationException($"真实历史数据不足：至少需要400期，实际{history.Count}期");
        if (holdoutPeriods < 50 || history.Count - holdoutPeriods < 250)
            throw new ArgumentOutOfRangeException(nameof(holdoutPeriods), "独立验收期数必须至少50期，并保留至少250期开发数据");

        int developmentCount = history.Count - holdoutPeriods;
        int developmentTestPeriods = Math.Min(150, developmentCount - 200);
        int developmentTrainPeriods = developmentCount - developmentTestPeriods;
        var development = history.Take(developmentCount).ToList();

        // V6.2权重只查看独立验收区之前的数据。
        var optimizerTraining = development.TakeLast(Math.Min(300, development.Count)).ToList();
        var optimized = WeightOptimizationService.FindBestWeightsFromTrainingData(
            optimizerTraining, minimumTrainPeriods: 100);

        var predictors = BuildPredictors(optimized.TotalTests > 0 ? optimized.Weights : Balanced);
        var developmentScores = predictors.ToDictionary(
            item => item.Key,
            item => WeightOptimizationService.EvaluatePredictor(
                development, developmentTrainPeriods, developmentTestPeriods, item.Key, item.Value));

        string selectedCandidate = developmentScores
            .Where(item => item.Key != "现行V6.1自适应100期")
            .OrderByDescending(item => item.Value.CombinedScore)
            .ThenByDescending(item => item.Value.Top3HitRate)
            .First().Key;

        var holdoutScores = predictors.ToDictionary(
            item => item.Key,
            item => WeightOptimizationService.EvaluatePredictor(
                history, developmentCount, holdoutPeriods, item.Key, item.Value));

        var firstHalfScores = EvaluateSlice(history, developmentCount, holdoutPeriods / 2, predictors);
        var secondHalfScores = EvaluateSlice(
            history, developmentCount + holdoutPeriods / 2,
            holdoutPeriods - holdoutPeriods / 2, predictors);

        var baseline = holdoutScores["现行V6.1自适应100期"];
        var candidate = holdoutScores[selectedCandidate];
        bool improvesReliably = candidate.Top3HitRate >= baseline.Top3HitRate + 3.0 &&
            candidate.Top6HitRate >= baseline.Top6HitRate &&
            firstHalfScores[selectedCandidate].Top3HitRate >=
                firstHalfScores["现行V6.1自适应100期"].Top3HitRate - 4.0 &&
            secondHalfScores[selectedCandidate].Top3HitRate >=
                secondHalfScores["现行V6.1自适应100期"].Top3HitRate - 4.0;

        return new HonestEvaluationReport
        {
            TotalRecords = history.Count,
            FirstIssue = history[0].Period,
            LastIssue = history[^1].Period,
            DevelopmentRecords = developmentCount,
            DevelopmentTestRecords = developmentTestPeriods,
            HoldoutRecords = holdoutPeriods,
            HoldoutFirstIssue = history[developmentCount].Period,
            HoldoutLastIssue = history[^1].Period,
            SelectedCandidate = selectedCandidate,
            OptimizedWeights = optimized.TotalTests > 0 ? FormatWeights(optimized.Weights) : "数据不足，使用均衡权重",
            DevelopmentScores = developmentScores,
            HoldoutScores = holdoutScores,
            FirstHalfScores = firstHalfScores,
            SecondHalfScores = secondHalfScores,
            RecommendReplacement = improvesReliably
        };
    }

    public static string GenerateReport(int totalPeriods = 500, int holdoutPeriods = 100)
    {
        HonestEvaluationReport report = Run(totalPeriods, holdoutPeriods);
        var sb = new StringBuilder();
        sb.AppendLine("六合分析软件 真实历史独立验算报告");
        sb.AppendLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"真实数据：{report.TotalRecords}期（{report.FirstIssue} 至 {report.LastIssue}）");
        sb.AppendLine($"开发区：前{report.DevelopmentRecords}期；其中最后{report.DevelopmentTestRecords}期用于候选筛选");
        sb.AppendLine($"独立验收区：最后{report.HoldoutRecords}期（{report.HoldoutFirstIssue} 至 {report.HoldoutLastIssue}）");
        sb.AppendLine("规则：每期只使用该期之前的记录；所有模型固定使用最近100期；独立验收区不参与权重和候选选择。");
        sb.AppendLine("理论随机基准：Top1 8.33%，Top3 25.00%，Top6 50.00%。");
        sb.AppendLine($"V6.2开发集优化权重：{report.OptimizedWeights}");
        sb.AppendLine();

        sb.AppendLine("开发区候选筛选结果：");
        AppendScores(sb, report.DevelopmentScores);
        sb.AppendLine($"开发区预先选出的候选：{report.SelectedCandidate}");
        sb.AppendLine();

        sb.AppendLine("最后100期独立验收结果：");
        AppendScores(sb, report.HoldoutScores);
        sb.AppendLine();
        sb.AppendLine("独立验收稳定性（前50期 / 后50期 Top3）：");
        foreach (string name in report.HoldoutScores.Keys)
        {
            sb.AppendLine($"{name}：{report.FirstHalfScores[name].Top3HitRate:F2}% / " +
                $"{report.SecondHalfScores[name].Top3HitRate:F2}%");
        }
        sb.AppendLine();

        var baseline = report.HoldoutScores["现行V6.1自适应100期"];
        var candidate = report.HoldoutScores[report.SelectedCandidate];
        sb.AppendLine($"预选候选相对现行模型：Top1 {candidate.Top1HitRate - baseline.Top1HitRate:+0.00;-0.00;0.00}，" +
            $"Top3 {candidate.Top3HitRate - baseline.Top3HitRate:+0.00;-0.00;0.00}，" +
            $"Top6 {candidate.Top6HitRate - baseline.Top6HitRate:+0.00;-0.00;0.00} 个百分点。");
        sb.AppendLine(report.RecommendReplacement
            ? "结论：候选方案达到预设替换条件，可进入下一轮前瞻观察；本报告不会自动修改正式模型。"
            : "结论：候选方案未达到预设替换条件，保留现行模型；不根据验收结果事后挑选赢家。" );
        sb.AppendLine("注意：彩票结果具有高度随机性，历史命中率不能保证下一期结果。样本只有100期时，小幅差异可能只是随机波动。");
        return sb.ToString();
    }

    private static Dictionary<string, Func<List<DatabaseHelper.HistoryRecord>, List<(string zodiac, double score)>>> BuildPredictors(
        ZodiacPredictEngineV2.WeightConfig optimizedWeights)
    {
        return new Dictionary<string, Func<List<DatabaseHelper.HistoryRecord>, List<(string zodiac, double score)>>>
        {
            ["现行V6.1自适应100期"] = history => ZodiacPredictEngineV2.RankHistory(Window(history)),
            ["固定均衡权重"] = history => ZodiacPredictEngineV2.RankHistory(Window(history), Balanced),
            ["重遗漏"] = history => ZodiacPredictEngineV2.RankHistory(Window(history), HeavyMissing),
            ["重走势"] = history => ZodiacPredictEngineV2.RankHistory(Window(history), HeavyTrend),
            ["重冷热"] = history => ZodiacPredictEngineV2.RankHistory(Window(history), HeavyHotCold),
            ["重周期"] = history => ZodiacPredictEngineV2.RankHistory(Window(history), HeavyPattern),
            ["原V6权重"] = history => ZodiacPredictEngineV2.RankHistory(Window(history), LegacyV6),
            ["V6.2开发集优化权重"] = history => ZodiacPredictEngineV2.RankHistory(Window(history), optimizedWeights),
            ["V6.4动态权重"] = history => DynamicWeightService.RankByDynamicWeights(Window(history)),
            ["V6.6遗漏可靠度"] = history => MissingReliabilityService.RankWithReliability(Window(history))
        };
    }

    private static List<DatabaseHelper.HistoryRecord> Window(List<DatabaseHelper.HistoryRecord> newestFirstHistory) =>
        newestFirstHistory.Take(AnalysisPeriods).ToList();

    private static Dictionary<string, ModelScoreResult> EvaluateSlice(
        List<DatabaseHelper.HistoryRecord> history,
        int trainPeriods,
        int testPeriods,
        Dictionary<string, Func<List<DatabaseHelper.HistoryRecord>, List<(string zodiac, double score)>>> predictors) =>
        predictors.ToDictionary(
            item => item.Key,
            item => WeightOptimizationService.EvaluatePredictor(
                history, trainPeriods, testPeriods, item.Key, item.Value));

    private static void AppendScores(StringBuilder sb, Dictionary<string, ModelScoreResult> scores)
    {
        foreach (var item in scores.OrderByDescending(item => item.Value.CombinedScore))
        {
            ModelScoreResult score = item.Value;
            sb.AppendLine($"{item.Key}：Top1 {score.Top1HitRate:F2}% ({score.Top1Hits}/{score.TotalTests})，" +
                $"Top3 {score.Top3HitRate:F2}% ({score.Top3Hits}/{score.TotalTests})，" +
                $"Top6 {score.Top6HitRate:F2}% ({score.Top6Hits}/{score.TotalTests})，" +
                $"最大Top3连败 {score.MaxConsecutiveMisses}，综合 {score.CombinedScore:F2}");
        }
    }

    private static string FormatWeights(ZodiacPredictEngineV2.WeightConfig weights) =>
        $"频率{weights.FrequencyWeight:P0}、走势{weights.RecentTrendWeight:P0}、遗漏{weights.OmissionWeight:P0}、" +
        $"冷热{weights.HotColdWeight:P0}、周期{weights.PeriodPatternWeight + weights.ConsecutiveWeight:P0}";
}

public sealed class HonestEvaluationReport
{
    public int TotalRecords { get; init; }
    public string FirstIssue { get; init; } = "";
    public string LastIssue { get; init; } = "";
    public int DevelopmentRecords { get; init; }
    public int DevelopmentTestRecords { get; init; }
    public int HoldoutRecords { get; init; }
    public string HoldoutFirstIssue { get; init; } = "";
    public string HoldoutLastIssue { get; init; } = "";
    public string SelectedCandidate { get; init; } = "";
    public string OptimizedWeights { get; init; } = "";
    public Dictionary<string, ModelScoreResult> DevelopmentScores { get; init; } = new();
    public Dictionary<string, ModelScoreResult> HoldoutScores { get; init; } = new();
    public Dictionary<string, ModelScoreResult> FirstHalfScores { get; init; } = new();
    public Dictionary<string, ModelScoreResult> SecondHalfScores { get; init; } = new();
    public bool RecommendReplacement { get; init; }
}
