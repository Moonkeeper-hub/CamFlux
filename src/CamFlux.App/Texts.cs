namespace CamFlux.App;

internal static class Texts
{
    public static readonly IReadOnlyDictionary<string, (string En, string Ru)> Values =
        new Dictionary<string, (string, string)>
        {
            ["Profiles"] = ("Profiles", "Профили"),
            ["ProfileName"] = ("Profile name", "Название профиля"),
            ["Save"] = ("Save", "Сохранить"),
            ["Load"] = ("Load", "Загрузить"),
            ["Language"] = ("Language", "Язык"),
            ["Server"] = ("SRT server", "Сервер SRT"),
            ["Host"] = ("Host", "Хост"),
            ["Port"] = ("UDP port", "Порт UDP"),
            ["Latency"] = ("Latency (ms)", "Задержка (мс)"),
            ["PublishUser"] = ("Publisher user", "Пользователь публикации"),
            ["PublishPassword"] = ("Publisher password", "Пароль публикации"),
            ["ReaderUser"] = ("OBS reader user", "Пользователь чтения OBS"),
            ["ReaderPassword"] = ("OBS reader password", "Пароль чтения OBS"),
            ["Cameras"] = ("Cameras", "Камеры"),
            ["Discover"] = ("Scan devices", "Найти устройства"),
            ["Browse"] = ("Select FFmpeg…", "Выбрать FFmpeg…"),
            ["StartAll"] = ("Start enabled", "Запустить выбранные"),
            ["StopAll"] = ("Stop all", "Остановить все"),
            ["Start"] = ("Start", "Пуск"),
            ["Stop"] = ("Stop", "Стоп"),
            ["Copy"] = ("Copy OBS URL", "Копировать URL OBS"),
            ["Enabled"] = ("Enabled", "Вкл."),
            ["Path"] = ("Stream path", "Путь потока"),
            ["Mode"] = ("Capture mode", "Режим захвата"),
            ["Probe"] = ("Find modes", "Найти режимы"),
            ["Bitrate"] = ("Video (kbps)", "Видео (кбит/с)"),
            ["Audio"] = ("Microphone", "Микрофон"),
            ["NoAudio"] = ("Video only", "Только видео"),
            ["Frames"] = ("Frames", "Кадры"),
            ["Bandwidth"] = ("Estimated upload · {0:F2} Mbps", "Оценка исходящего трафика · {0:F2} Мбит/с"),
            ["Logs"] = ("Diagnostics", "Диагностика"),
            ["Ready"] = ("Ready", "Готово"),
            ["Checking"] = ("Checking FFmpeg…", "Проверка FFmpeg…"),
            ["Downloading"] = ("Downloading verified FFmpeg build…", "Загрузка сборки FFmpeg с проверкой хеша…"),
            ["Probing"] = ("Reading capture modes…", "Чтение режимов захвата…"),
            ["NoModes"] = ("No capture modes found. Check that the camera is available.", "Режимы захвата не найдены. Проверьте доступность камеры."),
            ["NoCameras"] = ("No DirectShow cameras found.", "Камеры DirectShow не найдены."),
            ["InvalidFfmpeg"] = ("This executable failed the DirectShow, SRT or libx264 check.", "Исполняемый файл не прошёл проверку DirectShow, SRT или libx264."),
            ["SetupError"] = ("FFmpeg setup or device scan failed. Retry the scan or select an executable.", "Ошибка настройки FFmpeg или поиска устройств. Повторите поиск или выберите исполняемый файл."),
            ["HashMismatch"] = ("FFmpeg archive checksum mismatch. Installation cancelled.", "Контрольная сумма архива FFmpeg не совпала. Установка отменена."),
            ["InvalidEndpoint"] = ("Enter a valid host, UDP port (1–65535) and latency (1–60000 ms).", "Укажите хост, UDP-порт (1–65535) и задержку (1–60000 мс)."),
            ["InvalidPath"] = ("Use letters A–Z, digits, underscores and hyphens. Use / between path segments.", "Используйте латинские буквы, цифры, подчёркивания и дефисы. Разделяйте сегменты пути символом /."),
            ["InvalidCredentials"] = ("Fill in both credential fields or leave both empty. Use A–Z, digits and . _ ~ -", "Заполните оба поля учётных данных или оставьте оба пустыми. Допустимы латинские буквы, цифры и . _ ~ -"),
            ["InvalidCapture"] = ("Select a capture mode and enter a video bitrate of 100–100000 kbps.", "Выберите режим захвата и укажите видеобитрейт от 100 до 100000 кбит/с."),
            ["DuplicateStream"] = ("Each active camera needs a unique stream path.", "Каждой активной камере нужен уникальный путь потока."),
            ["DuplicateAudio"] = ("Select a microphone on only one active stream.", "Выберите микрофон только для одного активного потока."),
            ["Copied"] = ("OBS reader URL copied to clipboard.", "URL для чтения в OBS скопирован."),
            ["ClipboardError"] = ("Could not access the clipboard.", "Буфер обмена недоступен."),
            ["ProfileError"] = ("Could not save or load the profile.", "Не удалось сохранить или загрузить профиль."),
            ["Saved"] = ("Profile saved.", "Профиль сохранён."),
            ["Disconnected"] = ("Camera unavailable", "Камера недоступна"),
            ["Idle"] = ("Idle", "Ожидание"),
            ["Starting"] = ("Starting", "Запуск"),
            ["Running"] = ("Sending frames", "Отправка кадров"),
            ["Retrying"] = ("Retrying", "Повторная попытка"),
            ["Stopping"] = ("Stopping", "Остановка"),
            ["Failed"] = ("Failed", "Ошибка"),
            ["ProcessError"] = ("FFmpeg process error.", "Ошибка процесса FFmpeg."),
            ["Stalled"] = ("Frame progress timed out.", "Истекло время ожидания кадров."),
            ["Rejected"] = ("SRT connection rejected.", "Подключение SRT отклонено."),
            ["Timeout"] = ("FFmpeg reported a timeout.", "FFmpeg сообщил об истечении времени ожидания."),
            ["Connection"] = ("FFmpeg reported a connection error.", "FFmpeg сообщил об ошибке подключения."),
            ["Capture"] = ("DirectShow capture warning.", "Предупреждение захвата DirectShow."),
            ["Encoder"] = ("H.264 encoder warning.", "Предупреждение кодировщика H.264."),
            ["FfmpegWarning"] = ("FFmpeg warning.", "Предупреждение FFmpeg."),
            ["Exit"] = ("FFmpeg exit code", "Код завершения FFmpeg")
        };

    public static string Get(string key, string language)
    {
        const string exit = "FFmpeg exit: ";
        if (key.StartsWith(exit, StringComparison.Ordinal))
            return $"{Get("Exit", language)} · {key[exit.Length..]}";
        return Values.TryGetValue(key, out var text) ? language == "ru" ? text.Ru : text.En : Get("SetupError", language);
    }
}
