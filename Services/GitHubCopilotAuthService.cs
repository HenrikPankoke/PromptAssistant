using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace PromptAssistant.Services
{
    /// <summary>
    /// Handles GitHub Device Flow OAuth to obtain a Copilot token.
    /// Same flow used by opencode and other CLI tools.
    /// </summary>
    public class GitHubCopilotAuthService
    {
        private const string ClientId = "Iv1.b507a08c87ecfe98"; // GitHub Copilot VSCode client ID (public)
        private const string Scope = "read:user";

        private static readonly HttpClient Http = new();

        public record DeviceCodeResult(string DeviceCode, string UserCode, string VerificationUri, int ExpiresIn, int Interval);
        public record TokenResult(string AccessToken, string? Error);

        // Step 1 — request device code
        public static async Task<DeviceCodeResult?> RequestDeviceCodeAsync()
        {
            var resp = await Http.PostAsync(
                "https://github.com/login/device/code",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId,
                    ["scope"] = Scope
                }));

            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync();
            var qs = ParseQs(body);

            if (!qs.TryGetValue("device_code", out var dc)) return null;

            return new DeviceCodeResult(
                dc,
                qs.GetValueOrDefault("user_code", ""),
                qs.GetValueOrDefault("verification_uri", "https://github.com/login/device"),
                int.TryParse(qs.GetValueOrDefault("expires_in", "900"), out var exp) ? exp : 900,
                int.TryParse(qs.GetValueOrDefault("interval", "5"), out var intv) ? intv : 5
            );
        }

        // Step 2 — poll for token
        public static async Task<TokenResult> PollForTokenAsync(DeviceCodeResult device, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow.AddSeconds(device.ExpiresIn);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                await Task.Delay(device.Interval * 1000, ct);

                var resp = await Http.PostAsync(
                    "https://github.com/login/oauth/access_token",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_id"] = ClientId,
                        ["device_code"] = device.DeviceCode,
                        ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                    }), ct);

                var body = await resp.Content.ReadAsStringAsync(ct);
                var qs = ParseQs(body);

                if (qs.TryGetValue("access_token", out var tok))
                    return new TokenResult(tok, null);

                var err = qs.GetValueOrDefault("error", "");
                if (err == "authorization_pending") continue;
                if (err == "slow_down") await Task.Delay(5000, ct);
                else return new TokenResult("", err);
            }
            return new TokenResult("", "expired");
        }

        // Verify token is valid for Copilot
        public static async Task<bool> VerifyTokenAsync(string token)
        {
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
                req.Headers.Add("Authorization", $"token {token}");
                req.Headers.Add("User-Agent", "PromptAssistant/1.0");
                var resp = await Http.SendAsync(req);
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public static void OpenBrowser(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        private static Dictionary<string, string> ParseQs(string qs) =>
            qs.Split('&')
              .Select(p => p.Split('=', 2))
              .Where(p => p.Length == 2)
              .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));
    }
}
