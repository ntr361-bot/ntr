using System.Collections.Generic;

namespace 六合分析软件
{
    public class PredictionPenaltyResult
    {
        public Dictionary<string, double> PenaltyScores { get; set; } = new Dictionary<string, double>();
        public Dictionary<string, List<string>> Reasons { get; set; } = new Dictionary<string, List<string>>();
    }
}
