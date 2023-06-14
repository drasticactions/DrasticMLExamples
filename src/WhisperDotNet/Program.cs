// <copyright file="Program.cs" company="Drastic Actions">
// Copyright (c) Drastic Actions. All rights reserved.
// </copyright>

using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Drastic.Services;
using Sharprompt;
using WhisperDotNet.Models;
using WhisperDotNet.Services;
using WhisperDotNet.Tools;

internal class Program
{
    internal static MainProgram? program;

    private static async Task<int> Main(string[] args)
    {
        program = new MainProgram(args);
        return await program.RunAsync();
    }
}

internal class MainProgram
{
    private IAppDispatcher dispatcher;
    private RootCommand root;
    private readonly string[] args;
    private WhisperModelService modelService;
    private ITranscodeService transcodeService;
    private CancellationTokenSource cts;
    
    public MainProgram(string[] args)
    {
        this.cts = new CancellationTokenSource();
        this.dispatcher = new AppDispatcher();
        this.modelService = new WhisperModelService(this.dispatcher);
        this.transcodeService = new FFMpegTranscodeService();
        
        this.args = args;
        var localLanguageOption = new Option<string>("--language", "Whisper Language Code");
        var localModelOption = new Option<string>("--model", "Whisper Model");
        var transcribeCommand = new Command("transcribe")
        {
            Handler = CommandHandler.Create(Transcribe),
        };

        transcribeCommand.AddOption(localModelOption);
        transcribeCommand.Add(localLanguageOption);
        transcribeCommand.Add(new Option<List<string>>("--file", "Local file"));

        this.root = new RootCommand
        {
            transcribeCommand,
        };
    }

    public async Task<int> RunAsync()
    {
        return await this.root.InvokeAsync(this.args);
    }

    private async Task Transcribe(string model, string languageCode, List<string> file)
    {
        model = await this.GetModelPrompt(model);
        var language = this.GetLanguagePrompt(languageCode);
        var files = this.GetFilesPrompt(file).Where(n => File.Exists(n));
        foreach (var f in files)
        {
            await this.RunModelAsync(model, language, f);
        }
    }

    private async Task<string> GetModelPrompt(string? modelPath = "")
    {
        if (File.Exists(modelPath))
        {
            return modelPath;
        }

        var model = Prompt.Select("Select a model", this.modelService.AllModels, textSelector: (item) => item.Name);
        if (model.Exists)
        {
            return model.FileLocation;
        }

        Console.WriteLine("Downloading Model...");
        var whisperDownloader =
            new WhisperDownload(model, this.modelService, this.dispatcher);
        whisperDownloader.DownloadService.DownloadProgressChanged += (s, e) =>
        {
            //progressBar.Update((int)e.ProgressPercentage);
        };
        await whisperDownloader.DownloadCommand.ExecuteAsync();

        return model.FileLocation;
    }
    
    private List<string> GetFilesPrompt(List<string> existingFiles)
    {
        existingFiles = existingFiles.Where(n => File.Exists(n)).ToList();
        if (existingFiles.Any())
        {
            return existingFiles;
        }

        var file = Prompt.Input<string>("Enter File Path",
            defaultValue: Path.Combine(WhisperStatic.DefaultPath, "generated.wav"));
        existingFiles.Add(file);

        return existingFiles;
    }

    private WhisperLanguage GetLanguagePrompt(string languageCode = "")
    {
        var languages = WhisperLanguage.GenerateWhisperLangauages();
        var language = languages.FirstOrDefault(n => n.LanguageCode == languageCode)
                       ?? Prompt.Select("Select a Language", languages, textSelector: (item) => item.Language);
        return language;
    }

    private string ConvertToValidFilename(string input)
    {
        // Remove invalid characters
        string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
        string invalidReStr = string.Format(@"[{0}]+", invalidChars);
        string sanitizedInput = Regex.Replace(input, invalidReStr, "_");

        // Trim leading/trailing whitespaces and dots
        sanitizedInput = sanitizedInput.Trim().Trim('.');

        // Ensure filename is not empty
        if (string.IsNullOrEmpty(sanitizedInput))
        {
            return "_";
        }

        // Ensure filename is not too long
        int maxFilenameLength = 255;
        if (sanitizedInput.Length > maxFilenameLength)
        {
            sanitizedInput = sanitizedInput.Substring(0, maxFilenameLength);
        }

        return sanitizedInput;
    }
    
    private async Task RunModelAsync(string modelPath, WhisperLanguage language, string path, string? filename = null)
    {
        if (!File.Exists(modelPath))
        {
            throw new ArgumentNullException("Model does not exist");
        }

        var srtPath = Path.Combine(WhisperStatic.DefaultPath, "srt");
        Directory.CreateDirectory(srtPath);
        using var whisperService = new DefaultWhisperService();
        whisperService.InitModel(modelPath, language);

        var audio = await this.transcodeService.ProcessFile(path);
        if (string.IsNullOrEmpty(audio) || !File.Exists(audio))
        {
            throw new ArgumentNullException($"Could not generate audio file: {path}");
        }

        var srtFile = Path.Combine(srtPath,
            ConvertToValidFilename(filename ?? Path.GetFileNameWithoutExtension(path)) +
            $"_{Path.GetFileNameWithoutExtension(modelPath)}_generic.srt");

        using StreamWriter writer = new StreamWriter(srtFile);
        using StreamWriter stopwatchWriter =
            new StreamWriter(Path.Combine(WhisperStatic.DefaultPath, "stopwatch.txt"), true);

        Stopwatch stopwatch = new Stopwatch();
        var subtitles = 0;

        void WhisperServiceOnOnNewWhisperSegment(object? sender, OnNewSegmentEventArgs segment)
        {
            // Start when we get the first subtitle.
            if (!stopwatch.IsRunning)
            {
                stopwatch.Start();
            }

            subtitles = subtitles + 1;
            var e = segment.Segment;
            Console.WriteLine($"CSSS {e.Start} ==> {e.End} : {e.Text}");
            var item = new SrtSubtitleLine()
            { Start = e.Start, End = e.End, Text = e.Text.Trim(), LineNumber = subtitles };
            writer.WriteLine(item.ToString());
            writer.Flush();
        }

        whisperService.OnNewWhisperSegment += WhisperServiceOnOnNewWhisperSegment;
        await whisperService.ProcessAsync(audio, this.cts.Token);
        whisperService.OnNewWhisperSegment -= WhisperServiceOnOnNewWhisperSegment;
        stopwatch.Stop();
        Console.WriteLine(stopwatch.Elapsed);
        stopwatchWriter.WriteLine($"{srtFile}: {stopwatch.Elapsed}");
        stopwatchWriter.Flush();
    }
}

internal class AppDispatcher : IAppDispatcher
{
    public bool Dispatch(Action action)
    {
        action.Invoke();
        return true;
    }
}