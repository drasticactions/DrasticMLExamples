// <copyright file="Program.cs" company="Drastic Actions">
// Copyright (c) Drastic Actions. All rights reserved.
// </copyright>

using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using Sharprompt;

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
    private RootCommand root;
    private readonly string[] args;

    public MainProgram(string[] args)
    {
        this.args = args;
        var localModelOption = new Option<string>("--model", "Whisper Model");

        var promptCommand = new Command("transcribe")
        {
            Handler = CommandHandler.Create(Transcribe),
        };

        promptCommand.AddOption(localModelOption);

        this.root = new RootCommand
        {
            promptCommand,
        };
    }

    public async Task<int> RunAsync()
    {
        return await this.root.InvokeAsync(this.args);
    }

    private async Task Transcribe(string model)
    {
        model = this.GetModelPrompt(model);
        if (!File.Exists(model))
        {
            return;
        }
    }

    private string GetModelPrompt(string? modelPath = "")
    {
        if (File.Exists(modelPath))
        {
            return modelPath;
        }

        return Prompt.Input<string>("Enter model path.");
    }
}