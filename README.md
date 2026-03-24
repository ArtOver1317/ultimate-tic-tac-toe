# ultimate-tic-tac-toe

**EN** | [RU](#ru)

A collection of classic board games playable in the browser, built with Unity WebGL.
Includes online multiplayer, AI opponents, and a scalable architecture designed to support multiple games.

> **[▶ Play in Browser](https://artover1317.github.io/ultimate-tic-tac-toe/)**

---

## Games

| Game | Status | Highlights |
|---|---|---|
| **Classic Tic-Tac-Toe** | ✅ Done | Configurable NxN board, local & online, AI bot, move timer |
| **Ultimate Tic-Tac-Toe** | ✅ Done | 9 mini-boards (81 cells), strategic placement constraint, series scoring |
| **Battleship** | 🚧 In Development | Placement phase + combat, turn timers, bot, online |

More games (checkers, etc.) are planned — the architecture is built to support them.

---

## Tech Stack

| Area | Technology |
|---|---|
| Engine | Unity 6 (WebGL) |
| Render Pipeline | URP |
| Game Logic | [Morpeh ECS](https://github.com/scellecs/morpeh) |
| UI | Unity UI Toolkit (UXML / USS) |
| Reactivity / MVVM | [R3](https://github.com/Cysharp/R3) |
| Dependency Injection | [VContainer](https://github.com/hadashiA/VContainer) |
| Online Multiplayer | [Photon Fusion 2](https://www.photonengine.com/fusion) + Photon Cloud |
| Async | [UniTask](https://github.com/Cysharp/UniTask) |
| Tweening | [LitMotion](https://github.com/AnnulusGames/LitMotion) |
| Assets | Unity Addressables |
| Localization | Unity Localization (EN / RU / JA) |

---

## Architecture

```
View  (UI Toolkit / MonoBehaviour)
  ↕  R3 reactive bindings  (MVVM)
ViewModel
  ↕  Service Layer  (IGameService)
Game Logic  (Morpeh ECS — rules, FSM, AI, moves)
```

### Key Design Decisions

**ECS for game logic.**
All rules, win conditions, AI, and move timers live in Morpeh Systems and Components.
MonoBehaviour is used only for View and Unity-specific concerns (camera, audio, input).

**MVVM + R3.**
ViewModels subscribe to ECS-published events via reactive streams.
Views know nothing about ECS or game rules — only about what to display.

**Game Catalog + Strategy pattern.**
Adding a new game means implementing `IGameStrategy` + `IGameConfig` and registering them in the catalog.
There is a single Gameplay scene for all games; game-specific content is loaded via Addressables by `gameId`.

**Server-authoritative online.**
Clients send commands (`IGameCommand`); the authority (host) validates and applies them.
The same rules code runs on both client and host — no duplication.
Current MVP uses Client-Host; switching to a Dedicated Server requires no changes to game logic.

**`IGameService` with two implementations.**
`LocalGameService` and `NetworkGameService` share one interface.
The UI and ViewModels don't know which one is active.

### Project Structure

```
Assets/Scripts/Runtime/
├── Gameplay/           # Generic contracts: IGameService, IGameCommand, CellId, FieldRenderSpec
├── Games/
│   ├── TicTacToe/      # Rules, AI, Ultimate rules, move validation, ECS systems
│   └── Battleship/     # Placement logic, combat, networking bridge, AI
├── GameModes/
│   └── Wizard/         # Game creation wizard: catalog, strategies, settings ViewModels
├── Infrastructure/     # DI scopes, FSM, logging, save system, entry point
├── UI/                 # Views, ViewModels, Binders, UIService
├── Localization/       # CSV → JSON pipeline, locale switching
└── Services/           # Player profile, statistics, session services
```

---

## Running Locally

1. Install **Unity 6** (6000.3.x)
2. Clone the repository
3. Open `src/ultimate-tic-tac-toe` in Unity Hub
4. Open scene `Assets/Scenes/EntryPoint.unity` and press Play

To build WebGL: use `build.ps1` (Windows) or `build.sh` (Linux/macOS).

---

---

<a name="ru"></a>

# ultimate-tic-tac-toe

[EN](#) | **RU**

Коллекция классических настольных игр в браузере, собранная на Unity WebGL.
Онлайн-мультиплеер, боты с ИИ и расширяемая архитектура, рассчитанная на несколько игр.

> **[▶ Играть в браузере](https://artover1317.github.io/ultimate-tic-tac-toe/)**

---

## Игры

| Игра | Статус | Особенности |
|---|---|---|
| **Classic Tic-Tac-Toe** | ✅ Готово | Настраиваемое NxN поле, локально и онлайн, бот, таймер хода |
| **Ultimate Tic-Tac-Toe** | ✅ Готово | 9 мини-досок (81 клетка), ограничение на куда ходить, счёт серии |
| **Морской бой** | 🚧 В разработке | Расстановка кораблей + бой, таймеры, бот, онлайн |

Планируются шашки и другие классические настолки — архитектура это поддерживает.

---

## Технологии

| Область | Технология |
|---|---|
| Движок | Unity 6 (WebGL) |
| Render Pipeline | URP |
| Игровая логика | [Morpeh ECS](https://github.com/scellecs/morpeh) |
| UI | Unity UI Toolkit (UXML / USS) |
| Реактивность / MVVM | [R3](https://github.com/Cysharp/R3) |
| Dependency Injection | [VContainer](https://github.com/hadashiA/VContainer) |
| Онлайн | [Photon Fusion 2](https://www.photonengine.com/fusion) + Photon Cloud |
| Async | [UniTask](https://github.com/Cysharp/UniTask) |
| Анимации/тwyn | [LitMotion](https://github.com/AnnulusGames/LitMotion) |
| Ассеты | Unity Addressables |
| Локализация | Unity Localization (EN / RU / JA) |

---

## Архитектура

```
View  (UI Toolkit / MonoBehaviour)
  ↕  R3 reactive bindings  (MVVM)
ViewModel
  ↕  Service Layer  (IGameService)
Game Logic  (Morpeh ECS — правила, FSM, ИИ, ходы)
```

### Ключевые решения

**ECS для игровой логики.**
Правила, победные условия, ИИ и таймеры ходов реализованы через Morpeh Systems и Components.
MonoBehaviour используется только для View и Unity-специфики (камера, аудио, ввод).

**MVVM + R3.**
ViewModel подписывается на события из ECS через реактивные потоки.
View ничего не знает про ECS и правила игры — только что отображать.

**Game Catalog + Strategy pattern.**
Добавить новую игру = реализовать `IGameStrategy` + `IGameConfig` и зарегистрировать в каталоге.
Сцена Gameplay одна для всех игр; игровой контент подгружается через Addressables по `gameId`.

**Server-authoritative онлайн.**
Клиент отправляет команды (`IGameCommand`); authority (хост) валидирует и применяет.
Один и тот же код правил работает на клиенте и хосте — без дублирования.
MVP: Client-Host; переход на Dedicated Server не требует правок игровой логики.

**`IGameService` с двумя реализациями.**
`LocalGameService` и `NetworkGameService` — за одним интерфейсом.
UI и ViewModel не знают, какая из них активна.

### Структура проекта

```
Assets/Scripts/Runtime/
├── Gameplay/           # Общие контракты: IGameService, IGameCommand, CellId, FieldRenderSpec
├── Games/
│   ├── TicTacToe/      # Правила, ИИ, Ultimate-правила, ECS-системы
│   └── Battleship/     # Расстановка, бой, сетевой мост, ИИ
├── GameModes/
│   └── Wizard/         # Визард создания матча: каталог, стратегии, ViewModel настроек
├── Infrastructure/     # DI scopes, FSM, логирование, сохранения, EntryPoint
├── UI/                 # View, ViewModel, Binder, UIService
├── Localization/       # CSV → JSON пайплайн, переключение языка
└── Services/           # Профиль игрока, статистика, сессия
```

---

## Локальный запуск

1. Установить **Unity 6** (6000.3.x)
2. Клонировать репозиторий
3. Открыть `src/ultimate-tic-tac-toe` в Unity Hub
4. Открыть сцену `Assets/Scenes/EntryPoint.unity` и нажать Play

Для сборки WebGL: `build.ps1` (Windows) или `build.sh` (Linux/macOS).
