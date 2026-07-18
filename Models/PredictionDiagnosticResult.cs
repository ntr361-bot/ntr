using System.Collections.Generic;

namespace 六合分析软件
{
    public class PredictionContribution
    {
        public string Zodiac { get; set; } = "";
        public int Rank { get; set; }
        public double FrequencyScore { get; set; }
        public double TrendScore { get; set; }
        public double MissingScore { get; set; }
        public double PatternScore { get; set; }
        public double MomentumScore { get; set; }
        public double FrequencyContribution { get; set; }
        public double TrendContribution { get; set; }
        public double MissingContribution { get; set; }
        public double PatternContribution { get; set; }
        public double MomentumContribution { get; set; }
        public double TotalScore { get; set; }
    }

    public class PredictionExplainResult
    {
        public string Period { get; set; } = "";
        public string ActualZodiac { get; set; } = "";
        public List<string> Top3 { get; set; } = new List<string>();
        public List<string> Top6 { get; set; } = new List<string>();
        public bool Top3Hit { get; set; }
        public bool Top6Hit { get; set; }
        public List<PredictionContribution> Contributions { get; set; } = new List<PredictionContribution>();
    }

    public class PredictionErrorDetail
    {
        public string Period { get; set; } = "";
        public string ActualZodiac { get; set; } = "";
        public List<string> PredictedTop3 { get; set; } = new List<string>();
        public int ActualRank { get; set; }
        public string ErrorType { get; set; } = "";
        public double ContributionGap { get; set; }
    }

    public class ErrorAnalysisResult
    {
        public int TotalPredictions { get; set; }
        public int Top3Failures { get; set; }
        public Dictionary<string, int> FailureTypeCounts { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, double> FailureTypeRates { get; set; } = new Dictionary<string, double>();
        public List<PredictionErrorDetail> Details { get; set; } = new List<PredictionErrorDetail>();
    }
}
