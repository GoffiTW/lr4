using System.Collections.Generic;

namespace LumaChat.Services;

public interface IHistoryService
{
    IEnumerable<HistoryEntry> Load();
    void Append(HistoryEntry entry);
    void Clear();
}

public sealed class HistoryEntry
{
    public string Kind { get; init; } = "TEXT";
    public string Author { get; init; } = "";
    public string Message { get; init; } = "";
    public bool Own { get; init; }
    public string FileName { get; init; } = "";
    public string FilePath { get; init; } = "";
    public bool IsImage { get; init; }
}
