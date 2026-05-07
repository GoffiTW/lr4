using System;

namespace LumaChat.Services;

public enum ConnectionState
{
    Disconnected,
    WaitingForClient,
    Connecting,
    Connected
}
