using System;
using System.Collections.Generic;
using System.Linq;

namespace 六合分析软件
{
    public static class PredictionExplainService
    {
        private static readonly string[] Zodiacs =
        {
            "鼠", "牛", "虎", "兔", "龙", "蛇",
            "马", "羊", "猴", "鸡", "狗", "猪"
        };

        public static PredictionExplainResult Explain(
            List<DatabaseHelper.HistoryRecord> newestFirstHistory,
            string period,
            string actualZodiac)
        {
            var contributions = Zodiacs
                .Select(zodiac => CalculateContribution(newestFirstHistory, zodiac))
                .OrderByDescending(x => x.TotalScore)
                .ThenBy(x => x.Zodiac)
                .ToList();

            for (int i = 0; i < contributions.Count; i++)
                contributions[i].Rank = i + 1;

            var result = new PredictionExplainResult
            {
                Period = period,
                ActualZodiac = actualZodiac,
                Contributions = contributions,
                Top3 = contributions.Take(3).Select(x => x.Zodiac).ToList(),
                Top6 = contributions.Take(6).Select(x => x.Zodiac).ToList()
            };
            result.Top3Hit = result.Top3.Contains(actualZodiac);
            result.Top6Hit = result.Top6.Contains(actualZodiac);
            return result;
        }

        private static PredictionContribution CalculateContribution(
            List<DatabaseHelper.HistoryRecord> history,
            string zodiac)
        {
            int total = history.Count;
            if (total == 0) return new PredictionContribution { Zodiac = zodiac };

            int appear = history.Count(h => h.SpecialZodiac == zodiac);
            double frequency = Math.Min(100, (double)appear / total * 12 * 100);

            int recent10 = history.Take(Math.Min(10, total)).Count(h => h.SpecialZodiac == zodiac);
            int recent30 = history.Take(Math.Min(30, total)).Count(h => h.SpecialZodiac == zodiac);
            int previous30 = history.Skip(30).Take(30).Count(h => h.SpecialZodiac == zodiac);
            double trend = Math.Min(100, ((double)recent10 / Math.Min(10, total) * 0.55 +
                (double)recent30 / Math.Min(30, total) * 0.30 +
                Math.Max(0, recent30 - previous30) / 30.0 * 0.15) * 12 * 100);

            int omission = history.TakeWhile(h => h.SpecialZodiac != zodiac).Count();
            var intervals = GetIntervals(history, zodiac);
            double avgInterval = intervals.Count > 0 ? intervals.Average() : 12;
            double omissionRatio = omission / Math.Max(1, avgInterval);
            double missing = omissionRatio >= 0.8 && omissionRatio <= 1.6
                ? 90
                : Math.Max(10, 90 - Math.Abs(omissionRatio - 1.2) * 40);

            double pattern = 45;
            if (intervals.Count >= 3)
            {
                double avg = intervals.Average();
                double variance = intervals.Select(i => Math.Pow(i - avg, 2)).Average();
                double regularity = Math.Max(0, 100 - Math.Sqrt(variance) / Math.Max(1, avg) * 100);
                double match = Math.Max(0, 100 - Math.Abs(omission - avg) / Math.Max(1, avg) * 100);
                pattern = regularity * 0.45 + match * 0.55;
            }

            int recent20 = history.Take(Math.Min(20, total)).Count(h => h.SpecialZodiac == zodiac);
            double momentum = recent20 > 0
                ? Math.Max(0, Math.Min(100, (double)recent10 / recent20 * 100))
                : 50;

            var result = new PredictionContribution
            {
                Zodiac = zodiac,
                FrequencyScore = frequency,
                TrendScore = trend,
                MissingScore = missing,
                PatternScore = pattern,
                MomentumScore = momentum,
                FrequencyContribution = frequency * 0.40,
                TrendContribution = trend * 0.10,
                MissingContribution = missing * 0.40,
                PatternContribution = pattern * 0.10,
                MomentumContribution = 0
            };
            result.TotalScore = result.FrequencyContribution + result.TrendContribution +
                result.MissingContribution + result.PatternContribution + result.MomentumContribution;
            return result;
        }

        private static List<int> GetIntervals(List<DatabaseHelper.HistoryRecord> history, string zodiac)
        {
            var intervals = new List<int>();
            int last = -1;
            for (int i = 0; i < history.Count; i++)
            {
                if (history[i].SpecialZodiac != zodiac) continue;
                if (last >= 0) intervals.Add(i - last);
                last = i;
            }
            return intervals;
        }
    }
}
