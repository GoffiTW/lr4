// Models/Message.cs
namespace LumaChat.Models
{
    public class Message
    {
        public string Author { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTime Time { get; set; }
        public bool IsOwn { get; set; }
        public bool IsSystem { get; set; }
        public string? FilePath { get; set; }
        public string? FileName { get; set; }
        public double TransferProgress { get; set; } // 0..1
    }
}