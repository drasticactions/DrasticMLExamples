using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using Gpt4All;
using Sharprompt;
using Console = System.Console;

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
        var localModelOption = new Option<string>("--model", "GPT4All Model");
        var promptOption = new Option<string>("--prompt", "Set prompt");

        var promptCommand = new Command("prompt")
        {
            Handler = CommandHandler.Create(LocalFiles),
        };

        promptCommand.AddOption(localModelOption);
        promptCommand.AddOption(promptOption);

        this.root = new RootCommand
        {
            promptCommand,
        };
    }

    private async Task LocalFiles(string model, string prompt)
    {
        model = this.GetModelPrompt(model);
        if (!File.Exists(model))
        {
            return;
        }

        prompt ??= Prompt.Input<string>("Enter prompt");

        var modelFactory = new Gpt4AllModelFactory();
        var gpt4All = modelFactory.LoadModel(model);
        var result = await gpt4All.GetStreamingPredictionAsync(
            prompt,
            PredictRequestOptions.Defaults);
        Console.WriteLine(Environment.NewLine);
        await foreach (var token in result.GetPredictionStreamingAsync())
        {
            Console.Write(token);
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


    public async Task<int> RunAsync()
    {
        return await this.root.InvokeAsync(this.args);
    }
}