# Amber Notes - Project Journal

## Філософія проєкту (Vision)
Amber Notes — це кросплатформний інструмент для ведення нотаток, що поєднує швидкі "нотатки-нагадування" та глибокі "теплі" записи щоденника. 
Основна філософія: приватність, затишок та надійність (у дусі стоїцизму). Жодних виділених серверів, жодного збору даних.

## Технічні обмеження та Стек
- **Платформи:** Desktop (Windows) та Mobile (Android).
- **Стек:** .NET + Avalonia UI (C#).
- **Архітектура:** MVVM (Model-View-ViewModel). Усі спільні компоненти та бізнес-логіка знаходяться в `AmberNotes.Core`.
- **База даних:** Локальна SQLite з обов'язковим шифруванням (в планах SQLCipher).
- **Синхронізація:** У майбутньому — зашифровані бекапи/синхронізація через особисті хмари (Google Drive App Data Folder).

## Поточний статус (Roadmap)
**Реалізована версія:** v0.1: Skeleton & MVVM
- [x] Крок 1: Створення рішення (Core, Desktop, Android).
- [x] Крок 2: Створення PROJECT_JOURNAL.md.
- [x] Крок 3: Впровадження .cursorrules для ШІ.
- [x] Крок 4: Базовий UI (Головне вікно з кнопкою "Створити" і тестовим записом).
**Поточна версія:** v0.2: SQLite Local (Unencrypted)
- [x] Крок 5: Підключення Microsoft.Data.Sqlite.
- [x] Крок 6: Створення таблиці Books (Id, Name, Default [true/false]).
- [x] Крок 7: Створення таблиці Notes (Id, Title, Content, NoteDateTime, CreatedAt, UpdatedAt, Type: Open/Closed). Налаштування зв'язків між таблицями Books та Notes, як One-to-Many.
- [ ] Крок 8: Реалізація відображення вікна редагування нотатки з полями (Заголовок, Текст, Книга, Тип, Дата запису).
- [ ] Крок 9: Реалізація відображення списку створених нотаток та базового CRUD (створення, читання, редагування, видалення).

## Журнал сесій (Session Log)
### 2026-05-03 - Ініціалізація проєкту
- **Зроблено:** Створено структуру проєкту (.NET + Avalonia UI). Додано `PROJECT_JOURNAL.md` для збереження контексту ШІ.
- **Зроблено:** Налаштувати правила для Cursor AI (`.cursorrules`) та створити базовий інтерфейс з тестовими даними.
### 2026-05-03 — Крок 4: базовий UI
- **Зроблено:** `MainViewModel`: `Greeting` з `[ObservableProperty]`, команда `CreateNote` з `[RelayCommand]` (оновлення привітання після «Створити»).
- **Зроблено:** `MainView.axaml`: центрований `StackPanel`, `TextBlock` + кнопка з відступами та розмірами шрифту.
- **Наступний крок:** Продовжити roadmap після Кроку 4 (модель нотаток, навігація тощо за планом).
### 2026-05-04 — Кроки 5-7: Інфраструктура SQLite БД
- **Зроблено (Крок 5):** Підключено `Microsoft.Data.Sqlite` v9.0.4 через Central Package Management (`Directory.Packages.props`). Пакет додано до `AmberNotes.csproj`.
- **Зроблено (Крок 6):** Створено модель `Book` (`AmberNotes/Models/Book.cs`) з полями `Id`, `Name`, `IsDefault`. `DatabaseService` створює таблицю `Books` та автоматично сідить запис "My Notes" (IsDefault=1) при першому запуску.
- **Зроблено (Крок 7):** Створено enum `NoteType` (Open/Closed) та модель `Note` (`AmberNotes/Models/Note.cs`) з полями `Id`, `Title`, `Content`, `NoteDateTime`, `CreatedAt`, `UpdatedAt`, `Type`, `BookId` (FK). `DatabaseService` створює таблицю `Notes` з `FOREIGN KEY (BookId) REFERENCES Books(Id) ON DELETE CASCADE`.
- **Зроблено:** Створено `DatabaseService` (`AmberNotes/Services/DatabaseService.cs`) — ініціалізація БД, створення таблиць у транзакції, сід дефолтної книги.
- **Зроблено:** `App.axaml.cs` оновлено — `DatabaseService.Initialize()` викликається при старті. Шлях до БД: Desktop → `%LOCALAPPDATA%\AmberNotes\ambernotes.db`, Android → app-private storage.
- **Результат:** `dotnet build` — **succeeded** ✅ (0 помилок, 0 попереджень).
- **Наступний крок:** Кроки 8-9 — UI редагування нотатки та список нотаток з CRUD.

