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

// -----------------------------
// Application Services
// -----------------------------

builder.Services.AddSingleton<ChatClassifier>();

// 대화 히스토리를 유지하므로 Singleton
builder.Services.AddSingleton<ChatService>();

// Unity에 전달할 현재 캐릭터 상태
builder.Services.AddSingleton<CharacterStateService>();

// Unity에 전달할 최신 TTS 오디오
builder.Services.AddSingleton<CharacterAudioService>();

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
        CharacterStateService stateService,
        CharacterAudioService audioService,
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

        // ---------------------------------
        // 1. Ollama에서 AI 응답 생성
        // ---------------------------------

        AICharacterResponse response;

        try
        {
            response =
                await chatService.SendAsync(
                    request.Message,
                    cancellationToken
                );
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
        // 일부러 TTS 생성 후 state version을 올린다.
        //
        // 그래야 Unity가 새로운 version을 감지했을 때
        // 오디오도 이미 준비된 상태가 된다.
        //

        stateService.SetResponse(
            response.Text,
            response.Emotion,
            response.Intensity
        );

        CharacterState state =
            stateService.Get();

        // ---------------------------------
        // 4. 오디오 저장
        // ---------------------------------

        if (audio != null &&
            audio.Length > 0)
        {
            audioService.Set(
                audio,
                state.Version
            );
        }

        // ---------------------------------
        // 5. Browser Chat UI 응답
        // ---------------------------------

        return Results.Ok(
            new ChatResponse(
                response.Text
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
        ChatService chatService
    ) =>
    {
        chatService.Reset();

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
        CharacterAudioService audioService,
        long? version
    ) =>
    {
        var result =
            audioService.Get();

        // 아직 생성된 음성이 없는 경우
        if (result.Audio == null ||
            result.Audio.Length == 0)
        {
            return Results.NotFound();
        }

        // Unity가 요청한 캐릭터 상태 version과
        // 오디오 version이 다르면 재생하면 안 됨
        if (version.HasValue &&
            result.Version != version.Value)
        {
            return Results.NotFound();
        }

        return Results.File(
            result.Audio,
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