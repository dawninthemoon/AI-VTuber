using AIVTuber.Web.Models;
using AIVTuber.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------
// Options
// -----------------------------

builder.Services.Configure<OllamaOptions>(
    builder.Configuration.GetSection("Ollama")
);

// -----------------------------
// HTTP Services
// -----------------------------

builder.Services.AddHttpClient<OllamaService>();

builder.Services.AddHttpClient<GPTSoVitsTtsService>();
builder.Services.AddHttpClient<WikipediaSearchService>();

// -----------------------------
// Application Services
// -----------------------------

builder.Services.AddSingleton<ChatClassifier>();

// 대화 히스토리를 유지하므로 Singleton
builder.Services.AddSingleton<ChatService>();

// Unity에 전달할 현재 캐릭터 상태
builder.Services.AddSingleton<CharacterStateService>();

var app = builder.Build();

// -----------------------------
// Static Web UI
// -----------------------------

app.UseDefaultFiles();
app.UseStaticFiles();

// -----------------------------
// Chat
// -----------------------------

app.MapPost(
    "/chat",
    async (
        ChatRequest request,
        ChatService chatService,
        ChatClassifier classifier,
        WikipediaSearchService searchService,
        CharacterStateService stateService,
        GPTSoVitsTtsService ttsService,
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

        GenerationProfile profile = classifier.Classify(request.Message);
        SearchEvidence? evidence = null;

        async Task<AICharacterResponse> GenerateResponseAsync()
        {
            if (profile.NeedsSearch)
            {
                try
                {
                    evidence = await searchService.SearchAsync(request.Message, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "사실 검색 실패");
                    evidence = new SearchEvidence([]);
                }
            }

            return await chatService.SendAsync(request.Message, evidence, cancellationToken);
        }

        Task<AICharacterResponse> responseTask = GenerateResponseAsync();

        if (profile.NeedsSearch)
        {
            await Task.WhenAny(
                responseTask,
                Task.Delay(TimeSpan.FromSeconds(1.2), cancellationToken));

            if (!responseTask.IsCompleted)
            {
                string waitingLine = evidence == null
                    ? "잠시만, 찾아볼게."
                    : "생각 좀 해볼게.";
                byte[]? waitingAudio = null;

                try
                {
                    waitingAudio = await ttsService.GenerateAsync(waitingLine, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "검색 안내 음성 생성 실패");
                }

                if (!responseTask.IsCompleted && waitingAudio is { Length: > 0 })
                {
                    stateService.SetResponse(waitingLine, "neutral", 0.4f, waitingAudio);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
        }

        AICharacterResponse response;

        try
        {
            response = await responseTask;
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

        byte[]? audio = null;

        // ---------------------------------
        // 2. GPT-SoVITS 음성 생성
        // ---------------------------------

        try
        {
            audio =
                await ttsService.GenerateAsync(
                    response.Text,
                    cancellationToken
                );

            logger.LogInformation(
                "TTS 생성 성공: {Bytes} bytes",
                audio.Length
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // TTS가 실패해도 채팅 자체는 정상 동작하게 한다.
            logger.LogWarning(
                ex,
                "GPT-SoVITS TTS 생성 실패"
            );
        }

        // ---------------------------------
        // 3. Unity 상태 갱신
        // ---------------------------------
        //
        stateService.SetResponse(
            response.Text,
            response.Emotion,
            response.Intensity,
            audio
        );

        // ---------------------------------
        // 4. Browser Chat UI 응답
        // ---------------------------------

        return Results.Ok(
            new ChatResponse(
                response.Text,
                evidence?.Hits
            )
        );
    }
);

// -----------------------------
// Chat Reset
// -----------------------------

app.MapPost(
    "/chat/reset",
    (
        ChatService chatService,
        CharacterStateService stateService
    ) =>
    {
        chatService.Reset();
        stateService.Reset();

        return Results.Ok();
    }
);

// -----------------------------
// Unity Character State
// -----------------------------

app.MapGet(
    "/unity/state",
    (
        CharacterStateService stateService
    ) =>
    {
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
