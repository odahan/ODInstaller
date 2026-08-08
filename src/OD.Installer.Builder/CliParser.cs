namespace OD.Installer.Builder;

/// <summary>
/// Parsed command-line arguments for the builder.
/// </summary>
public sealed class CliArguments
{
    /// <summary>
    /// The command to execute. Only <c>build</c> is supported.
    /// </summary>
    public string Command { get; init; } = "";

    /// <summary>
    /// Path of the <c>installer.json</c> file to build.
    /// </summary>
    public string ManifestPath { get; init; } = "";

    /// <summary>
    /// Optional output directory overriding <c>output.directory</c>.
    /// </summary>
    public string? Output { get; init; }

    /// <summary>
    /// Indicates whether an existing output file may be overwritten.
    /// </summary>
    public bool Force { get; init; }

    /// <summary>
    /// Indicates whether detailed information must be printed.
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// Indicates that help must be displayed.
    /// </summary>
    public bool ShowHelp { get; init; }

    /// <summary>
    /// Indicates that the builder version must be displayed.
    /// </summary>
    public bool ShowVersion { get; init; }
}

/// <summary>
/// Parses the builder's command-line arguments.
/// </summary>
public static class CliParser
{
    private const string BuildCommand = "build";

    /// <summary>
    /// Parses command-line arguments.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>The parsed arguments.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the arguments are not a valid command line.
    /// </exception>
    public static CliArguments Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException(
                "Missing command. Expected 'build <installer.json>' or '--help'.");
        }

        if (IsHelp(args[0]))
        {
            return new CliArguments { ShowHelp = true };
        }

        if (string.Equals(args[0], "--version", StringComparison.OrdinalIgnoreCase))
        {
            return new CliArguments { ShowVersion = true };
        }

        if (!string.Equals(args[0], BuildCommand, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unknown command '{args[0]}'. Expected '{BuildCommand}'.");
        }

        if (args.Length < 2)
        {
            throw new ArgumentException($"Missing manifest path for '{BuildCommand}'.");
        }

        var manifestPath = args[1];
        string? output = null;
        var force = false;
        var verbose = false;

        for (var i = 2; i < args.Length; i++)
        {
            var argument = args[i];

            if (IsHelp(argument))
            {
                return new CliArguments { ShowHelp = true };
            }

            if (string.Equals(argument, "--version", StringComparison.OrdinalIgnoreCase))
            {
                return new CliArguments { ShowVersion = true };
            }

            if (string.Equals(argument, "--force", StringComparison.OrdinalIgnoreCase))
            {
                force = true;
                continue;
            }

            if (string.Equals(argument, "--verbose", StringComparison.OrdinalIgnoreCase))
            {
                verbose = true;
                continue;
            }

            if (string.Equals(argument, "--output", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException("Missing value for '--output'.");
                }

                output = args[++i];
                continue;
            }

            if (argument.StartsWith('-'))
            {
                throw new ArgumentException($"Unknown option '{argument}'.");
            }

            throw new ArgumentException($"Unexpected argument '{argument}'.");
        }

        return new CliArguments
        {
            Command = BuildCommand,
            ManifestPath = manifestPath,
            Output = output,
            Force = force,
            Verbose = verbose
        };
    }

    private static bool IsHelp(string argument) =>
        argument is "--help" or "-h" or "-?";
}
