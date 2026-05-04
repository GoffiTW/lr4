# Документация Luma TCP Chat

Добро пожаловать в документацию по API и архитектуре приложения.

## Основные классы

- `LumaChat.MainWindow` — главное окно, содержит всю логику чата.
- `LumaChat.ContactInfo` — модель данных контакта.
- `LumaChat.ChatMessageViewModel` — модель сообщения.

## Генерация документации

Документация создаётся автоматически из XML-комментариев в коде. Чтобы обновить её, выполните:

```bash
dotnet tool install -g docfx
cd docs
docfx docfx.json