namespace 六合分析软件
{
    public class MissingReliabilityResult
    {
        public string Zodiac { get; set; } = "";
        public int OmissionLength { get; set; }
        public double EffectiveSamples { get; set; }
        public double RecoveryProbability { get; set; }
        public double ReliabilityScore { get; set; }
        public double OriginalMissingScore { get; set; }
        public double AdjustedMissingScore { get; set; }
    }
}
