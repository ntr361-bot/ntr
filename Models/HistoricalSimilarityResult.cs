using System.Collections.Generic;

namespace 六合分析软件
{
    public class HistoricalSimilarityResult
    {
        public Dictionary<string, double> SimilarityScores { get; set; } = new Dictionary<string, double>();
        public Dictionary<string, int> SimilarAppearCounts { get; set; } = new Dictionary<string, int>();
        public int MatchedCycles { get; set; }
    }
}
