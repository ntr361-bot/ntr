using System;
using System.Collections.Generic;
using System.Linq;

namespace 六合分析软件
{
    public static class PredictionPenaltyService
    {
        private static readonly string[] Zodiacs =
        {
            "鼠", "牛", "虎", "兔", "龙", "蛇",
            "马", "羊", "猴", "鸡", "狗", "猪"
        };

        public static PredictionPenaltyResult Analyze(List<DatabaseHelper.HistoryRecord> newestFirstHistory)
        {
            var result = new PredictionPenaltyResult();
            foreach (string zodiac in Zodiacs)
            {
                result.PenaltyScores[zodiac] = 0;
                result.Reasons[zodiac] = new List<string>();
            }

            if (newestFirstHistory.Count < 30) return result;

            foreach (string zodiac in Zodiacs)
            {
                int recent5 = newestFirstHistory.Take(5).Count(h => h.SpecialZodiac == zodiac);
                int recent10 = newestFirstHistory.Take(10).Count(h => h.SpecialZodiac == zodiac);
                int recent30 = newestFirstHistory.Take(30).Count(h => h.SpecialZodiac == zodiac);
                int previous30 = newestFirstHistory.Skip(30).Take(30).Count(h => h.SpecialZodiac == zodiac);
                int omission = newestFirstHistory.TakeWhile(h => h.SpecialZodiac != zodiac).Count();
                var intervals = GetIntervals(newestFirstHistory, zodiac);
                double avgInterval = intervals.Count > 0 ? intervals.Average() : 12;

                if (recent5 >= 2)
                    AddPenalty(result, zodiac, 18 + (recent5 - 2) * 8, "最近5期过热");

                if (recent10 >= 3)
                    AddPenalty(result, zodiac, 12 + (recent10 - 3) * 5, "最近10期过热");

                if (omission > avgInterval * 2.2 && avgInterval > 0)
                    AddPenalty(result, zodiac, Math.Min(25, (omission / avgInterval - 2.0) * 12), "遗漏周期明显异常");

                if (previous30 > 0 && recent30 <= previous30 * 0.45)
                    AddPenalty(result, zodiac, 12, "短期趋势反转");

                result.PenaltyScores[zodiac] = Math.Min(100, result.PenaltyScores[zodiac]);
            }

            return result;
        }

        private static void AddPenalty(PredictionPenaltyResult result, string zodiac, double score, string reason)
        {
            result.PenaltyScores[zodiac] += Math.Max(0, score);
            result.Reasons[zodiac].Add(reason);
        }

        private static List<int> GetIntervals(List<DatabaseHelper.HistoryRecord> newestFirstHistory, string zodiac)
        {
            var intervals = new List<int>();
            int last = -1;
            for (int i = 0; i < newestFirstHistory.Count; i++)
            {
                if (newestFirstHistory[i].SpecialZodiac != zodiac) continue;
                if (last >= 0) intervals.Add(i - last);
                last = i;
            }
            return intervals;
        }
    }
}
