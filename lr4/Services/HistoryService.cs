using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LumaChat.Services;

public sealed class HistoryService : IHistoryService
{
    private const int MaxHistoryEntries = 200;

    private readonly string filePath;

    public HistoryService(string? overridePath = null)
    {
        filePath = overridePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LumaChat", "history.log");
    }

    public IEnumerable<HistoryEntry> Load()
    {
        if (!File.Exists(filePath)) yield break;
        foreach (string line in File.ReadAllLines(filePath, Encoding.UTF8))
        {
            var parts = line.Split('|', 7);
            if (parts.Length < 7) continue;
            yield return new HistoryEntry
            {
                Kind = parts[0],
                Author = ChatUtils.DecodePayload(parts[1]),
                Message = ChatUtils.DecodePayload(parts[2]),
                Own = parts[3] == "1",
                FileName = ChatUtils.DecodePayload(parts[4]),
                FilePath = ChatUtils.DecodePayload(parts[5]),
                IsImage = parts[6] == "1"
            };
        }
    }

    public void Append(HistoryEntry entry)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            string line = string.Join("|",
                entry.Kind,
                ChatUtils.EncodePayload(entry.Author),
                ChatUtils.EncodePayload(entry.Message),
                entry.Own ? "1" : "0",
                ChatUtils.EncodePayload(entry.FileName),
                ChatUtils.EncodePayload(entry.FilePath),
                entry.IsImage ? "1" : "0");
            File.AppendAllText(filePath, line + Environment.NewLine, Encoding.UTF8);
            Trim();
        }
        catch { }
    }

    private void Trim()
    {
        try
        {
            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            if (lines.Length > MaxHistoryEntries)
                File.WriteAllLines(filePath, lines.Skip(lines.Length - MaxHistoryEntries).ToArray(), Encoding.UTF8);
        }
        catch { }
    }

    public void Clear()
    {
        try { if (File.Exists(filePath)) File.Delete(filePath); } catch { }
    }
}
