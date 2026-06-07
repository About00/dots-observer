namespace DotsObserver
{
    public struct ObserverConfig
    {
        // === Default Values ===
        
        public const int DefaultUpdateInterval = 1;

        public const ScheduleMode DefaultMode = ScheduleMode.Parallel;

        public const ChangeDetectionMode DefaultChangeDetection = ChangeDetectionMode.Both;
        
        public const ObserverExecutionMode DefaultExecutionMode = ObserverExecutionMode.BurstJob;

        public const int DefaultMaxEventsPerFrame = 1000;

        public const bool DefaultTrackEntityLifecycle = false;

        public const bool DefaultTrackEnableable = false;

        public const int DefaultRingQueueCapacity = 0;

        public const int DefaultMaxQueueSize = 0;
        
        // === Config Core ===
        
        /// <summary>
        /// Кадры между обновлениями (1 = каждый кадр).
        /// </summary>
        public int UpdateInterval;

        /// <summary>
        /// Parallel или Sequential внутри группы (если используется).
        /// </summary>
        public ScheduleMode Mode;

        /// <summary>
        /// Способ детекции изменений.
        /// </summary>
        public ChangeDetectionMode ChangeDetection;
        
        /// <summary>
        /// Режим выполнения: BurstJob (по умолчанию) или MainThread (синхронный, без job overhead).
        /// </summary>
        public ObserverExecutionMode ExecutionMode;

        /// <summary>
        /// Максимум событий за кадр.
        /// </summary>
        public int MaxEventsPerFrame;

        /// <summary>
        /// Отслеживать появление/исчезновение entity с компонентом.
        /// </summary>
        public bool TrackEntityLifecycle;

        /// <summary>
        /// Отслеживать включение/выключение IEnableableComponent.
        /// </summary>
        public bool TrackEnableable;

        /// <summary>
        /// 0 = NativeQueue (неограниченная), >0 = NativeRingQueue фикс. ёмкости.
        /// </summary>
        public int RingQueueCapacity;

        /// <summary>
        /// Макс. размер очереди (для NativeQueue, 0 = без лимита).
        /// </summary>
        public int MaxQueueSize;
        
        /// <summary>
        /// Конфигурация по умолчанию.
        /// </summary>
        public static ObserverConfig Default => new ObserverConfig
        {
            UpdateInterval = DefaultUpdateInterval,
            Mode = DefaultMode,
            ChangeDetection = DefaultChangeDetection,
            ExecutionMode = DefaultExecutionMode,
            MaxEventsPerFrame = DefaultMaxEventsPerFrame,
            TrackEntityLifecycle = DefaultTrackEntityLifecycle,
            TrackEnableable = DefaultTrackEnableable,
            RingQueueCapacity = DefaultRingQueueCapacity,
            MaxQueueSize = DefaultMaxQueueSize
        };
        
        public ObserverConfig(
            int updateInterval = DefaultUpdateInterval,
            ScheduleMode mode = DefaultMode,
            ChangeDetectionMode changeDetection = DefaultChangeDetection,
            ObserverExecutionMode executionMode = DefaultExecutionMode,
            int maxEventsPerFrame = DefaultMaxEventsPerFrame,
            bool trackEntityLifecycle = DefaultTrackEntityLifecycle,
            bool trackEnableable = DefaultTrackEnableable,
            int ringQueueCapacity = DefaultRingQueueCapacity,
            int maxQueueSize = DefaultMaxQueueSize)
        {
            UpdateInterval = updateInterval;
            Mode = mode;
            ChangeDetection = changeDetection;
            ExecutionMode = executionMode;
            MaxEventsPerFrame = maxEventsPerFrame;
            TrackEntityLifecycle = trackEntityLifecycle;
            TrackEnableable = trackEnableable;
            RingQueueCapacity = ringQueueCapacity;
            MaxQueueSize = maxQueueSize;
        }
    }

    public static class ObserverConfigExtensions
    {
        public static ObserverConfig With(
            this ObserverConfig config,
            int? updateInterval = null,
            ScheduleMode? mode = null,
            ChangeDetectionMode? changeDetection = null,
            ObserverExecutionMode? executionMode = null,
            int? maxEventsPerFrame = null,
            bool? trackEntityLifecycle = null,
            bool? trackEnableable = null,
            int? ringQueueCapacity = null,
            int? maxQueueSize = null)
        {
            return new ObserverConfig(
                updateInterval: updateInterval ?? config.UpdateInterval,
                mode: mode ?? config.Mode,
                changeDetection: changeDetection ?? config.ChangeDetection,
                executionMode: executionMode ?? config.ExecutionMode,
                maxEventsPerFrame: maxEventsPerFrame ?? config.MaxEventsPerFrame,
                trackEntityLifecycle: trackEntityLifecycle ?? config.TrackEntityLifecycle,
                trackEnableable: trackEnableable ?? config.TrackEnableable,
                ringQueueCapacity: ringQueueCapacity ?? config.RingQueueCapacity,
                maxQueueSize: maxQueueSize ?? config.MaxQueueSize
            );
        }
    }
}