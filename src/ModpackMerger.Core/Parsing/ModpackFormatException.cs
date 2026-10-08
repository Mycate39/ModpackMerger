namespace ModpackMerger.Core.Parsing;

public sealed class ModpackFormatException(string message, Exception? inner = null) : Exception(message, inner);
