using AIVTuber.Web.Models;
using Microsoft.Extensions.Options;
using SherpaOnnx;

namespace AIVTuber.Web.Services;

public sealed class SupertonicTtsService : IDisposable
{
    private readonly SupertonicOptions _options;
    private readonly string _modelDirectory;
    private readonly SemaphoreSlim _generationLock = new(1, 1);
    private readonly Lazy<OfflineTts> _tts;

    public SupertonicTtsService(
        IOptions<SupertonicOptions> options,
        IWebHostEnvironment environment)
    {
        _options = options.Value;
        _modelDirectory = Path.GetFullPath(
            Path.Combine(environment.ContentRootPath, _options.ModelDirectory));
        _tts = new Lazy<OfflineTts>(CreateTts);
    }

    public async Task<byte[]> GenerateAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        await _generationLock.WaitAsync(cancellationToken);

        try
        {
            return await Task.Run(
                () => GenerateWave(text, cancellationToken),
                cancellationToken);
        }
        finally
        {
            _generationLock.Release();
        }
    }

    private byte[] GenerateWave(
        string text,
        CancellationToken cancellationToken)
    {
        var generationConfig = new OfflineTtsGenerationConfig
        {
            Sid = _options.SpeakerId,
            NumSteps = _options.NumSteps,
            Speed = _options.Speed
        };
        generationConfig.Extra["lang"] = "ko";

        var callback = new OfflineTtsCallbackProgressWithArg(
            (_, _, _, _) => cancellationToken.IsCancellationRequested ? 0 : 1);
        OfflineTtsGeneratedAudio audio = _tts.Value.GenerateWithConfig(
            text,
            generationConfig,
            callback);

        cancellationToken.ThrowIfCancellationRequested();

        return CreatePcm16Wave(audio.Samples, audio.SampleRate);
    }

    private OfflineTts CreateTts()
    {
        string[] requiredFiles =
        [
            "duration_predictor.int8.onnx",
            "text_encoder.int8.onnx",
            "vector_estimator.int8.onnx",
            "vocoder.int8.onnx",
            "tts.json",
            "unicode_indexer.bin",
            "voice.bin"
        ];

        string? missingFile = requiredFiles
            .Select(GetModelPath)
            .FirstOrDefault(path => !File.Exists(path));

        if (missingFile != null)
        {
            throw new FileNotFoundException(
                "Supertonic 모델이 없습니다. Scripts/download-supertonic.ps1을 실행하세요.",
                missingFile);
        }

        var config = new OfflineTtsConfig();
        config.Model.Supertonic.DurationPredictor = GetModelPath(requiredFiles[0]);
        config.Model.Supertonic.TextEncoder = GetModelPath(requiredFiles[1]);
        config.Model.Supertonic.VectorEstimator = GetModelPath(requiredFiles[2]);
        config.Model.Supertonic.Vocoder = GetModelPath(requiredFiles[3]);
        config.Model.Supertonic.TtsJson = GetModelPath(requiredFiles[4]);
        config.Model.Supertonic.UnicodeIndexer = GetModelPath(requiredFiles[5]);
        config.Model.Supertonic.VoiceStyle = GetModelPath(requiredFiles[6]);
        config.Model.NumThreads = Math.Max(1, _options.NumThreads);
        config.Model.Debug = 0;
        config.Model.Provider = "cpu";

        return new OfflineTts(config);
    }

    private string GetModelPath(string fileName) =>
        Path.Combine(_modelDirectory, fileName);

    private static byte[] CreatePcm16Wave(
        float[] samples,
        int sampleRate)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        int dataLength = samples.Length * sizeof(short);

        using var stream = new MemoryStream(44 + dataLength);
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataLength);

        foreach (float sample in samples)
        {
            float clipped = Math.Clamp(sample, -1.0f, 1.0f);
            writer.Write((short)Math.Round(clipped * short.MaxValue));
        }

        writer.Flush();
        return stream.ToArray();
    }

    public void Dispose()
    {
        if (_tts.IsValueCreated)
        {
            _tts.Value.Dispose();
        }

        _generationLock.Dispose();
    }
}
