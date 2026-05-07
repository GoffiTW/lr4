using System;

namespace LumaChat.Services;

public sealed class ChatTextReceivedEventArgs : EventArgs
{
    public string Author { get; }
    public string Text { get; }
    public ChatTextReceivedEventArgs(string author, string text)
    {
        Author = author;
        Text = text;
    }
}

public sealed class ChatFileReceivedEventArgs : EventArgs
{
    public string Author { get; }
    public string FileName { get; }
    public string SavedPath { get; }
    public ChatFileReceivedEventArgs(string author, string fileName, string savedPath)
    {
        Author = author;
        FileName = fileName;
        SavedPath = savedPath;
    }
}
