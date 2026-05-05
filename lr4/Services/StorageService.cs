// Services/StorageService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LumaChat.Models;

namespace LumaChat.Services
{
    public class StorageService : IStorageService
    {
        private const int MaxHistoryEntries = 200;
        private readonly string _historyPath;

        public StorageService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _historyPath = Path.Combine(appData, "LumaChat", "history.log");
            Directory.CreateDirectory(Path.GetDirectoryName(_historyPath)!);
        }

        public void AppendMessage(Message message)
        {
            try
            {
                string line = SerializeMessage(message);
                File.AppendAllText(_historyPath, line + Environment.NewLine, Encoding.UTF8);
                TrimHistory();
            }
            catch { /* ignore */ }
        }

        public List<Message> LoadMessages()
        {
            var messages = new List<Message>();
            if (!File.Exists(_historyPath)) return messages;

            foreach (string line in File.ReadAllLines(_historyPath, Encoding.UTF8))
            {
                var msg = DeserializeMessage(line);
                if (msg != null) messages.Add(msg);
            }
            return messages;
        }

        public void ClearHistory()
        {
            try { File.Delete(_historyPath); } catch { }
        }

        private string SerializeMessage(Message msg)
        {
            // Простое форматирование: тип|автор|текст|время|своё|путь|имя_файла
            return string.Join("|",
                msg.IsFileTransfer ? "FILE" : "TEXT",
                EncodeBase64(msg.Author),
                EncodeBase64(msg.Text),
                msg.Time.Ticks.ToString(),
                msg.IsOwn ? "1" : "0",
                EncodeBase64(msg.FilePath ?? ""),
                EncodeBase64(msg.FileName ?? ""));
        }

        private Message? DeserializeMessage(string line)
        {
            var parts = line.Split('|');
            if (parts.Length < 7) return null;
            string kind = parts[0];
            string author = DecodeBase64(parts[1]);
            string text = DecodeBase64(parts[2]);
            long ticks = long.Parse(parts[3]);
            bool isOwn = parts[4] == "1";
            string filePath = DecodeBase64(parts[5]);
            string fileName = DecodeBase64(parts[6]);

            return new Message
            {
                Author = author,
                Text = text,
                Time = new DateTime(ticks),
                IsOwn = isOwn,
                FilePath = string.IsNullOrEmpty(filePath) ? null : filePath,
                FileName = string.IsNullOrEmpty(fileName) ? null : fileName
            };
        }

        private static string EncodeBase64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
        private static string DecodeBase64(string s)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
            catch { return s; }
        }

        private void TrimHistory()
        {
            var lines = File.ReadAllLines(_historyPath, Encoding.UTF8);
            if (lines.Length > MaxHistoryEntries)
                File.WriteAllLines(_historyPath, lines.Skip(lines.Length - MaxHistoryEntries).ToArray(), Encoding.UTF8);
        }
    }
}