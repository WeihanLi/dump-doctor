using DumpDoctor.Core;
using DumpDoctor.Hosting;
using DumpDoctor.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var exitCode = await RunAsync(args, cancellation.Token);
Environment.Exit((int)exitCode);

static async Task<ExitCode> RunAsync(string[] args, CancellationToken cancellationToken)
{
    if (args.Length == 0 || IsHelp(args[0]))
    {
        WriteHelp();
        return ExitCode.Success;
    }

    var analysisService = new AnalysisService();

    var renderers = new Dictionary<ReportFormat, IReportRenderer>
    {
        [ReportFormat.Json] = new JsonReportRenderer(),
        [ReportFormat.Markdown] = new MarkdownReportRenderer()
    };

    try
    {
        var command = args[0].ToLowerInvariant();
        return command switch
        {
            "analyze" => await HandleAnalyzeOrInspectAsync("analyze", includeFindings: true, args[1..], analysisService, renderers, cancellationToken),
            "inspect" => await HandleAnalyzeOrInspectAsync("inspect", includeFindings: false, args[1..], analysisService, renderers, cancellationToken),
            "mcp" => await RunMcpAsync(args[1..], cancellationToken),
            "rules" => HandleRules(args[1..], analysisService),
            _ => ExitWithError($"Unknown command '{args[0]}'.", ExitCode.InvalidArguments)
        };
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Analysis was cancelled.");
        return ExitCode.Cancelled;
    }
    catch (AnalysisException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return ex.ExitCode;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Unexpected error: {ex.Message}");
        return ExitCode.UnexpectedInternalError;
    }
}

static async Task<ExitCode> RunMcpAsync(string[] args, CancellationToken cancellationToken)
{
    if (args.Length > 0)
    {
        return ExitWithError("The mcp command does not accept arguments.", ExitCode.InvalidArguments);
    }

    var builder = Host.CreateApplicationBuilder();
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    builder.Services.AddSingleton<AnalysisService>();
    builder.Services.AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    using var host = builder.Build();
    await host.RunAsync(cancellationToken);
    return ExitCode.Success;
}

static async Task<ExitCode> HandleAnalyzeOrInspectAsync(
    string command,
    bool includeFindings,
    string[] args,
    AnalysisService analysisService,
    IReadOnlyDictionary<ReportFormat, IReportRenderer> renderers,
    CancellationToken cancellationToken)
{
    if (args.Length == 0)
    {
        return ExitWithError($"{command} requires a dump path.", ExitCode.InvalidArguments);
    }

    var options = AnalysisOptions.CreateDefault(args[0]);
    var index = 1;

    while (index < args.Length)
    {
        var option = args[index];
        switch (option)
        {
            case "--focus":
                options = options with { Focus = ParseEnumOption<AnalysisFocus>(args, ref index, "--focus") };
                break;
            case "--format":
                options = options with { Format = ParseEnumOption<ReportFormat>(args, ref index, "--format") };
                break;
            case "--output":
                options = options with { OutputPath = ParseStringOption(args, ref index, "--output") };
                break;
            case "--fail-on":
                options = options with { FailOn = ParseEnumOption<Severity>(args, ref index, "--fail-on") };
                break;
            case "--captured-at":
                options = options with { CapturedAtOverrideUtc = ParseDateTimeOption(args, ref index, "--captured-at") };
                break;
            case "--redact":
                options = options with { Redaction = ParseEnumOption<RedactionLevel>(args, ref index, "--redact") };
                break;
            case "--allow-symbol-server":
                options = options with { AllowSymbolServer = true, Offline = false };
                index++;
                break;
            case "--offline":
                options = options with { Offline = true, AllowSymbolServer = false };
                index++;
                break;
            case "--verbose":
                options = options with { Verbose = true };
                index++;
                break;
            case "--help":
            case "-h":
                WriteHelp();
                return ExitCode.Success;
            default:
                return ExitWithError($"Unknown option '{option}'.", ExitCode.InvalidArguments);
        }
    }

    var report = await analysisService.AnalyzeAsync(command, options, includeFindings, cancellationToken);
    var output = renderers[options.Format].Render(report);

    if (string.IsNullOrWhiteSpace(options.OutputPath))
    {
        Console.Out.WriteLine(output);
    }
    else
    {
        var fullPath = Path.GetFullPath(options.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, output, cancellationToken);
        Console.Error.WriteLine($"Wrote {options.Format} report to {fullPath}");
    }

    return report.DetermineExitCode();
}

static ExitCode HandleRules(string[] args, AnalysisService analysisService)
{
    if (args.Length == 0 || !string.Equals(args[0], "list", StringComparison.OrdinalIgnoreCase))
    {
        return ExitWithError("Supported rules command: 'rules list'.", ExitCode.InvalidArguments);
    }

    Console.Out.WriteLine("| Rule ID | Focus | Category | Required evidence | Thresholds |");
    Console.Out.WriteLine("|---|---|---|---|---|");
    foreach (var rule in analysisService.Rules.OrderBy(rule => rule.Id, StringComparer.Ordinal))
    {
        var evidence = string.Join(", ", rule.RequiredEvidence.OrderBy(kind => kind));
        var thresholds = string.Join(", ", rule.Thresholds.Select(entry => $"{entry.Key}={entry.Value}"));
        Console.Out.WriteLine($"| {rule.Id} | {rule.Focus} | {rule.Category} | {evidence} | {thresholds} |");
    }

    return ExitCode.Success;
}

static TEnum ParseEnumOption<TEnum>(string[] args, ref int index, string optionName)
    where TEnum : struct, Enum
{
    var value = ParseStringOption(args, ref index, optionName);
    if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var result))
    {
        throw new AnalysisException($"Invalid value '{value}' for {optionName}.", ExitCode.InvalidArguments);
    }

    return result;
}

static DateTimeOffset ParseDateTimeOption(string[] args, ref int index, string optionName)
{
    var value = ParseStringOption(args, ref index, optionName);
    if (!DateTimeOffset.TryParse(value, out var parsed))
    {
        throw new AnalysisException($"Invalid timestamp '{value}' for {optionName}.", ExitCode.InvalidArguments);
    }

    return parsed;
}

static string ParseStringOption(string[] args, ref int index, string optionName)
{
    if (index + 1 >= args.Length)
    {
        throw new AnalysisException($"Missing value for {optionName}.", ExitCode.InvalidArguments);
    }

    index += 2;
    return args[index - 1];
}

static bool IsHelp(string value) =>
    string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(value, "help", StringComparison.OrdinalIgnoreCase);

static ExitCode ExitWithError(string message, ExitCode exitCode)
{
    Console.Error.WriteLine(message);
    return exitCode;
}

static void WriteHelp()
{
    Console.Out.WriteLine(
        """
        DumpDoctor - evidence-driven .NET dump triage

        Commands:
          dumpdoctor analyze <dumpPath> [options]
          dumpdoctor inspect <dumpPath> [options]
          dumpdoctor mcp
          dumpdoctor rules list

        Options:
          --focus <all|memory|threads|cpu>
          --format <json|markdown>
          --output <path>
          --fail-on <informational|low|medium|high|critical>
          --captured-at <timestamp>
          --offline
          --allow-symbol-server
          --redact <strict|standard|none>
          --verbose
        """);
}
