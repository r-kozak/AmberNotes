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

**Реалізована версія:** v0.2: SQLite Local (Unencrypted)
- [x] Крок 5: Підключення Microsoft.Data.Sqlite.
- [x] Крок 6: Створення таблиці Books (Id, Name, Default [true/false]).
- [x] Крок 7: Створення таблиці Notes (Id, Title, Content, NoteDateTime, CreatedAt, UpdatedAt, Type: Public/Private). Налаштування зв'язків між таблицями Books та Notes, як One-to-Many.
- [x] Крок 8: Реалізація відображення вікна редагування нотатки з полями (Заголовок, Текст, Книга, Тип, Дата запису).
- [x] Крок 9: Реалізація відображення списку створених нотаток та базового CRUD (створення, читання, редагування, видалення).

## Журнал сесій (Session Log)
### 2026-05-03 — Ініціалізація проєкту
- **Зроблено:** Створено структуру проєкту (.NET + Avalonia UI). Додано `PROJECT_JOURNAL.md` для збереження контексту ШІ.
- **Зроблено:** Налаштовано правила для Cursor AI (`.cursorrules`) та створено базовий інтерфейс з тестовими даними.

### 2026-05-03 — Крок 4: базовий UI
- **Зроблено:** `MainViewModel`: `Greeting` з `[ObservableProperty]`, команда `CreateNote` з `[RelayCommand]`.
- **Зроблено:** `MainView.axaml`: центрований `StackPanel`, `TextBlock` + кнопка.

### 2026-05-04 — Кроки 5-7: Інфраструктура SQLite БД
- **Зроблено (Крок 5):** Підключено `Microsoft.Data.Sqlite` v9.0.4 через Central Package Management (`Directory.Packages.props`). Пакет додано до `AmberNotes.csproj`.
- **Зроблено (Крок 6):** Створено модель `Book` (`Models/Book.cs`) з полями `Id`, `Name`, `IsDefault`. `DatabaseService` створює таблицю `Books` та сідить "My Notes" (IsDefault=1) при першому запуску.
- **Зроблено (Крок 7):** Створено enum `NoteType` (Public/Private) та модель `Note` (`Models/Note.cs`) з полями `Id`, `Title`, `Content`, `NoteDateTime`, `CreatedAt`, `UpdatedAt`, `Type`, `BookId` (FK). Таблиця `Notes` з `FOREIGN KEY (BookId) REFERENCES Books(Id) ON DELETE CASCADE`.
- **Зроблено:** `DatabaseService` (`Services/DatabaseService.cs`) — ініціалізація БД у транзакції, платформо-незалежний шлях (Desktop → `%LOCALAPPDATA%\AmberNotes\ambernotes.db`).
- **Результат:** `dotnet build` — **succeeded** ✅

### 2026-05-04 — Кроки 8-9: UI та CRUD
- **Зроблено (Крок 8):** `NoteEditViewModel` + `NoteEditView.axaml` — вікно редагування/створення нотатки з полями: Заголовок, Текст, Книга (ComboBox), Тип (ComboBox: Public/Private), Дата запису (CalendarDatePicker). Кнопки «Зберегти» / «Скасувати».
- **Зроблено (Крок 9):** `NoteRepository` (`Services/NoteRepository.cs`) — повний CRUD (GetAll, GetById, Create, Update, Delete) з JOIN на Books. `BookRepository` (`Services/BookRepository.cs`) — GetAll, GetDefault. `MainViewModel` оновлено: `ObservableCollection<Note>`, команди `CreateNote`, `EditNote`, `DeleteNote` (з CanExecute). `MainView.axaml` оновлено: тулбар з кнопками, `ListBox` зі списком нотаток (заголовок, превʼю тексту, дата, бейдж типу), empty-state повідомлення.
- **Зроблено:** `App.axaml.cs` — передає `NoteRepository` та `BookRepository` у `MainViewModel` через конструктор.
- **Результат:** `dotnet build` — **succeeded** ✅ (0 помилок)

### 2026-05-04 — Виправлення крашу на Android
- **Проблема:** Android-версія падала з `NotSupportedException` при спробі створити `Window` (NoteEditView).
- **Зроблено:** `NoteEditView` перетворено з `Window` на `UserControl`.
- **Зроблено:** В `MainView.axaml` додано `DialogOverlay` (Panel + Border + ContentControl) для відображення діалогів на мобільних платформах.
- **Зроблено:** В `MainView.axaml.cs` реалізовано адаптивну логіку: на Desktop створюється динамічне `Window` для `NoteEditView`, на Android використовується overlay.
- **Результат:** Додаток стабільно працює на обох платформах. ✅

- **Наступний крок:** v0.3 — покращення UI/UX, стилізація, можливо навігація між книгами.
