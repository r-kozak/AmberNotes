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


## KNOWN ISSUES AND BUGS
1. При створенні або редагуванні нотатки, якщо змінити її тип (приватна -> публічна АБО публічна -> приватна), нотатка видаляється з програми, її більше не існує.
2. UI-проблеми в Андроїд-версії з вільним місцем під написи, перемикачі, кнопки, які наїжджають один на одного.

## FEATURE IMPROVEMENTS
1. Впровадити JetBrainsMono шрифт.


## Поточний статус (Roadmap)
**Реалізована версія:** v0.1: Skeleton & MVVM ✅
- [x] Крок 1: Створення рішення (Core, Desktop, Android).
- [x] Крок 2: Створення PROJECT_JOURNAL.md.
- [x] Крок 3: Впровадження .cursorrules для ШІ.
- [x] Крок 4: Базовий UI (Головне вікно з кнопкою "Створити" і тестовим записом).

**Реалізована версія:** v0.2: SQLite Local (Unencrypted) ✅
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

**Реалізована версія:** v0.4: Writing Experience & Modes ✅
- [x] **Крок 15: Themes.** Створення ResourceDictionary для "Amber Noir" та "Saffron Linen". Налаштування DynamicResource для всіх компонентів.
- [x] **Крок 16: ModeSwitcher.** Реалізація сервісу перемикання режимів та UI-контрола в Header.
- [x] **Крок 17: Security Bridge.** Оновлення логіки входу: запит пароля лише для Приватного режиму. Розділення потоків даних Public/Private.
- [x] **Крок 18: Markdown Core.** Підключення Markdig + Markdown.Avalonia. Створення NoteEditorView з підтримкою Markdown-розмітки та перемикачем Edit/Preview.
- [x] **Крок 19: Single-Window Navigation.** Впровадження ViewLocator або Router для зміни екранів (List <-> Editor) без нових вікон.

**Поточна версія:** v0.5: Google Drive Auth & AppData
- [x] **Крок 20: Google Auth Infrastructure.** `PkceHelper.cs`, `GoogleAuthConfig.cs`, `GoogleTokens.cs` — PKCE RFC 7636, OAuth endpoints, константи конфігурації, зберігання токенів (JSON).
- [x] **Крок 21: GoogleAuthService.** Повний OAuth 2.0 PKCE flow без зовнішніх SDK: Desktop (HttpListener loopback), Android (Custom URI Scheme + static TCS bridge). Обмін коду на токени, refresh, revoke, зберігання на диску.
- [x] **Крок 22: GoogleDriveService + ICloudStorageService.** Реалізація через чистий HttpClient. `ConnectAsync`, `DisconnectAsync`, `TestConnectionAsync` (drive.appdata scope).
- [x] **Крок 23: Android Integration.** `AndroidManifest.xml` — intent-filter для `com.kozak.ambernotes://oauth2callback`. `MainActivity.cs` — `OnNewIntent`/`OnCreate` → `HandleAndroidCallback`. `Application.cs` — реєстрація Android BrowserLauncher (Intent.ActionView).
- [x] **Крок 24: SettingsView.** `SettingsViewModel.cs` + `SettingsView.axaml` — підключення/відключення Google Drive, статус з'єднання, підказки налаштування Cloud Console, тематизація.
- [x] **Крок 25: Navigation Integration.** `MainViewModel.GoToSettingsCommand`, `AppViewModel.SwitchToMain` оновлено з `GoogleDriveService`. ⚙ кнопка в тулбарі `MainView`. `App.axaml` DataTemplate. `App.axaml.cs` — створення + передача сервісів.


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

### 2026-05-07 — Кроки 15-16: Themes & ModeSwitcher (v0.4 початок)

