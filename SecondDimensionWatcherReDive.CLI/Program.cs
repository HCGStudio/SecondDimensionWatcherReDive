using System.CommandLine;
using SecondDimensionWatcherReDive.ConfigMigration;
using SecondDimensionWatcherReDive.Framework.ConfigurationMigration;

namespace SecondDimensionWatcherReDive.CLI;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var applet = CliInvocation.GetAppletName();
            var root = new RootCommand("SecondDimensionWatcher Re:Dive command-line tools.")
            {
                HelpName = applet == "migrate" ? "sdw-migrate" : "sdw-cli"
            };

            if (applet == "migrate")
            {
                root.Description = "Upgrade configuration through each version, prompting only for breaking decisions.";
                ConfigureMigrateCommand(root);
            }
            else
            {
                var migrate = new Command("migrate",
                    "Upgrade configuration through each version, prompting only for breaking decisions.");
                ConfigureMigrateCommand(migrate);
                root.Subcommands.Add(migrate);
            }

            return await root.Parse(args).InvokeAsync(new InvocationConfiguration
            {
                EnableDefaultExceptionHandler = false
            });
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Configuration migration canceled.");
            return 130;
        }
        catch (ConfigMigrationException exception)
        {
            Console.Error.WriteLine($"Configuration migration failed: {exception.Message}");
            return 1;
        }
        catch (Exception exception)
        {
            // Parser and I/O exceptions may include configuration content; never print raw details.
            Console.Error.WriteLine($"Configuration migration failed ({exception.GetType().Name}). " +
                                    "Check the configuration path, working directory, and file permissions.");
            return 1;
        }
    }

    private static void ConfigureMigrateCommand(Command command)
    {
        var config = new Option<string>("--config")
        {
            Description = "Configuration file (default: Config environment variable, installed YAML, or ./appsettings.json).",
            HelpName = "PATH",
            DefaultValueFactory = _ => GetDefaultConfigPath()
        };
        var nonInteractive = new Option<bool>("--non-interactive")
        {
            Description = "Allow silent upgrades only; fail if a breaking decision requires user input."
        };
        var workingDirectory = new Option<string>("--working-directory")
        {
            Description = "Original application working directory for resolving legacy relative paths.",
            HelpName = "DIR",
            DefaultValueFactory = _ => Environment.CurrentDirectory
        };

        command.Options.Add(config);
        command.Options.Add(nonInteractive);
        command.Options.Add(workingDirectory);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var silent = parseResult.GetValue(nonInteractive) || Console.IsInputRedirected;
            var result = await new ConfigMigrationRunner().MigrateFileAsync(
                parseResult.GetValue(config)!,
                parseResult.GetValue(workingDirectory)!,
                silent ? null : ChooseAsync,
                cancellationToken);

            if (result.AppliedMigrations.Count == 0)
                Console.WriteLine($"Configuration is already at version {result.ToVersion}.");
            else
            {
                Console.WriteLine($"Configuration upgraded from {result.FromVersion} to {result.ToVersion}.");
                if (result.BackupPath is not null)
                    Console.WriteLine($"Backup: {result.BackupPath}");
            }

            return 0;
        });
    }

    private static string GetDefaultConfigPath()
    {
        var configuredPath = Environment.GetEnvironmentVariable("Config");
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return configuredPath;

        const string installedPath = "/etc/sdw-redive/appsettings.yml";
        return File.Exists(installedPath) ? installedPath : "appsettings.json";
    }

    private static async Task<string> ChooseAsync(
        ConfigMigrationChoice choice,
        CancellationToken cancellationToken)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(choice.Description);
        for (var index = 0; index < choice.Options.Count; index++)
        {
            var option = choice.Options[index];
            Console.Error.WriteLine($"  {index + 1}. {option.Value}: {option.Description}");
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.Error.Write("Choose an option number or value (Ctrl+C to cancel): ");
            // Console.In may implement ReadLineAsync synchronously; keep cancellation responsive.
            var answer = await Task.Run(Console.ReadLine, cancellationToken).WaitAsync(cancellationToken);
            if (answer is null)
                throw new OperationCanceledException("Input ended before a decision was made.", cancellationToken);

            answer = answer.Trim();
            if (int.TryParse(answer, out var selected) && selected >= 1 && selected <= choice.Options.Count)
                return choice.Options[selected - 1].Value;

            var selectedOption = choice.Options.FirstOrDefault(option =>
                string.Equals(option.Value, answer, StringComparison.OrdinalIgnoreCase));
            if (selectedOption is not null)
                return selectedOption.Value;

            Console.Error.WriteLine("Enter one of the listed options; no default is selected for a breaking change.");
        }
    }
}
