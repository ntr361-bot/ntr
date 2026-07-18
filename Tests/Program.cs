using System.Text.Json;
using 六合分析软件;

string testData = Path.Combine(AppContext.BaseDirectory, "TestData");
if (Directory.Exists(testData)) Directory.Delete(testData, recursive: true);
Environment.SetEnvironmentVariable("LIUHE_DATA_DIR", testData);
DatabaseHelper.InitializeDatabase();
SeedHistory();

var tests = new (string Name, Action Run)[]
{
    ("解析有效开奖 JSON", ParseValidJson),
    ("拒绝失败 API 响应", RejectFailedResponse),
    ("号码统计正确", CountNumbers),
    ("预测缓存跨进程序列化", PredictionCacheRoundTrip),
    ("自动识别下一期", AutoDetectNextIssue),
    ("指定期号运行", ExplicitIssue),
    ("已存在文件时跳过", ExistingFileSkips),
    ("强制覆盖", ForceOverwrite),
    ("历史数据为空", EmptyHistoryFails),
    ("历史数据格式错误", InvalidHistoryFails),
    ("输出 JSON 校验", OutputJsonIsValid),
    ("latest.json 更新", LatestJsonUpdates),
    ("重复期号检测", DuplicateIssueFails),
    ("dry-run 不修改文件", DryRunDoesNotWrite)
};

int failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

return failures == 0 ? 0 : 1;

void SeedHistory()
{
    DatabaseHelper.ClearHistory();
    DatabaseHelper.InsertHistory("100", "010203040506", "07", "马", "2026-01-01 21:30:00", "2026-01-01");
    DatabaseHelper.InsertHistory("101", "010203040506", "08", "蛇", "2026-01-03 21:30:00", "2026-01-03");
    DatabaseHelper.InsertHistory("102", "010203040506", "09", "龙", "2026-01-05 21:30:00", "2026-01-05");
}

void ParseValidJson()
{
    const string json = """
        {"code":0,"message":"ok","data":[{"issue":"2026001","openCode":"01,02,03,04,05,06,07","openTime":"2026-01-01 21:30:00","pet":"马"}]}
        """;
    var records = DataCrawler.ParseJson(json);
    Assert(records.Count == 1, "应解析出一条记录");
    Assert(records[0].Period == "2026001", "期号不正确");
    Assert(records[0].SpecialNumber == "07", "特码不正确");
}

void RejectFailedResponse() => Assert(
    DataCrawler.ParseJson("{\"code\":500,\"message\":\"failed\",\"data\":[]}").Count == 0,
    "失败响应不应产生记录");

void CountNumbers()
{
    var counts = AnalysisEngine.CountNumbers(new List<string> { "01", "02", "03", "02", "03", "04" });
    Assert(counts["02"] == 2 && counts["04"] == 1, "号码频次计算不正确");
}

void PredictionCacheRoundTrip()
{
    const string cacheName = "smoke-prediction-cache";
    JsonFileCache.RemoveByPrefix(cacheName);
    try
    {
        var expected = FakePrediction(103);
        JsonFileCache.Save(cacheName, "key-1", expected);
        Assert(JsonFileCache.TryLoad<AIEngine.PredictResult>(cacheName, "key-1", out var loaded), "应命中相同键缓存");
        Assert(loaded?.PredictPeriod == "103" && loaded.Top3.Count == 3, "缓存内容不完整");
        Assert(!JsonFileCache.TryLoad<AIEngine.PredictResult>(cacheName, "key-2", out _), "不同键不应命中缓存");
    }
    finally { JsonFileCache.RemoveByPrefix(cacheName); }
}

void AutoDetectNextIssue()
{
    string output = FreshDirectory();
    PredictionRunResult result = PredictionAutomation.Run(new() { DryRun = true, OutputDirectory = output });
    Assert(result.Issue == 103, "下一期应为 103");
}

void ExplicitIssue()
{
    PredictionRunResult result = PredictionAutomation.Run(new() { Issue = 110, DryRun = true, OutputDirectory = FreshDirectory() });
    Assert(result.Issue == 110, "应使用指定期号");
}

void ExistingFileSkips()
{
    string output = FreshDirectory();
    File.WriteAllText(Path.Combine(output, "103.json"), "{}");
    PredictionRunResult result = PredictionAutomation.Run(new() { OutputDirectory = output }, _ => throw new Exception("不应调用预测器"));
    Assert(result.Status == "skipped" && !result.Changed, "已存在文件应跳过");
}

void ForceOverwrite()
{
    string output = FreshDirectory();
    File.WriteAllText(Path.Combine(output, "103.json"), "{}");
    PredictionRunResult result = PredictionAutomation.Run(new() { OutputDirectory = output, Force = true }, FakePrediction);
    Assert(result.Changed, "强制模式应覆盖文件");
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "103.json")));
    Assert(document.RootElement.GetProperty("status").GetString() == "success", "覆盖后的状态无效");
}

void EmptyHistoryFails() => AssertThrows<InvalidDataException>(
    () => PredictionAutomation.ValidateHistory(Array.Empty<DatabaseHelper.HistoryRecord>()), "空历史应失败");

void InvalidHistoryFails() => AssertThrows<InvalidDataException>(() => PredictionAutomation.ValidateHistory(new[]
{
    History("100", "not-a-number", "马")
}), "非法号码应失败");

void OutputJsonIsValid()
{
    string output = FreshDirectory();
    PredictionAutomation.Run(new() { OutputDirectory = output }, FakePrediction);
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "103.json")));
    JsonElement prediction = document.RootElement.GetProperty("prediction");
    Assert(prediction.GetProperty("zodiacs").GetArrayLength() == 3, "生肖输出为空");
    Assert(prediction.GetProperty("numbers").GetArrayLength() == 3, "号码输出为空");
}

void LatestJsonUpdates()
{
    string output = FreshDirectory();
    PredictionAutomation.Run(new() { OutputDirectory = output }, FakePrediction);
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "latest.json")));
    Assert(document.RootElement.GetProperty("latest_issue").GetInt64() == 103, "latest.json 期号错误");
    Assert(document.RootElement.GetProperty("prediction_file").GetString() == "103.json", "latest.json 文件名错误");
}

void DuplicateIssueFails() => AssertThrows<InvalidDataException>(() => PredictionAutomation.ValidateHistory(new[]
{
    History("100", "07", "马"), History("100", "08", "蛇")
}), "重复期号应失败");

void DryRunDoesNotWrite()
{
    string output = FreshDirectory();
    PredictionAutomation.Run(new() { OutputDirectory = output, DryRun = true });
    Assert(!Directory.EnumerateFileSystemEntries(output).Any(), "dry-run 不应写文件");
}

AIEngine.PredictResult FakePrediction(long issue) => new()
{
    PredictPeriod = issue.ToString(),
    PredictTime = DateTime.Now,
    AnalysisPeriods = 3,
    Top3 = new() { "马", "蛇", "龙" },
    Top6 = new() { "马", "蛇", "龙", "兔", "虎", "牛" },
    RecommendedNumbers = new() { 7, 8, 9 },
    Confidence = "测试",
    BestModel = "fake"
};

DatabaseHelper.HistoryRecord History(string issue, string number, string zodiac) => new()
{
    Period = issue,
    SpecialNumber = number,
    SpecialZodiac = zodiac
};

string FreshDirectory()
{
    string path = Path.Combine(testData, "output", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

void AssertThrows<T>(Action action, string message) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException(message);
}

void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
