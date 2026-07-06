using System.IO;
using System.Text.Json;

namespace PromptAssistant.Services
{
    public static class StatsService
    {
        private static readonly string StatsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PromptAssistant", "stats.json");

        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        private static Dictionary<string, int> _daily = new();

        public static void Load()
        {
            try
            {
                if (File.Exists(StatsPath))
                    _daily = JsonSerializer.Deserialize<Dictionary<string, int>>(
                        File.ReadAllText(StatsPath), JsonOpts) ?? new();
            }
            catch { }
        }

        public static void RecordExecution()
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            _daily[today] = _daily.TryGetValue(today, out var n) ? n + 1 : 1;
            Save();
        }

        public static int GetToday()
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            return _daily.TryGetValue(today, out var n) ? n : 0;
        }

        public static int GetWeekly()
        {
            var total = 0;
            for (int i = 0; i < 7; i++)
            {
                var key = DateTime.Today.AddDays(-i).ToString("yyyy-MM-dd");
                if (_daily.TryGetValue(key, out var n)) total += n;
            }
            return total;
        }

        public static int GetTotal() => _daily.Values.Sum();

        public static IReadOnlyDictionary<string, int> GetDailyCounts() => _daily;

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StatsPath)!);
                File.WriteAllText(StatsPath, JsonSerializer.Serialize(_daily, JsonOpts));
            }
            catch { }
        }
    }
}
