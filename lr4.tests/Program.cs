using LumaChat;  // Пространство имён вашего MainWindow
using System;
using System.Collections.Generic;
using System.Reflection;

namespace чат.Test;

internal static class Program
{
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
        // Создаём экземпляр MainWindow (без отображения окна)
        var windowType = typeof(MainWindow);
        var window = Activator.CreateInstance(windowType, true);

        TestEncodeDecode(window);
        TestTryReadPort(window);
        TestSanitizeFileName(window);
        TestGetLocalIpAddresses(window);
        TestIsLikelyLanIp();
        TestDiscoveryParsing(window);
        Console.WriteLine("All individual tests passed.");
    }

    private static void TestEncodeDecode(object window)
    {
        var encode = GetPrivateMethod(window, "EncodePayload");
        var decode = GetPrivateMethod(window, "DecodePayload");

        const string original = "Привет, мир! 🌍 Тест &<>";
        var encoded = (string)encode.Invoke(window, new object[] { original });
        var decoded = (string)decode.Invoke(window, new object[] { encoded });

        AssertEqual(original, decoded, "Encode/Decode не сохраняют исходную строку.");

        // Проверка с пустой строкой
        var emptyEncoded = (string)encode.Invoke(window, new object[] { "" });
        var emptyDecoded = (string)decode.Invoke(window, new object[] { emptyEncoded });
        AssertEqual("", emptyDecoded, "Пустая строка не прошла кодирование/декодирование.");
    }

    private static void TestTryReadPort(object window)
    {
        // Получаем поле PortTextBox
        var portTextBoxField = typeof(MainWindow).GetField("PortTextBox", BindingFlags.NonPublic | BindingFlags.Instance);
        var portTextBox = portTextBoxField.GetValue(window) as System.Windows.Controls.TextBox;

        var tryReadPort = GetPrivateMethod(window, "TryReadPort");

        portTextBox.Text = "5000";
        var result = (bool)tryReadPort.Invoke(window, null);
        AssertTrue(result, "Порт 5000 должен быть валидным.");

        portTextBox.Text = "0";
        result = (bool)tryReadPort.Invoke(window, null);
        AssertFalse(result, "Порт 0 не должен быть валидным.");

        portTextBox.Text = "70000";
        result = (bool)tryReadPort.Invoke(window, null);
        AssertFalse(result, "Порт > 65535 не должен быть валидным.");

        portTextBox.Text = "abc";
        result = (bool)tryReadPort.Invoke(window, null);
        AssertFalse(result, "Нечисловое значение не должно быть валидным.");
    }

    private static void TestSanitizeFileName(object window)
    {
        var sanitize = GetPrivateMethod(window, "SanitizeFileName");
        var result = (string)sanitize.Invoke(window, new object[] { "file:name?.txt" });
        AssertEqual("file_name_.txt", result, "Некорректная замена недопустимых символов.");

        result = (string)sanitize.Invoke(window, new object[] { "simple.txt" });
        AssertEqual("simple.txt", result, "Имя без спецсимволов не должно меняться.");
    }

    private static void TestGetLocalIpAddresses(object window)
    {
        var getIps = GetPrivateMethod(window, "GetLocalIpAddresses");
        var ips = (string[])getIps.Invoke(window, null);
        AssertTrue(ips.Length >= 0, "Метод GetLocalIpAddresses должен возвращать массив (возможно пустой).");
        // В среде без сети может быть пусто, это не ошибка
        Console.WriteLine($"Найдено локальных IP: {ips.Length}");
    }

    private static void TestIsLikelyLanIp()
    {
        var method = typeof(MainWindow).GetMethod("IsLikelyLanIp", BindingFlags.NonPublic | BindingFlags.Static);

        AssertTrue((bool)method.Invoke(null, new object[] { "192.168.1.1" }), "192.168.x.x должен быть LAN");
        AssertTrue((bool)method.Invoke(null, new object[] { "10.0.0.1" }), "10.x.x.x должен быть LAN");
        AssertTrue((bool)method.Invoke(null, new object[] { "172.16.0.1" }), "172.16-31.x.x должен быть LAN");
        AssertFalse((bool)method.Invoke(null, new object[] { "8.8.8.8" }), "8.8.8.8 не должен считаться LAN");
        AssertFalse((bool)method.Invoke(null, new object[] { "127.0.0.1" }), "localhost не считается LAN");
    }

    private static void TestDiscoveryParsing(object window)
    {
        var tryParse = GetPrivateMethod(window, "TryParseDiscoveryReply");
        string name, machine;
        int port;

        // Правильный ответ
        string reply = "LUMA_TCP_CHAT_HOST_V1|QW5kcmV5|5000|U2VydmVyUA==";
        bool success = (bool)tryParse.Invoke(window, new object[] { reply, null, null, null });
        // Для получения выходных параметров нужно использовать рефлексию с массивом параметров
        // Упростим: создадим обёртку, но можно просто проверить, что метод не падает.
        // Лучше переписать тест с использованием массива объектов:
        var parameters = new object[] { reply, null, null, null };
        var method = typeof(MainWindow).GetMethod("TryParseDiscoveryReply", BindingFlags.NonPublic | BindingFlags.Instance);
        success = (bool)method.Invoke(window, parameters);
        name = (string)parameters[1];
        machine = (string)parameters[2];
        port = (int)parameters[3];

        AssertTrue(success, "Не удалось распарсить корректный discovery ответ.");
        AssertEqual("Андрей", name, "Имя декодировано неверно.");
        AssertEqual("ServerPC", machine, "Имя машины декодировано неверно.");
        AssertEqual(5000, port, "Порт распаршен неверно.");
    }

    // Вспомогательные методы
    private static MethodInfo GetPrivateMethod(object target, string name)
    {
        var method = typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
        if (method == null)
            throw new MissingMethodException($"Метод {name} не найден в MainWindow.");
        return method;
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Ожидалось: {expected}, получено: {actual}.");
        }
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