- **Зроблено (Крок 15):** Глобальна система тем на базі `DynamicResource`.
  - `AmberNotes/Styles/Themes/AmberNoir.axaml` — темна тема (#1A1A1B фон, #FFBF00 акцент). Визначає 15 семантичних ресурсів-пензлів: `AppBackground`, `AppSurface`, `AppSurfaceVariant`, `AppToolbar`, `AppOnBackground`, `AppOnSurface`, `AppSubtext`, `AppPrimary`, `AppOnPrimary`, `AppSecondary`, `AppBorder`, `AppDivider`, `AppPrivateGlow`, `AppPrivateBadge`, `AppPublicBadge`.
  - `AmberNotes/Styles/Themes/SaffronLinen.axaml` — світла тема (#F4F1EA фон, #D97706 акцент). Ті ж самі ключі — гарантує сумісність між темами.
  - `ThemeService.cs` (Singleton) — `SetTheme()` + `ToggleTheme()` + `Initialize()`. При перемиканні: видаляє старий `ResourceInclude` з `Application.Resources.MergedDictionaries`, додає новий, оновлює `app.RequestedThemeVariant` (Dark/Light) для FluentTheme. Працює на Desktop і Android однаково через `avares://` URIs.
  - `App.axaml` оновлено: `RequestedThemeVariant="Dark"`, `<ResourceInclude Source="avares://AmberNotes/Styles/Themes/AmberNoir.axaml"/>` як дефолт.
  - `App.axaml.cs` оновлено: `ThemeService.Instance.Initialize()` викликається першим у `OnFrameworkInitializationCompleted`.
  - `AmberNotes.csproj` оновлено: `<AvaloniaResource Include="Styles\**" />`.
  - `MainView.axaml` повністю переписано: всі кольори замінені на `DynamicResource` (AppBackground, AppToolbar, AppBorder, AppSubtext, AppSurface, AppSurfaceVariant, AppOnSurface, AppPrimary, AppOnPrimary, AppSecondary).

- **Зроблено (Крок 16):** ModeSwitcher — Public/Private режим.
  - `ModeService.cs` (Singleton) — `SetMode()`, `ToggleMode()`, подія `ModeChanged`. Стан: Public (за замовчуванням) / Private.
  - `MainViewModel.cs` оновлено: `IsPrivateMode` + `IsPublicMode` (inverse), `IsDarkTheme`, `ThemeToggleIcon`, `ThemeToggleTip`; команди `SetPublicModeCommand`, `SetPrivateModeCommand`, `ToggleThemeCommand`; підписка на singleton-події; `LoadNotes()` фільтрує нотатки за типом (Public/Private відповідно до режиму).
  - `MainView.axaml` Header: 3-колонковий Grid — [CRUD кнопки | Segmented Mode Switcher | Theme Toggle]. Mode Switcher: активний стан — статичний `Border` з кольором акценту, неактивний — `Button` з прозорим фоном. Theme Toggle: кнопка 🌙/☀ з тултипом.
  - **Visual Cue (Private mode):** Напівпрозорий `Panel` з `AppPrivateGlow` `Border` (BorderThickness=3) покриває весь view (`ZIndex=500`, `IsHitTestVisible=False`) коли `IsPrivateMode=true`.
- **Результат:** `dotnet build` — **succeeded** ✅ (0 помилок, Desktop + Android)
- **Наступний крок:** Крок 17 — Security Bridge (password re-prompt для Private mode, відокремлена public.db).

### 2026-05-07 — Крок 17: Security Bridge (v0.4 продовження) — ФІНАЛЬНА ВЕРСІЯ

- **Ключова логіка запуску (виправлено):**
  - Застосунок **завжди** стартує в Public режимі без будь-якого LoginView.
  - Пароль **ніколи** не запитується при запуску — користувач може використовувати публічні нотатки нескінченно без пароля.
  - LoginView з'являється **лише** коли користувач натискає "🔒 Приватний":
    - немає `ambernotes.salt` (перший вхід у приватний) → LoginView з формою **створення** сховища.
    - `ambernotes.salt` є → LoginView з формою **розблокування** сховища.
    - При скасуванні → повернення до MainView в Public режимі без змін.

- **Зроблено:** Розділення даних на дві незалежних бази даних:
  - `public.db` — незашифрована plain SQLite. Містить публічні нотатки (`NoteType.Public`). Відкривається при запуску.
  - `ambernotes.db` — SQLCipher AES-256. Містить приватні нотатки (`NoteType.Private`). Відкривається лише після введення майстер-пароля в Private режимі.

- **Зроблено (DatabaseService.cs):** `UnlockAsPlain()` + `OpenConnectionPlain()`. `OpenConnection()` перевіряє `_isPlain` — без `PRAGMA key` для public.db.

- **Зроблено (App.axaml.cs):** Простий лінійний старт без розгалуження:
  1. `ThemeService.Instance.Initialize()`
  2. `publicDbService.UnlockAsPlain()` + `Initialize()` → публічні репо.
  3. `privateDbService` + `cryptoSvc` — тільки конфігуруються, не відкриваються.
  4. `appVm.SwitchToMain(...)` → одразу MainView в Public режимі.
  5. `mainVm.PrivateLoginRequested += () => appVm.ShowPrivateLogin(...)` — підписка на подію.

- **Зроблено (AppViewModel.cs):** Спрощено до одного конструктора. `SwitchToMain()` тепер повертає `MainViewModel` (для підписки на події). Новий метод `ShowPrivateLogin(cryptoSvc, privateDbService, mainVm)`:
  - Створює `LoginViewModel` з `CanCancel=true`.
  - Підписується на `LoginSucceeded` → будує приватні репо → `mainVm.SetPrivateRepos()` → `ModeService.SetMode(Private)` → `CurrentViewModel = mainVm`.
  - Підписується на `LoginCancelled` → `CurrentViewModel = mainVm` (без зміни режиму).

- **Зроблено (LoginViewModel.cs):** Нові властивість `CanCancel` (`[ObservableProperty]`), подія `LoginCancelled`, команда `CancelCommand` — очищає поля та викликає `LoginCancelled`.

- **Зроблено (LoginView.axaml):** Кнопка "Скасувати" (`IsVisible="{Binding CanCancel}"`) — прозорий стиль з бурштиновою рамкою; розташована після security hint.

- **Зроблено (MainViewModel.cs):**
  - Видалено `UnlockOverlay`/`IsUnlockOverlayVisible`/`PrivateUnlockViewModel`.
  - Додано `event Action? PrivateLoginRequested` — стріляє в `SetPrivateModeCommand` коли приватний репо ще null.
  - Додано `SetPrivateRepos(NoteRepository, BookRepository)` — викликається `AppViewModel` після успішного входу.
  - `LoadNotes()` — гвард проти null при недоступному приватному репо.

- **Зроблено (MainView.axaml):** Видалено `Panel (ZIndex=800)` з `ContentControl Content="{Binding UnlockOverlay}"`. `PrivateUnlockView` overlay більше не потрібен.

- **Результат:** `dotnet build` — **succeeded** ✅ (0 помилок, 0 попереджень, Desktop)

### 2026-05-07 — Крок 18: Markdown Core (v0.4 продовження)

- **NuGet пакети:** Додано `Markdig 1.1.3` та `Markdown.Avalonia 12.0.0-a3` (pre-release, сумісний з Avalonia 12.x) до `Directory.Packages.props` та `AmberNotes.csproj`.

- **Зроблено (NoteEditViewModel.cs):**
  - Нова властивість `IsEditMode` (`[ObservableProperty]`, default `true`) — перемикач режимів.
  - Обчислювана `IsPreviewMode` (= `!IsEditMode`) для `IsVisible` в UI.
  - Обчислювана `EditorModeTip` — tooltip для сегментованого перемикача.
  - Нова команда `ToggleEditorModeCommand` (`[RelayCommand]`) — інвертує `IsEditMode`.

- **Зроблено (NoteEditView.axaml) — повна переробка:**
  - **[Row 0] Header:** Назва вікна (`WindowTitle`) + Segmented-перемикач "✏ Редагування / 👁 Перегляд". Активна таблетка — бурштиновий `Border` (amber `AppPrimary`), неактивна — прозорий `Button`. DynamicResource кольори.
  - **[Row 1] Meta-смуга:** Поле заголовку (`FontSize=15, SemiBold, AppSurfaceVariant фон, без рамки`) + рядок Книга/Тип/Дата в `Grid` 3 колонки. `DataTemplate x:DataType="models:Book"` для ComboBox.
  - **[Row 2] Контент:**
    - *Edit mode:* `TextBox` з моноширинним шрифтом `"Cascadia Code,Cascadia Mono,Courier New,Monospace"`, `FontSize=13`, `LineHeight=20`, `AcceptsReturn=True`, `TextWrapping=Wrap`, `AppBackground/AppOnBackground` — повністю тематизований.
    - *Preview mode:* `md:MarkdownScrollViewer` (з namespace `using:Markdown.Avalonia`) із прив'язкою `Markdown="{Binding Content}"`, загорнутий у `Border` з `AppBackground`.
  - **[Row 3] Footer:** Кнопки "Скасувати" + "Зберегти" (клас `accent`, локальні стилі з `AppPrimary/AppOnPrimary`).
  - Всі кольори через `DynamicResource` — теми Amber Noir / Saffron Linen переключаються миттєво.

- **Зроблено (App.axaml):** Namespace `Markdown.Avalonia` оголошено на кореневому елементі. В цій alpha-версії `MarkdownScrollViewer` реєструє стилі самостійно — явне додавання до `Application.Styles` не потрібне.

- **Технічна примітка:** `Markdown.Avalonia 12.0.0-a3` — `MarkdownStyle` у цьому релізі не реалізує `Avalonia.Styling.IStyle` і не потребує явного додавання до `Application.Styles`. `MarkdownScrollViewer` рендерить Markdown через внутрішній Markdig-пайплайн, нативними Avalonia-контролами.

- **Результат:** `dotnet build` — **succeeded** ✅ (0 помилок, 0 попереджень, Desktop)

### 2026-05-07 — Крок 19: Single-Window Navigation (v0.4 завершення)

- **Ключова зміна:** Повна реорганізація навігації — жодних нових `Window`, лише `CurrentPage` у `MainViewModel`.

- **Нові файли:**
  - `MainListViewModel.cs` — ViewModel для сторінки списку нотаток. Отримує спільний `ObservableCollection<Note>` із `MainViewModel`. Команди `CreateNoteCommand`, `EditNoteCommand`, `DeleteNoteCommand` + `SelectedNote`. Делегує навігацію та видалення через `Action`-callbacks у `MainViewModel`.
  - `MainListView.axaml` / `.cs` — View для сторінки списку. Власний CRUD-тулбар (Create/Edit/Delete) + `ListBox` зі списком нотаток. `x:DataType="MainListViewModel"`.

- **Оновлено (MainViewModel.cs):**
  - Додано `[ObservableProperty] ViewModelBase _currentPage` — точка навігації.
  - У конструкторі: `_listVm = new MainListViewModel(Notes, GoToEditor, id => NoteRepo.Delete(id))` → `CurrentPage = _listVm`.
  - `private void GoToEditor(int? noteId)` — створює `NoteEditViewModel`, підписується на `Saved` (LoadNotes + GoBackToList) та `Cancelled` (GoBackToList), встановлює `CurrentPage = editVm`.
  - `private void GoBackToList()` — `CurrentPage = _listVm`.
  - Видалено: `OpenNoteEditRequested` event, `CreateNote`/`EditNote`/`DeleteNote`/`SelectedNote` (перенесено в `MainListViewModel`), `RefreshNotes()`.

- **Оновлено (MainView.axaml):**
  - Shell із тулбаром (лише Mode Switcher + Theme Toggle — без CRUD кнопок).
  - `TransitioningContentControl Content="{Binding CurrentPage}"` + `CrossFade Duration="0:0:0.25"` замість списку нотаток.
  - Збережено Private Mode Glow overlay (ZIndex=500).
  - Видалено: DialogOverlay / DialogContent (більше не потрібні).

- **Оновлено (MainView.axaml.cs):** Зведено до мінімуму — лише `InitializeComponent()`. Жодної логіки `Window.ShowDialog`, `DialogOverlay`, подій.

- **Оновлено (MainWindow.axaml):** `ContentControl` → `TransitioningContentControl Content="{Binding CurrentViewModel}"` + `CrossFade Duration="0:0:0.25"` (плавний перехід Login ↔ Main).

- **Оновлено (App.axaml):**
  - Додано `xmlns:vm` та `xmlns:views` namespace.
  - Явні `DataTemplate` перед `ViewLocator`: `MainListViewModel → MainListView`, `NoteEditViewModel → NoteEditView`.
  - `ViewLocator` залишається як fallback для `LoginViewModel`, `MainViewModel`, `AppViewModel`.

- **Результат:** `dotnet build` — **succeeded** ✅ (0 помилок, 0 попереджень, Desktop)
- **Статус v0.4:** ЗАВЕРШЕНО ✅

### 2026-05-11 — Кроки 20-25: Google Drive Auth & AppData (v0.5)

- **Ключова архітектурна рішення:**
  - Реалізовано без зовнішніх Google SDK (без `Google.Apis.Drive.v3`) — чистий `HttpClient` + PKCE. Lean підхід (vibe coding).
  - Платформна абстракція `GoogleAuthService.BrowserLauncher` (static `Func<string, Task>`) — Desktop використовує `Process.Start`, Android переписує у `Application.OnCreate()` через `Intent.ActionView`.
  - Android callback через статичний `TaskCompletionSource` (bridge pattern) — `MainActivity.OnNewIntent` → `GoogleAuthService.HandleAndroidCallback()`.

- **Нові файли (AmberNotes/Services/):**
  - `PkceHelper.cs` — PKCE RFC 7636: генерація code_verifier (96 random bytes → base64url) + code_challenge (SHA256 → base64url).
  - `GoogleAuthConfig.cs` — OAuth constants, endpoints, scopes (`drive.appdata`+`userinfo.email`), Android redirect URI, хелпери `IsCurrentPlatformConfigured`.
  - `GoogleTokens.cs` — DTO з `[JsonPropertyName]`, `ExpiresAtUtc`, `IsExpired` (60-секундний буфер).
  - `GoogleAuthService.cs` — повний OAuth 2.0 PKCE flow: Desktop (HttpListener loopback, 5хв таймаут, success HTML page), Android (Custom URI TCS bridge). `SignInAsync`, `RefreshAsync`, `RevokeAndClearAsync`. Токени → `googletokens.json`.
  - `ICloudStorageService.cs` — інтерфейс: `IsConnected`, `ConnectedEmail`, `ConnectAsync`, `DisconnectAsync`, `TestConnectionAsync`.
  - `GoogleDriveService.cs` — реалізація `ICloudStorageService` через Drive REST API + `HttpClient`. `TestConnectionAsync` перевіряє `appDataFolder` scope, авто-рефреш.

- **Нові файли (AmberNotes/ViewModels/ + Views/):**
  - `SettingsViewModel.cs` — `ToggleGoogleDriveCommand` (connect/disconnect), `TestConnectionCommand`, `IsGoogleConnected`, `GoogleEmail`, `IsBusy`, `ShowSetupHint` (якщо Client IDs не налаштовані).
  - `SettingsView.axaml` — Google Drive картка (статус, прогрес-бар, кнопки підключення/тесту), картка-інструкція Google Cloud Console. Повна тематизація через DynamicResource.

- **Оновлені файли:**
  - `AndroidManifest.xml` — `<activity>` з `<intent-filter>` для scheme=`com.kozak.ambernotes`, host=`oauth2callback`, launchMode=`singleTop`.
  - `MainActivity.cs` — `LaunchMode.SingleTop`, `OnCreate`+`OnNewIntent` → `HandleOAuthIntent` → `GoogleAuthService.HandleAndroidCallback`.
  - `Application.cs` — реєстрація `GoogleAuthService.BrowserLauncher` через `Intent.ActionView` + `ActivityFlags.NewTask`.
  - `MainViewModel.cs` — 5-й параметр `GoogleDriveService`, `GoToSettingsCommand`→`GoToSettings()` (CurrentPage = SettingsViewModel).
  - `AppViewModel.cs` — `SwitchToMain` приймає `GoogleDriveService`.
  - `App.axaml.cs` — `GetAppDataFolder()` (уніфікований шлях замість 2 окремих helper), створення `GoogleAuthService`+`GoogleDriveService`, передача до `SwitchToMain`.
  - `App.axaml` — `DataTemplate: SettingsViewModel → SettingsView`.
  - `MainView.axaml` — ⚙ кнопка "Налаштування" поруч з 🌙/☀ у тулбарі.

- **Результат:** `dotnet build AmberNotes.Desktop` — **succeeded** ✅ (0 помилок)
- **Статус v0.5:** ЗАВЕРШЕНО ✅

- **Наступні кроки для активації:**
  1. Відкрити `console.cloud.google.com` → створити проєкт → увімкнути Google Drive API.
  2. Credentials → OAuth Client ID (Desktop app) → скопіювати Client ID + Secret у `GoogleAuthConfig.cs`.
  3. Credentials → OAuth Client ID (Android) → Package `com.kozak.AmberNotes` → SHA-1 → скопіювати Client ID.
  4. Запустити застосунок → ⚙ → "Підключити Google Drive".
