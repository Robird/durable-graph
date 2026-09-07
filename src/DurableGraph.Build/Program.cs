namespace Atelia.DurableGraph.Build;

internal static class Program {
    private const int InvalidInputExitCode = 2;

    public static int Main(string[] args) {
        try {
            Command command = Command.Parse(args);
            SchemaHistoryTool tool = new();

            SchemaHistoryResult result = command.Name switch {
                "publish" => tool.Publish(command.ManifestPath, command.SchemaHistoryDirectory),
                "verify" => tool.Verify(command.ManifestPath, command.SchemaHistoryDirectory),
                _ => throw new CommandLineException($"unknown command '{command.Name}'"),
            };

            Console.Out.WriteLine(result.Message);
            return 0;
        } catch (CommandLineException exception) {
            WriteError(exception.Message);
            WriteUsage();
            return InvalidInputExitCode;
        } catch (SchemaHistoryException exception) {
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
            "usage: DurableGraph.Build <publish|verify> --manifest <generated.g.cs> --schema-history <directory>");
    }

    private sealed record Command(
        string Name,
        string ManifestPath,
        string SchemaHistoryDirectory) {
        public static Command Parse(string[] args) {
            if (args.Length == 0) {
                throw new CommandLineException("a command is required");
            }

            string name = args[0];

            if (name is not ("publish" or "verify")) {
                throw new CommandLineException($"unknown command '{name}'");
            }

            string? manifestPath = null;
            string? schemaHistoryDirectory = null;

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
                    case "--schema-history" when schemaHistoryDirectory is null:
                        schemaHistoryDirectory = value;
                        break;
                    case "--manifest":
                    case "--schema-history":
                        throw new CommandLineException($"option '{option}' was specified more than once");
                    default:
                        throw new CommandLineException($"unknown option '{option}'");
                }
            }

            if (manifestPath is null) {
                throw new CommandLineException("option '--manifest' is required");
            }

            if (schemaHistoryDirectory is null) {
                throw new CommandLineException("option '--schema-history' is required");
            }

            return new Command(name, manifestPath, schemaHistoryDirectory);
        }
    }
}

internal sealed class CommandLineException : Exception {
    public CommandLineException(string message)
        : base(message) {
    }
}
