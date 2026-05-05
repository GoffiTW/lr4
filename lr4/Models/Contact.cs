// Models/Contact.cs
namespace LumaChat.Models
{
    public class Contact
    {
        public string Name { get; set; } = "";
        public string IpAddress { get; set; } = "";
        public int Port { get; set; }
        public bool IsOnline { get; set; }
    }
}