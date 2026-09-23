using AIVTuber.Web.Models;
using AIVTuber.Web.Services;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<IdleOptions>(builder.Configuration.GetSection("Idle"));
builder.Services.AddSingleton<IdleActivityService>();
builder.Services.AddHostedService<IdleBehaviorService>();

// -----------------------------
// Options
// -----------------------------

builder.Services.Configure<SpireOptions>(
    builder.Configuration.GetSection("Spire")
);
builder.Services.Configure<OpenAiOptions>(
    builder.Configuration.GetSection("OpenAI")
);
builder.Services.Configure<YouTubeOptions>(
    builder.Configuration.GetSection("YouTube")
);

// -----------------------------
// HTTP Services
// -----------------------------

builder.Services.AddHttpClient<OpenAiChatService>();
builder.Services.AddHttpClient<OpenAiSpireDecisionService>();

builder.Services.AddHttpClient<GPTSoVitsTtsService>();
builder.Services.AddHttpClient<WikipediaSearchService>();
builder.Services.AddHttpClient<YouTubeLiveChatService>();

// -----------------------------
// Application Services
// -----------------------------

builder.Services.AddSingleton<ChatClassifier>();

// 대화 히스토리를 유지하므로 Singleton
builder.Services.AddSingleton<ChatService>();

// Unity에 전달할 현재 캐릭터 상태
builder.Services.AddSingleton<CharacterStateService>();
builder.Services.AddSingleton<PlaybackStatusService>();
builder.Services.AddSingleton<SpeechTurnCoordinator>();
builder.Services.AddSingleton<SpireContextBuilder>();
builder.Services.AddSingleton<OpenAiSpireDecisionService>();
builder.Services.AddSingleton<SpireSpeechService>();
builder.Services.AddSingleton<SpireTurnService>();
builder.Services.AddSingleton<ChatResponseService>();
builder.Services.AddSingleton<YouTubeChatStatus>();
builder.Services.AddHostedService(
    services => services.GetRequiredService<YouTubeLiveChatService>());

var app = builder.Build();

// -----------------------------
// Static Web UI
// -----------------------------

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/youtube/status", (YouTubeChatStatus status) => Results.Ok(status.Snapshot()));

// -----------------------------
// Chat
// -----------------------------

app.MapPost(
    "/chat",
    async (
        ChatRequest request,
        ChatResponseService responseService,
        ILogger<Program> logger,
        CancellationToken cancellationToken
    ) =>
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.BadRequest(
                new
                {
                    error = "메시지가 비어 있습니다."
                }
            );
        }

        try
        {
            ChatResponse response = await responseService.RespondAsync(
                request.Message,
                cancellationToken);
            return Results.Ok(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "AI 응답 생성 실패"
            );

            return Results.Problem(
                "AI 응답 생성에 실패했습니다."
            );
        }

    }
);

// -----------------------------
// Chat Reset
// -----------------------------

app.MapPost(
    "/chat/reset",
    (
        ChatService chatService,
        CharacterStateService stateService,
        PlaybackStatusService playbackStatusService
    ) =>
    {
        chatService.Reset();
        stateService.Reset();
        playbackStatusService.Set(false);

        return Results.Ok();
    }
);

// -----------------------------
// Unity Character State
// -----------------------------

app.MapGet(
    "/unity/state",
    (
        CharacterStateService stateService,
        PlaybackStatusService playback
    ) =>
    {
        playback.ViewerHeartbeat();
        CharacterState state =
            stateService.Get();

        return Results.Ok(
            state
        );
    }
);

// -----------------------------
// Unity TTS Audio
// -----------------------------

app.MapGet(
    "/unity/audio",
    (
        CharacterStateService stateService,
        long? version
    ) =>
    {
        if (!version.HasValue)
        {
            return Results.BadRequest();
        }

        byte[]? audio = stateService.GetAudio(version.Value);

        // 아직 생성된 음성이 없는 경우
        if (audio == null || audio.Length == 0)
        {
            return Results.NotFound();
        }

        return Results.File(
            audio,
            "audio/wav"
        );
    }
);

// -----------------------------
// Unity Voice Playback Status
// -----------------------------

app.MapPost(
    "/voice/playback",
    (
        PlaybackStatusRequest request,
        PlaybackStatusService playbackStatusService
    ) =>
    {
        playbackStatusService.Set(request.Speaking, request.CompletedVersion);
        return Results.Ok(playbackStatusService.Get());
    }
);

// -----------------------------
// Slay the Spire / CommunicationMod
// -----------------------------

// This endpoint is intentionally synchronous: CommunicationMod sends one stable
// state then waits for exactly one command on its child process's stdout.
app.MapPost(
    "/spire/turn",
    async (
        JsonElement state,
        SpireTurnService spireTurnService,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        SpireTurnResponse response = await spireTurnService.DecideAsync(state, cancellationToken);

        logger.LogInformation(
            "Spire turn received. Command={Command}, AutoPlay={AutoPlay}",
            response.Command,
            response.AutoPlay);

        return Results.Ok(response);
    }
);

app.MapGet(
    "/spire/state",
    (SpireTurnService spireTurnService) =>
        Results.Ok(spireTurnService.GetLastState())
);

app.MapGet(
    "/spire/context",
    (SpireTurnService spireTurnService) =>
        Results.Ok(spireTurnService.GetLastContext())
);

// Local-only diagnostic endpoint for adding support for new CommunicationMod screens.
app.MapGet(
    "/spire/raw-state",
    (SpireTurnService spireTurnService) =>
        Results.Ok(spireTurnService.GetLastRawState())
);

app.MapGet(
    "/voice/playback",
    (PlaybackStatusService playbackStatusService) =>
        Results.Ok(playbackStatusService.Get())
);

// -----------------------------
// Health Check
// -----------------------------

app.MapGet(
    "/health",
    () =>
    {
        return Results.Ok(
            new
            {
                status = "ok"
            }
        );
    }
);

app.Run();
