using System;
using System.Threading.Tasks;
using LumaChat.Models;

namespace LumaChat.Services;

public interface IPresenceService
{
    event EventHandler<ContactInfo>? ContactDiscovered;

    void Start(Func<int> getPort, Func<string> getName);
    void StartHostResponder(int port, string name, string machine);
    void StopHostResponder();
    Task<int> ProbeOnceAsync(int timeoutMs = 1800);
    void Stop();
}
