using 六合分析软件;

try
{
    Dictionary<string, string?> arguments = ParseArguments(args);
    if (arguments.ContainsKey("help"))
    {
        PrintUsage();
        return 0;
    }

    string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    string dataDirectory = Environment.GetEnvironmentVariable("LIUHE_DATA_DIR")
        ?? Path.Combine(repositoryRoot, "data");
    string outputDirectory = Environment.GetEnvironmentVariable("PREDICTION_OUTPUT_DIR")
        ?? Path.Combine(repositoryRoot, "site", "data", "predictions");
    string dailyOutputDirectory = Environment.GetEnvironmentVariable("DAILY_PREDICTION_OUTPUT_DIR")
        ?? Path.Combine(repositoryRoot, "site", "data", "daily-records");
    Environment.SetEnvironmentVariable("LIUHE_DATA_DIR", dataDirectory);

    if (arguments.ContainsKey("evaluate-models"))
    {
        DatabaseHelper.InitializeDatabase();
        string report = HonestModelEvaluationService.GenerateReport();
        string outputPath = arguments.TryGetValue("report-output", out string? configuredOutput) &&
            !string.IsNullOrWhiteSpace(configuredOutput)
            ? Path.GetFullPath(configuredOutput)
            : Path.Combine(repositoryRoot, "model-evaluation-report.txt");
        File.WriteAllText(outputPath, report, System.Text.Encoding.UTF8);
        Console.WriteLine(report);
        Console.WriteLine($"[SUCCESS] 真实验算报告：{outputPath}");
        return 0;
    }

    if (arguments.ContainsKey("refresh-data"))
    {
        await LotteryDataRefresh.RefreshAsync(arguments.ContainsKey("dry-run"));
        if (arguments.ContainsKey("refresh-only"))
            return 0;
    }

    long? issue = null;
    if (arguments.TryGetValue("issue", out string? issueText))
    {
        if (!long.TryParse(issueText, out long parsed) || parsed <= 0)
            throw new ArgumentException("--issue 必须是正整数");
        issue = parsed;
    }

    if (arguments.ContainsKey("generate-all"))
    {
        DailyPredictionAutomation.GenerateMissing(outputDirectory, dailyOutputDirectory, issue,
            arguments.ContainsKey("force"), arguments.ContainsKey("dry-run"));
        if (!arguments.ContainsKey("dry-run"))
            CloudHistoryAutomation.Export(Path.Combine(repositoryRoot, "site", "data", "history.json"));
        return 0;
    }

    PredictionAutomation.Run(new PredictionRunOptions
    {
        Issue = issue,
        Force = arguments.ContainsKey("force"),
        DryRun = arguments.ContainsKey("dry-run"),
        OutputDirectory = outputDirectory
    });
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[ERROR] {ex.Message}");
    if (Environment.GetEnvironmentVariable("PREDICTION_DEBUG") == "1")
        Console.Error.WriteLine(ex);
    return 1;
}

static Dictionary<string, string?> ParseArguments(string[] values)
{
    var parsed = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < values.Length; i++)
    {
        string value = values[i];
        switch (value)
        {
            case "--issue":
                if (++i >= values.Length) throw new ArgumentException("--issue 缺少期号");
                parsed["issue"] = values[i];
                break;
            case "--force": parsed["force"] = null; break;
            case "--dry-run": parsed["dry-run"] = null; break;
            case "--refresh-data": parsed["refresh-data"] = null; break;
            case "--refresh-only": parsed["refresh-only"] = null; break;
            case "--generate-all": parsed["generate-all"] = null; break;
            case "--evaluate-models": parsed["evaluate-models"] = null; break;
            case "--report-output":
                if (++i >= values.Length) throw new ArgumentException("--report-output 缺少路径");
                parsed["report-output"] = values[i];
                break;
            case "--help":
            case "-h": parsed["help"] = null; break;
            default: throw new ArgumentException($"未知参数：{value}");
        }
    }
    return parsed;
}

static void PrintUsage()
{
    Console.WriteLine("用法：dotnet run --project PredictionRunner -- [--issue 199] [--force] [--dry-run] [--refresh-data] [--refresh-only] [--generate-all] [--evaluate-models] [--report-output 路径]");
}
