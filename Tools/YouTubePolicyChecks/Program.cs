using AIVTuber.Web.Services;

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine($"PASS: {name}");
}

var policy = new YouTubeReconnectPolicy();
int[] delays = [5, 10, 20, 40, 80, 160, 300, 300];
foreach (int seconds in delays)
{
    Check(policy.AfterClose(TimeSpan.FromSeconds(10), true).TotalSeconds == seconds,
        $"Short clean streams back off to {seconds}s even after receiving data");
}
Check(policy.AfterClose(TimeSpan.FromMinutes(3), true).TotalSeconds == 1, "Healthy EOF resumes promptly");
Check(policy.AfterClose(TimeSpan.FromSeconds(10), false).TotalSeconds == 5, "Healthy session resets failure backoff");
for (int i = 0; i < 12; i++)
{
    Check(policy.TryStart(TimeSpan.FromMinutes(i)), $"Attempt {i + 1} allowed");
}
Check(!policy.TryStart(TimeSpan.FromMinutes(12)), "Attempt 13 blocked");
Check(!policy.TryStart(TimeSpan.FromMinutes(59)), "Budget remains blocked within rolling hour");
Check(policy.TryStart(TimeSpan.FromHours(1)), "Oldest attempt expires after one hour");
