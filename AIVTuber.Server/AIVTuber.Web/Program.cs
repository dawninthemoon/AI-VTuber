using AIVTuber.Web.Models;
using AIVTuber.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<OllamaOptions>(
    builder.Configuration.GetSection("Ollama"));

builder.Services.AddHttpClient<OllamaService>();
builder.Services.AddSingleton<ChatClassifier>();
builder.Services.AddSingleton<ChatService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/chat", async (
    ChatRequest request,
    ChatService chatService,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new
        {
            error = "메시지가 비어 있습니다."
        });
    }

    try
    {
        var response = await chatService.SendAsync(
            request.Message,
            cancellationToken);

        return Results.Ok(new ChatResponse(response));
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem(
            title: "Ollama 연결 오류",
            detail: ex.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/chat/reset", (ChatService chatService) =>
{
    chatService.Reset();
    return Results.NoContent();
});

app.Run();
