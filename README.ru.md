# DotsObserver

[English version](README.md)

## Содержание

- [От автора](#от-автора)
- [Что это такое](#что-это-такое)
- [Установка и требования](#установка-и-требования)
  - [Scripting Define Symbols](#scripting-define-symbols)
  - [Ссылки](#ссылки)
- [Быстрый старт](#быстрый-старт)
- [Архитектура](#архитектура)
  - [Обзор пакетов](#обзор-пакетов)
  - [Жизненный цикл EntityObserver](#жизненный-цикл-entityobserver)
  - [Поток данных](#поток-данных)
- [API](#api)
  - [EntityObserver&lt;T&gt;](#entityobservert)
  - [BufferObserver&lt;T&gt;](#bufferobservert)
  - [EntityScope и BufferScope](#entityscope-и-bufferscope)
  - [EntityScopeBuilder и EntityScopeGroup](#entityscopebuilder-и-entityscopegroup)
  - [Конфигурация ObserverConfig](#конфигурация-observerconfig)
  - [Автоматический выбор планировщика](#автоматический-выбор-планировщика)
  - [API Cheatsheet](#api-cheatsheet)
- [Лицензия](#лицензия)

---

## От автора

Библиотека **DotsObserver** и данная документация полностью сгенерированы с помощью ИИ.  
Проект прошёл комплексное тестирование: суммарно с пакетом **DotsObserver.MVVM** реализовано и успешно пройдено **199 unit-тестов** (NUnit + Unity Test Framework), покрывающий ядро, MVVM-слой и интеграционные сценарии.

---

## Что это такое

**DotsObserver** — высокопроизводительная библиотека для Unity DOTS/ECS, предоставляющая реактивный слой наблюдения за изменениями компонентов (`IComponentData`) и динамических буферов (`IBufferElementData`).

Основные возможности:
- **События жизненного цикла**: `Created`, `Changed`, `Destroyed`, `Enabled`, `Disabled`.
- **Burst-оптимизированные Job'ы**: `IJobChunk` с нулевой аллокацией в горячем цикле.
- **Несколько режимов детекции изменений**: `ChangeFilterOnly`, `EqualsCheck` (MemCmp), `Both`.
- **Автоматическая оптимизация `IEquatable<T>`**: если компонент реализует `IEquatable<T>`, планировщик автоматически переключается на `T.Equals()` вместо MemCmp.
- **Поддержка `IEnableableComponent`**: отслеживание включения/выключения компонентов.
- **Два режима выполнения**: `BurstJob` (по умолчанию) и `MainThread` (синхронный, без job overhead).
- **Поддержка кастомного `EntityQuery`**: передать собственный фильтрованный запрос при создании наблюдателя.
- **Wildcard-режим**: наблюдение за всеми entity с заданным компонентом сразу.
- **Zero-allocation API**: `NativeQueue`, `NativeParallelHashMap`, `NativeArray` — никаких managed-аллокаций во время обновления.
- **Managed обёртки**: `EntityScope<T>` и `BufferScope<T>` с привычными C#-событиями для UI-слоя.
- **Enable/Disable scope'ов**: приостановка наблюдения без уничтожения объекта.
- **Fluent builder**: `EntityScopeBuilder` для батчевой регистрации наблюдателей.

---

## Установка и требования

- **Unity**: 2022.3 LTS или новее.
- **Entities**: 1.0.x (DOTS / Unity ECS).
- **Burst**, **Collections**, **Jobs**: стандартные пакеты DOTS.

### Scripting Define Symbols

Добавьте в **Edit → Project Settings → Player → Scripting Define Symbols** при необходимости:

| Символ | Описание |
|--------|----------|
| `DOTS_OBSERVER_USE_FNV1A` | Принудительно использует 32-битный FNV-1a для хэширования буферов вместо xxHash3 (по умолчанию). |

### Ссылки

- **[DotsObserver.MVVM](https://github.com/About00/dots-observer.mvvm)** — MVVM-обёртка с `DotsViewModel`, `ComponentProperty<T>` и двусторонним биндингом для интеграции с UI.
- **[DotsObserver.Tests](https://github.com/About00/dots-observer.tests)** — набор unit-тестов для библиотек **DotsObserver** и **DotsObserver.MVVM**.

---

## Быстрый старт

```csharp
using DotsObserver;
using Unity.Entities;
using Unity.Collections;

public partial class HealthSystem : SystemBase
{
    private EntityObserver<Health> _observer;

    protected override void OnCreate()
    {
        var config = ObserverConfig.Default.With(
            trackEntityLifecycle: true,
            changeDetection: ChangeDetectionMode.Both);

        _observer = new EntityObserver<Health>();
        _observer.OnCreate(this, config);
    }

    protected override void OnUpdate()
    {
        _observer.Update(this);
        Dependency.Complete();

        var events = _observer.FlushEvents(Allocator.Temp);
        try
        {
            for (int i = 0; i < events.Length; i++)
            {
                var e = events[i];
                switch (e.Type)
                {
                    case ChangeEventType.Created:
                        UnityEngine.Debug.Log($"[Created] {e.Entity}: {e.NewValue}");
                        break;
                    case ChangeEventType.Changed:
                        UnityEngine.Debug.Log($"[Changed] {e.Entity}: {e.PreviousValue} -> {e.NewValue}");
                        break;
                    case ChangeEventType.Destroyed:
                        UnityEngine.Debug.Log($"[Destroyed] {e.Entity}");
                        break;
                }
            }
        }
        finally
        {
            events.Dispose();
        }
    }

    protected override void OnDestroy()
    {
        _observer.OnDestroy(this);
    }
}

public struct Health : IComponentData
{
    public int Value;
}
```

| Сценарий использования                                                                    | Complete за кадр        | Примечание                                                                                                                                                                                           |
| ----------------------------------------------------------------------------------------- | ----------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **MVVM через `ObserverPresentationSystem`** (рекомендуемый)                               | **1** (централизованно) | Все batch-джобы объединяются в один `JobHandle` и завершаются единым `Complete()` в `OnUpdate`. Fallback-скопы делают дополнительные вызовы, но на уже завершённую `Dependency` (практически no-op). |
| **`EntityScope<T>.UpdateAndFlush()`** или **`BufferScope<T>.UpdateAndFlush()`** (ISystem) | **2 × N**               | `Update()` делает `Complete()` перед очисткой очереди, `Flush()` делает второй `Complete()` перед чтением событий. N — количество scope'ов.                                                          |
| **`EntityObserver<T>.Update()`** / **`BufferObserver<T>.Update()`** (низкоуровневый)      | **1 × N**               | Один `Complete()` на каждый observer в начале `Update()`.                                                                                                                                            |
| **`EntityObserver<T>.UpdateAndFlush()`**                                                  | **2 × N**               | Как и у scope'ов: один в `Update()`, второй перед `FlushEvents()`.  

---

## Архитектура

### Обзор пакетов

```mermaid
flowchart TB
    subgraph Core["DotsObserver (Core)"]
        direction TB
        EO[EntityObserver<T>]
        BO[BufferObserver<T>]
        ES[EntityScope<T>]
        BS[BufferScope<T>]
        BU[EntityScopeBuilder]
        EG[EntityScopeGroup]
    end

    subgraph MVVM["DotsObserver.MVVM"]
        direction TB
        VM[DotsViewModel]
        PS[ObserverPresentationSystem]
        PES[PresentationEntityScope<T>]
        PBS[PresentationBufferScope<T>]
    end

    subgraph UI["UI / Presentation"]
        V[View / MonoBehaviour]
    end

    EO -->|NativeQueue| ES
    BO -->|NativeQueue| BS
    ES -->|C# events| VM
    BS -->|C# events| VM
    PS -->|UpdateAndFlush| PES
    PS -->|UpdateAndFlush| PBS
    VM -->|INotifyPropertyChanged| V
```

### Жизненный цикл EntityObserver

```mermaid
stateDiagram-v2
    [*] --> Idle : new EntityObserver<T>()
    Idle --> Updating : OnCreate()
    Updating --> Flushing : FlushEvents()
    Flushing --> Updating : Update()
    Updating --> Destroyed : OnDestroy()
    Destroyed --> [*] : Dispose()

    note right of Updating
        BurstJob: IJobChunk + CleanupJob
        MainThread: синхронный EntityManager
        TrackEnableable только в BurstJob
    end note
```

### Поток данных

```mermaid
flowchart LR
    A[ECS World<br/>ArchetypeChunk] -->|IJobChunk| B(EntityObserver<T>)
    B -->|NativeQueue<br/>ChangeEvent<T>| C{FlushEvents}
    C -->|Managed dispatch| D[EntityScope<T>]
    D -->|ComponentCreatedHandler<br/>ComponentChangedHandler| E[UI / Logic / MVVM]

    style B fill:#517d6e,stroke:#000,stroke-width:2px
    style C fill:#80392e,stroke:#333,stroke-width:2px,color:#fff
```

---

## API

### EntityObserver&lt;T&gt;

Ядро системы. Не содержит managed-ссылок, безопасен для Burst.

| Метод | Описание |
|-------|----------|
| `OnCreate(ref SystemState, config, watchedEntity)` | Инициализация в `ISystem`. |
| `OnCreate(ref SystemState, config, customQuery, watchedEntity)` | Инициализация с кастомным `EntityQuery`. |
| `OnCreate(SystemBase, config, watchedEntity)` | Инициализация в `SystemBase`. |
| `OnCreate(SystemBase, config, customQuery, watchedEntity)` | Инициализация в `SystemBase` с кастомным `EntityQuery`. |
| `Update(ref SystemState)` / `Update(SystemBase)` | Выполняет Job'ы детекции изменений (или синхронное обновление в `MainThread` режиме). |
| `FlushEvents(Allocator)` | Возвращает `NativeArray<ChangeEvent<T>>` и очищает очередь. |
| `UpdateAndFlush(..., Allocator)` | `Update` + `FlushEvents` в одном вызове. |
| `GetEvents(Allocator)` | Возвращает копию событий **без** очистки очереди. |
| `TryDequeue(out ChangeEvent<T>)` | Извлекает одно событие (без лимитов). |
| `FlushToManagedEvents(Action<ChangeEvent<T>>)` | Синхронный flush с диспетчеризацией в делегат. |
| `GetMetrics()` | Возвращает `ObserverMetrics` (processed, dropped, pressure). |
| `ClearEvents()` | Очищает очередь без возврата данных. |
| `OnDestroy(...)` / `Dispose()` | Освобождение нативных коллекций. |

> **MainThread режим**: при `ExecutionMode = ObserverExecutionMode.MainThread` обновление выполняется синхронно через `EntityManager` без шедулинга Job'а. `TrackEnableable` в этом режиме не поддерживается и будет проигнорирован.

### BufferObserver&lt;T&gt;

Аналог `EntityObserver`, но для `IBufferElementData`. Использует хэширование содержимого (xxHash3 по умолчанию, FNV-1a при `DOTS_OBSERVER_USE_FNV1A`) для детекции изменений.

| Метод | Описание |
|-------|----------|
| `OnCreate(...)` / `Update(...)` / `FlushEvents(...)` | Аналогично `EntityObserver`. |
| `FlushToManagedEvents(Action<BufferChangeEvent<T>>)` | Диспетчеризация в managed-делегат. |

### EntityScope и BufferScope

Managed обёртки с C#-событиями для main-thread кода (UI, ViewModel). Поддерживают приостановку через `Enable()` / `Disable()`.

```csharp
// Наблюдение за конкретной entity
var scope = EntityScope<Health>.Create(ref state, entity, config);

// Wildcard: наблюдение за всеми entity с компонентом
var scope = EntityScope<Health>.CreateWildcard(ref state, config);

// С кастомным EntityQuery
var scope = EntityScope<Health>.Create(ref state, customQuery, config, entity);

scope.OnCreated  += (in Entity e, in Health v) => { };
scope.OnChanged  += (in Entity e, in Health p, in Health c) => { };
scope.OnDestroyed += (in Entity e, in Health l) => { };
scope.OnEnabled  += (in Entity e, in Health v) => { };
scope.OnDisabled += (in Entity e, in Health l) => { };

scope.Enable();          // возобновить наблюдение
scope.Disable();         // приостановить без уничтожения

scope.UpdateAndFlush(ref state);
scope.Dispose(ref state);
```

### EntityScopeBuilder и EntityScopeGroup

```csharp
// Fluent builder
var builder = new EntityScopeBuilder(config.With(trackEntityLifecycle: true));

// Наблюдение за конкретной entity
builder.Watch<Health>(ref state, playerEntity);
builder.Watch<Mana>(ref state, playerEntity);

// Wildcard: все entity с компонентом
builder.WatchAll<Health>(ref state);
builder.WatchAll<Health>(ref state, customQuery);  // с фильтром

// Буферы
builder.WatchBuffer<InventoryItem>(ref state, playerEntity);
builder.WatchAllBuffers<InventoryItem>(ref state);

// С кастомным EntityQuery
builder.Watch<Health>(ref state, playerEntity, customQuery);

var group = builder.Build();

// Bulk-операции над группой
group.UpdateAll(ref state);
group.FlushAll(ref state);
group.UpdateAndFlushAll(ref state);
group.EnableAll();
group.DisableAll();
group.DisposeAll(ref state);
```

### Конфигурация ObserverConfig

```csharp
public struct ObserverConfig
{
    public int UpdateInterval;        // 1 = каждый кадр
    public ScheduleMode Mode;         // Parallel (по умолчанию)
    public ChangeDetectionMode ChangeDetection; // Both (по умолчанию)
    public ObserverExecutionMode ExecutionMode; // BurstJob или MainThread
    public int MaxEventsPerFrame;     // 1000 по умолчанию (0 = без лимита)
    public bool TrackEntityLifecycle; // Created / Destroyed
    public bool TrackEnableable;      // Enabled / Disabled (только BurstJob)
    public int RingQueueCapacity;     // 0 = NativeQueue, >0 = кольцевая
    public int MaxQueueSize;          // Жёсткий лимит на выдачу
}
```

**Значения по умолчанию** (`ObserverConfig.Default`):

| Поле | Значение |
|------|----------|
| `UpdateInterval` | 1 |
| `Mode` | `Parallel` |
| `ChangeDetection` | `Both` |
| `ExecutionMode` | `BurstJob` |
| `MaxEventsPerFrame` | **1000** |
| `TrackEntityLifecycle` | `false` |
| `TrackEnableable` | `false` |
| `RingQueueCapacity` | 0 |
| `MaxQueueSize` | 0 |

Создание через `With(...)`:
```csharp
var config = ObserverConfig.Default.With(
    trackEntityLifecycle: true,
    changeDetection: ChangeDetectionMode.EqualsCheck,
    executionMode: ObserverExecutionMode.MainThread,
    maxEventsPerFrame: 500);
```

### Автоматический выбор планировщика

`EntityObserver<T>` автоматически выбирает оптимальный планировщик в зависимости от типа компонента:

| Условие | Планировщик | Поведение |
|---------|-------------|-----------|
| `T : IEnableableComponent` + `trackEnableable: true` | `EnableableUpdateScheduler<T>` | Отслеживает `Enabled`/`Disabled` |
| `T : IEquatable<T>` | `EquatableUpdateScheduler<T>` | Использует `T.Equals()` вместо MemCmp |
| Всё остальное | `RegularUpdateScheduler<T>` | MemCmp для сравнения |

Выбор происходит единожды при `OnCreate` через рефлексию; runtime-стоимость равна нулю.

### API Cheatsheet

```csharp
// === EntityObserver (Core) ===
var observer = new EntityObserver<Health>();
observer.OnCreate(ref systemState, ObserverConfig.Default);
observer.Update(ref systemState);
var events = observer.FlushEvents(Allocator.Temp);
events.Dispose();

// === EntityObserver c кастомным запросом ===
observer.OnCreate(ref systemState, config, myCustomQuery);

// === EntityScope (Managed) ===
var scope = EntityScope<Health>.Create(ref state, entity, config);
scope.OnChanged += (e, p, c) => { };
scope.Enable();
scope.Disable();
scope.UpdateAndFlush(ref state);
scope.Dispose(ref state);

// === EntityScope wildcard ===
var wildScope = EntityScope<Health>.CreateWildcard(ref state, config);

// === BufferScope ===
var bScope = BufferScope<InventoryItem>.Create(ref state, entity, config);
bScope.OnBufferChanged += (e) => { };

// === BufferScope wildcard ===
var bWild = BufferScope<InventoryItem>.CreateWildcard(ref state, config);

// === Builder + Group ===
var builder = new EntityScopeBuilder(ObserverConfig.Default.With(trackEntityLifecycle: true));
builder.Watch<Health>(ref state, entity);
builder.WatchAll<Mana>(ref state);
builder.WatchBuffer<InventoryItem>(ref state, entity);
builder.WatchAllBuffers<InventoryItem>(ref state);
var group = builder.Build();
group.UpdateAndFlushAll(ref state);
group.EnableAll();
group.DisableAll();
group.DisposeAll(ref state);

// === Config ===
var cfg = ObserverConfig.Default.With(
    updateInterval: 2,
    changeDetection: ChangeDetectionMode.Both,
    maxEventsPerFrame: 500,
    trackEnableable: true,
    executionMode: ObserverExecutionMode.MainThread);
```

---

## Лицензия

MIT License. Подробности см. в файле `LICENSE`.