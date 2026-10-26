// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Globalization;

namespace App.Common;

// Тексты ошибок для клиента. Ключ — английский шаблон из исключения (он же остаётся в логах),
// значение — перевод; {0}, {1} — аргументы исключения. Язык — CurrentUICulture запроса
// (Accept-Language, см. LocalizationSetup). Нет перевода — уходит английский.
public static class ErrorText
{
    public static string Format(string template, IReadOnlyList<object?> args) =>
        args.Count == 0 ? template : string.Format(CultureInfo.InvariantCulture, template, [.. args]);

    public static string Localize(string template, IReadOnlyList<object?>? args = null) =>
        Format(Translate(template, CultureInfo.CurrentUICulture), args ?? []);

    public static string Translate(string template, CultureInfo language) =>
        language.TwoLetterISOLanguageName == "ru" && Russian.TryGetValue(template, out var russian) ? russian : template;

    public static IReadOnlyDictionary<string, string> Russian { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Общее
        ["The requested resource was not found."] = "Не найдено.",
        ["Invalid credentials."] = "Нужно войти заново.",
        ["Access denied."] = "Нет доступа.",
        ["An unexpected error occurred."] = "Что-то пошло не так.",
        ["One or more validation errors occurred."] = "Запрос заполнен неверно.",

        // Вход и учётные записи
        ["Invalid username or password."] = "Неверное имя пользователя или пароль.",
        ["This account has been deactivated."] = "Учётная запись отключена.",
        ["The current password is not correct."] = "Текущий пароль неверный.",
        ["Missing refresh token."] = "Нужно войти заново.",
        ["Refresh token is invalid or expired."] = "Нужно войти заново.",
        ["User not found."] = "Пользователь не найден.",
        ["A user with that username already exists."] = "Пользователь с таким именем уже есть.",

        // Каталог
        ["Track not found."] = "Трек не найден.",
        ["Album not found."] = "Альбом не найден.",
        ["Artist not found."] = "Исполнитель не найден.",
        ["Genre not found."] = "Жанр не найден.",
        ["Playlist not found."] = "Плейлист не найден.",
        ["The track is not in this playlist."] = "Этого трека нет в плейлисте.",
        ["At least one track id is required."] = "Не выбрано ни одного трека.",
        ["No tracks were selected."] = "Не выбрано ни одного трека.",
        ["At most {0} tracks can be looked up at once."] = "За раз можно запросить не больше {0} треков.",
        ["An artist named \"{0}\" already exists."] = "Исполнитель «{0}» уже есть.",
        ["\"{0}\" already exists for that artist."] = "У этого исполнителя уже есть «{0}».",
        ["The waveform is not ready yet."] = "Волна трека ещё не готова.",

        // Обложки и картинки
        ["This album has no cover art"] = "У альбома нет обложки.",
        ["This track has no cover art."] = "У трека нет обложки.",
        ["This playlist has no cover art."] = "У плейлиста нет обложки.",
        ["This artist has no photo."] = "У исполнителя нет фото.",
        ["The image file is missing from storage."] = "Файл картинки пропал из хранилища.",
        ["No image was provided."] = "Картинка не выбрана.",
        ["Only JPEG, PNG and WebP images are accepted."] = "Подходят только JPEG, PNG и WebP.",
        ["Only .jpg, .png and .webp files are accepted."] = "Подходят только файлы .jpg, .png и .webp.",
        ["That file could not be read as an image."] = "Этот файл не читается как картинка.",
        ["That image has too many pixels to process."] = "Картинка слишком большая.",

        // Загрузка
        ["The file is empty."] = "Файл пустой.",
        ["The file contains no audio stream."] = "В файле нет звука.",
        ["The file exceeds the {0} MB limit."] = "Файл больше {0} МБ.",
        ["The file exceeds the {0} MB upload limit."] = "Файл больше {0} МБ.",
        ["Only {0} files are supported."] = "Подходят только файлы {0}.",
        ["The file is not a readable {0} file."] = "Файл {0} не читается.",
        ["The file is not a {0} file despite its name."] = "Это не {0}, хотя так назван.",
        ["The uploaded file could not be read back."] = "Загруженный файл не читается.",
        ["The file could not be processed."] = "Файл не удалось обработать.",
        ["This file is already in the library."] = "Этот файл уже есть в библиотеке.",
        ["A lossless version of this track is already in the library."] = "Этот трек уже есть в библиотеке в lossless.",

        // Воспроизведение
        ["The audio file for this track is missing from storage."] = "Аудиофайл трека пропал из хранилища.",
        ["HLS asset not found."] = "Поток не найден.",
        ["Original is not an HLS quality cap."] = "Исходное качество не ограничивает поток.",
        ["A deviceId is required."] = "Не указано устройство.",
        ["The index must point into the queue."] = "Такого места в очереди нет.",
        ["Repeat must be off, all or one."] = "Неизвестный режим повтора.",
        ["Another device holds playback."] = "Музыка сейчас играет на другом устройстве.",
        ["Nothing is playing on another device."] = "На других устройствах ничего не играет.",
        ["The tracks that were playing are gone."] = "Треков, которые играли, больше нет.",
        ["A play is only recorded after {0} seconds of listening."] = "Прослушивание засчитывается после {0} с.",

        // Рекомендации и настройки
        ["Unknown mood '{0}'."] = "Неизвестное настроение «{0}».",
        ["Radio limit must be between 1 and {0}."] = "Радио берёт от 1 до {0} треков.",
        ["Days must be between 1 and {0}."] = "Период — от 1 до {0} дней.",
        ["Unknown audio quality."] = "Неизвестное качество звука.",
        ["The time zone name is not valid."] = "Неверный часовой пояс.",
        ["Unknown time zone '{0}'."] = "Неизвестный часовой пояс «{0}».",

        // Дубликаты
        ["Select at least two tracks."] = "Выберите хотя бы два трека.",
        ["Select the tracks to remove."] = "Выберите треки, которые убрать.",

        // Импорт из Яндекс Музыки
        ["Add a Yandex Music token in the settings first."] = "Сначала добавьте токен Яндекс Музыки в настройках.",
        ["The Yandex Music token is too long."] = "Токен Яндекс Музыки слишком длинный.",
        ["Yandex Music rejected the token."] = "Яндекс Музыка не приняла токен.",
        ["Yandex Music did not answer ({0})."] = "Яндекс Музыка не ответила ({0}).",
        ["Unknown import source."] = "Непонятно, что импортировать.",
        ["This import is already running."] = "Этот импорт уже идёт.",
        ["Import not found."] = "Импорт не найден.",
        ["Nothing to import here."] = "Здесь нечего импортировать.",
        ["This track is not available on Yandex Music."] = "Трек недоступен в Яндекс Музыке.",
        ["Yandex Music did not give a download link for this track."] = "Яндекс Музыка не дала ссылку на этот трек.",
        ["Yandex Music offered only {0}, which Caimack does not store."] = "Яндекс Музыка отдаёт только {0}, а Caimack такое не хранит.",
        ["The FLAC stream could not be unpacked."] = "Не удалось распаковать FLAC.",
        ["The import failed."] = "Импорт не удался.",
    };
}
