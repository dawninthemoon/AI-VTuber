using System.Net.Http.Json;
using System.Text.Json;

var httpClient = new HttpClient();

while (true)
{
    Console.Write("You: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
        continue;

    var request = new
    {
        model = "qwen3:8b",

        prompt = $"""
        너는 실시간 방송 중인 AI 버튜버다.
        답변은 반드시 자연스러운 한국어로 1~2문장만 한다.
        짧고 빠르게 대답한다.
        모르는 사실은 지어내지 않는다.

        시청자: {input}
        """,

        think = false,
        stream = true,

        options = new
        {
            temperature = 0.6,
            num_predict = 80
        }
    };

    using var response = await httpClient.PostAsJsonAsync(
        "http://localhost:11434/api/generate",
        request
    );

    response.EnsureSuccessStatusCode();

    Console.Write("AI: ");

    string buffer = "";

    using var stream = await response.Content.ReadAsStreamAsync();
    using var reader = new StreamReader(stream);

    while (true)
    {
        Console.Write("You: ");
        var input2 = Console.ReadLine();

        Console.WriteLine();
        Console.WriteLine($"입력됨: {input2}");
    }

    while (true)
    {
        var line = await reader.ReadLineAsync();

        if (line is null)
            break;

        if (string.IsNullOrWhiteSpace(line))
            continue;

        var chunk = JsonSerializer.Deserialize<OllamaChunk>(line);

        if (!string.IsNullOrEmpty(chunk?.response))
        {
            buffer += chunk.response;

            if (buffer.Contains('.') ||
                buffer.Contains('!') ||
                buffer.Contains('?') ||
                buffer.Contains('\n'))
            {
                Console.Write(buffer);
                buffer = "";
            }
        }

        if (chunk?.done == true)
            break;
    }

    if (!string.IsNullOrEmpty(buffer))
    {
        Console.Write(buffer);
    }

    Console.WriteLine();
    Console.WriteLine();
}

public class OllamaChunk
{
    public string response { get; set; } = "";
    public bool done { get; set; }
}