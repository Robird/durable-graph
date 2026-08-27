namespace Atelia.DurableGraph.Build;

internal static class Program {
    private const int InvalidInputExitCode = 2;

    public static int Main(string[] args) {
        try {
            Command command = Command.Parse(args);
            SnapshotHistoryTool tool = new();

            SnapshotHistoryResult result = command.Name switch {
                "publish" => tool.Publish(command.ManifestPath, command.HistoryDirectory),
                "verify" => tool.Verify(command.ManifestPath, command.HistoryDirectory),
                _ => throw new CommandLineException($"unknown command '{command.Name}'"),
            };

            Console.Out.WriteLine(result.Message);
            return 0;
        } catch (CommandLineException exception) {
            WriteError(exception.Message);
            WriteUsage();
            return InvalidInputExitCode;
        } catch (SnapshotHistoryException exception) {
            WriteError(exception.Message);
            return InvalidInputExitCode;
        } catch (Exception exception) {
            WriteError(exception.Message);
            return 1;
        }
    }

    private static void WriteError(string message) {
        Console.Error.WriteLine($"durable-graph-build: {message}");
    }

    private static void WriteUsage() {
        Console.Error.WriteLine(
            "usage: DurableGraph.Build <publish|verify> --manifest <generated.g.cs> --history <directory>");
    }

    private sealed record Command(
        string Name,
        string ManifestPath,
        string HistoryDirectory) {
        public static Command Parse(string[] args) {
            if (args.Length == 0) {
                throw new CommandLineException("a command is required");
            }

            string name = args[0];

            if (name is not ("publish" or "verify")) {
                throw new CommandLineException($"unknown command '{name}'");
            }

            string? manifestPath = null;
            string? historyDirectory = null;

            for (int index = 1; index < args.Length; index += 2) {
                if (index + 1 >= args.Length) {
                    throw new CommandLineException($"option '{args[index]}' requires a value");
                }

                string option = args[index];
                string value = args[index + 1];

                if (string.IsNullOrWhiteSpace(value)) {
                    throw new CommandLineException($"option '{option}' requires a non-empty value");
                }

                switch (option) {
                    case "--manifest" when manifestPath is null:
                        manifestPath = value;
                        break;
                    case "--history" when historyDirectory is null:
                        historyDirectory = value;
                        break;
                    case "--manifest":
                    case "--history":
                        throw new CommandLineException($"option '{option}' was specified more than once");
                    default:
                        throw new CommandLineException($"unknown option '{option}'");
                }
            }

            if (manifestPath is null) {
                throw new CommandLineException("option '--manifest' is required");
            }

            if (historyDirectory is null) {
                throw new CommandLineException("option '--history' is required");
            }

            return new Command(name, manifestPath, historyDirectory);
        }
    }
}

internal sealed class CommandLineException : Exception {
    public CommandLineException(string message)
        : base(message) {
    }
}
