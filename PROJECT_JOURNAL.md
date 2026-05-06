# Amber Notes - Project Journal

## Філософія проєкту (Vision)
Amber Notes — це кросплатформний інструмент для ведення нотаток, що поєднує швидкі "нотатки-нагадування" та глибокі "теплі" записи щоденника. 
Основна філософія: приватність, затишок та надійність (у дусі стоїцизму). Жодних виділених серверів, жодного збору даних.

## Технічні обмеження та Стек
- **Платформи:** Desktop (Windows) та Mobile (Android).
- **Стек:** .NET + Avalonia UI (C#).
- **Архітектура:** MVVM (Model-View-ViewModel). Усі спільні компоненти та бізнес-логіка знаходяться в `AmberNotes.Core`. Застосунок реалізовується, як Single-window application.
- **База даних:** Локальна SQLite з шифруванням AES-256 через SQLCipher (активовано у v0.3).
- **Синхронізація:** У майбутньому — зашифровані бекапи/синхронізація через особисті хмари (Google Drive App Data Folder).

## Архітектура безпеки (v0.3+)

### Схема шифрування
```
Майстер-пароль (у пам'яті)
       │
       ▼  PBKDF2-SHA256 (256,000 ітерацій)
       │   Salt: ambernotes.salt (256 біт, генерується при першому запуску)
       │
       ▼
  HEX-ключ (256 біт) ──► PRAGMA key = "x'hexKey'" ──► SQLCipher AES-256
       │
       ▼
  ambernotes.db (зашифрована БД)
```

### Файли сховища
| Файл | Опис | Шифрування |
|------|------|------------|
| `ambernotes.db` | База даних SQLite (SQLCipher AES-256) | ✅ Так |
| `ambernotes.salt` | 32-байтна криптографічна сіль | ❌ Ні (потрібна до відкриття БД) |

**Важлива примітка:** Сіль навмисно зберігається у відкритому вигляді — вона не є секретом. Безпека гарантується майстер-паролем + PBKDF2 (256k ітерацій). Без пароля сіль марна.

### Навігаційна архітектура
```
AppViewModel.CurrentViewModel
       ├── LoginViewModel  →  LoginView   (App Lock, крок 13)
       └── MainViewModel   →  MainView    (Dashboard)

MainWindow / AppView
  └── ContentControl Content="{Binding CurrentViewModel}"
        └── ViewLocator (Application.DataTemplates) — автоматичне мапування VM → View
```

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

**Реалізована версія:** v0.3: The Vault (SQLCipher) ✅
- [x] Крок 10: Заміна інфраструктури БД: `Microsoft.Data.Sqlite` → `Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.bundle_e_sqlcipher` (AES-256). `Batteries_V2.Init()` додано до `Program.cs` (Desktop) та `Application.OnCreate()` (Android).
- [x] Крок 11: Криптографічний сервіс: `CryptoService.cs` — PBKDF2-SHA256 (256,000 ітерацій, 256-біт ключ, 256-біт сіль). `DatabaseService.cs` оновлено: `TryUnlockWithKey()`, `PRAGMA key = "x'hexKey'"`, `DeleteDatabaseFile()` (міграція v0.2).
- [x] Крок 12: `LoginViewModel` (CommunityToolkit.Mvvm): обробка першого запуску/розблокування, PBKDF2 у фоні (`Task.Run`), очищення пароля з пам'яті. `AppViewModel` з `CurrentViewModel` для навігації. `AppView.axaml` (мобільний root). `MainWindow.axaml` оновлено на `ContentControl`.
- [x] Крок 13: `LoginView.axaml` — повноекранний екран авторизації у бурштинових тонах (#2B1B10 фон, #FFBF00 акцент). Поле пароля (PasswordChar), поле підтвердження (IsVisible=IsFirstRun), панель помилок, кнопка «Створити сховище»/«Розблокувати», ProgressBar під час PBKDF2.
- [x] Крок 14: Логіка ініціалізації: перший запуск → `CryptoService.IsFirstRun=true` → показ Confirm-поля + створення сховища; наступні запуски → `IsFirstRun=false` → розблокування. Міграційний guard v0.2→v0.3. `dotnet build` **succeeded** ✅ (0 помилок, 0 попереджень).

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

### 2026-05-06 — Кроки 10-12: The Vault (SQLCipher) — Інфраструктура безпеки
- **Зроблено (Крок 10):** Замінено `Microsoft.Data.Sqlite` → `Microsoft.Data.Sqlite.Core` (shared) + `SQLitePCLRaw.bundle_e_sqlcipher` v2.1.10 (Desktop + Android entry points). `SQLitePCL.Batteries_V2.Init()` викликається в `Program.Main()` (Desktop) та `Application.OnCreate()` (Android) перед будь-яким SQLite-кодом.
- **Зроблено (Крок 11):** `CryptoService.cs` — PBKDF2-SHA256 (256,000 ітерацій, 256-біт ключ/сіль). Сіль зберігається у `ambernotes.salt` поруч з БД. `DatabaseService.cs` повністю переписано: `TryUnlockWithKey(hexKey)` відкриває з'єднання і застосовує `PRAGMA key = "x'hexKey'"` як перший запит; `Initialize()` тепер вимагає попереднього `TryUnlockWithKey()`; `DeleteDatabaseFile()` для міграції v0.2→v0.3.
- **Зроблено (Крок 12):** `LoginViewModel.cs` — обробляє перший запуск (IsFirstRun) та авторизацію. PBKDF2 виконується у `Task.Run` (не блокує UI). Пароль очищується з пам'яті у `finally`. Міграційний guard: якщо перший запуск + стара незашифрована БД існує — видаляємо її. `AppViewModel.cs` — `CurrentViewModel` (LoginViewModel → MainViewModel) для ContentControl-навігації. `AppView.axaml` — мобільний root-view. `MainWindow.axaml` оновлено: `ContentControl Content="{Binding CurrentViewModel}"`. `App.axaml.cs` оновлено: використовує `AppViewModel` + `LoginViewModel`; `SwitchToMain()` викликається після `LoginSucceeded`.
- **Результат:** `dotnet build` — **succeeded** ✅ (0 помилок, 0 попереджень)

### 2026-05-06 — Кроки 13-14: The Vault (SQLCipher) — UI та завершення
- **Зроблено (Крок 13):** `LoginView.axaml` + `LoginView.axaml.cs` — повноекранний екран авторизації у бурштинових тонах. Деталі: фон `#2B1B10`, картка `#3D2514`, акцент `#FFBF00`. Поле пароля (`PasswordChar="•"`, `PlaceholderText`), поле підтвердження (`IsVisible="{Binding IsFirstRun}"`), панель помилок з іконкою ⚠ (`IsVisible="{Binding HasError}"`), кнопка зі стилями hover/pressed/disabled, `ProgressBar IsIndeterminate=True` під час PBKDF2, підказка з попередженням при першому запуску. `KeyBinding Gesture="Enter"` → `SubmitCommand`. Автофокус поля пароля через `AttachedToVisualTree`. Локальні стилі TextBox (amber-tinted, border focus / hover).
- **Зроблено (Крок 14):** Логіка ініціалізації повністю реалізована. Перший запуск — `CryptoService.IsFirstRun=true`, сіль ще не існує → LoginView показує Confirm-поле + кнопку «Створити сховище» → PBKDF2 генерує сіль + ключ → `TryUnlockWithKey()` + `Initialize()` → `SwitchToMain()`. Наступні запуски — сіль існує → `IsFirstRun=false` → тільки поле пароля + кнопка «Розблокувати» → PBKDF2 відтворює той самий ключ → верифікація `SELECT count(*) FROM sqlite_master`. Помилковий пароль → `SqliteException` → поля очищуються. `LoginViewModel` хелпери: `HasError`, `IsNotBusy`, `ButtonText`, `SubtitleText` — через `partial void On*Changed`.
- **Результат:** `dotnet build` — **succeeded** ✅ (0 помилок, 0 попереджень)
- **Статус v0.3:** ЗАВЕРШЕНО ✅
- **Наступний крок:** v0.4 — покращення UX (стилізація MainView, теми, навігація між книгами / пошук).
