// Services/IStorageService.cs
using System.Collections.Generic;
using LumaChat.Models;

namespace LumaChat.Services
{
    public interface IStorageService
    {
        void AppendMessage(Message message);
        List<Message> LoadMessages();
        void ClearHistory();
    }
}