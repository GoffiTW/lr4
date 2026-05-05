using System;
using System.IO;
using System.Collections.Generic;
using LumaChat;

namespace lr4.tests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            RunTests();
            Console.WriteLine("All tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test failed: {ex.Message}");
            return 1;
        }
    }

    private static void RunTests()
    {
        TestEncodeDecode();
        TestTryReadPort();
        TestSanitizeFileName();
        TestGetLocalIpAddresses();
        TestIsLikelyLanIp();
        TestDiscoveryParsing();
        TestChatServiceMessageRoundTrip();
        Console.WriteLine("All individual tests passed.");
    }

    private static void TestChatServiceMessageRoundTrip()
    {
        var hostSvc = new LumaChat.Services.ChatService();
        var clientSvc = new LumaChat.Services.ChatService();

        string? receivedByHost = null;
        string? receivedByClient = null;
        hostSvc.TextReceived += (_, e) => receivedByHost = e.Text;
        clientSvc.TextReceived += (_, e) => receivedByClient = e.Text;

        const int port = 5061;
        var hostTask = hostSvc.HostAsync(port, "Host");
        System.Threading.Thread.Sleep(300);
        clientSvc.ConnectAsync("127.0.0.1", port, "Client").GetAwaiter().GetResult();
        System.Threading.Thread.Sleep(500);

        AssertEqual(LumaChat.Services.ConnectionState.Connected, hostSvc.State, "Host should be connected");
        AssertEqual(LumaChat.Services.ConnectionState.Connected, clientSvc.State, "Client should be connected");
        AssertTrue(hostSvc.UseLumaProtocol, "Host should use Luma");
        AssertTrue(clientSvc.UseLumaProtocol, "Client should use Luma");

        clientSvc.SendTextAsync("hello-from-client").GetAwaiter().GetResult();
        System.Threading.Thread.Sleep(300);
        hostSvc.SendTextAsync("hello-from-host").GetAwaiter().GetResult();
        System.Threading.Thread.Sleep(300);

        AssertEqual("hello-from-client", receivedByHost ?? "", "Host should receive client message");
        AssertEqual("hello-from-host", receivedByClient ?? "", "Client should receive host message");

        clientSvc.Disconnect();
        hostSvc.Disconnect();
    }

    private static void TestEncodeDecode()
    {
        const string original = "Привет, мир! 🌍 Тест &<>";
        var encoded = ChatUtils.EncodePayload(original);
        var decoded = ChatUtils.DecodePayload(encoded);
        AssertEqual(original, decoded, "Encode/Decode не сохраняют исходную строку.");

        var emptyEncoded = ChatUtils.EncodePayload("");
        var emptyDecoded = ChatUtils.DecodePayload(emptyEncoded);
        AssertEqual("", emptyDecoded, "Пустая строка не прошла кодирование.");
    }

    private static void TestTryReadPort()
    {
        var valid = ChatUtils.TryReadPort("5000", out int port);
        AssertTrue(valid, "Порт 5000 должен быть валидным.");
        AssertEqual(5000, port, "Значение порта не совпадает.");

        AssertFalse(ChatUtils.TryReadPort("0", out _), "Порт 0 не должен быть валидным.");
        AssertFalse(ChatUtils.TryReadPort("70000", out _), "Порт > 65535 невалидный.");
        AssertFalse(ChatUtils.TryReadPort("abc", out _), "Нечисловое значение невалидно.");
    }

    private static void TestSanitizeFileName()
    {
        var result = ChatUtils.SanitizeFileName("file:name?.txt");
        AssertEqual("file_name_.txt", result, "Некорректная замена недопустимых символов.");

        result = ChatUtils.SanitizeFileName("simple.txt");
        AssertEqual("simple.txt", result, "Имя без спецсимволов не должно меняться.");
    }

    private static void TestGetLocalIpAddresses()
    {
        var ips = ChatUtils.GetLocalIpAddresses();
        AssertTrue(ips.Length >= 0, "Метод должен возвращать массив (возможно пустой).");
        Console.WriteLine($"Найдено локальных IP: {ips.Length}");
    }

    private static void TestIsLikelyLanIp()
    {
        AssertTrue(ChatUtils.IsLikelyLanIp("192.168.1.1"), "192.168.x.x должен быть LAN");
        AssertTrue(ChatUtils.IsLikelyLanIp("10.0.0.1"), "10.x.x.x должен быть LAN");
        AssertTrue(ChatUtils.IsLikelyLanIp("172.16.0.1"), "172.16-31.x.x должен быть LAN");
        AssertFalse(ChatUtils.IsLikelyLanIp("8.8.8.8"), "8.8.8.8 не должен считаться LAN");
        AssertFalse(ChatUtils.IsLikelyLanIp("127.0.0.1"), "localhost не считается LAN");
    }
    private static void TestDiscoveryParsing()
    {
        // Генерируем правильные данные динамически
        string expectedName = "Андрей";
        string expectedMachine = "ServerPC";
        int expectedPort = 5000;

        string base64Name = ChatUtils.EncodePayload(expectedName);
        string base64Machine = ChatUtils.EncodePayload(expectedMachine);
        string reply = $"LUMA_TCP_CHAT_HOST_V1|{base64Name}|{expectedPort}|{base64Machine}";

        bool success = ChatUtils.TryParseDiscoveryReply(reply, out string name, out string machine, out int port);

        AssertTrue(success, "Не удалось распарсить корректный discovery ответ.");
        AssertEqual(expectedName, name, "Имя декодировано неверно.");
        AssertEqual(expectedMachine, machine, "Имя машины декодировано неверно.");
        AssertEqual(expectedPort, port, "Порт распаршен неверно.");

        // Негативный тест
        success = ChatUtils.TryParseDiscoveryReply("wrong", out _, out _, out _);
        AssertFalse(success, "Неверный ответ не должен парситься.");
    }

    // Вспомогательные методы Assert
    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Ожидалось: {expected}, получено: {actual}.");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message)
    {
        if (condition) throw new InvalidOperationException(message);
    }
}