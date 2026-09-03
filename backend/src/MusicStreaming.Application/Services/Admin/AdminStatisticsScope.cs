// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;

namespace MusicStreaming.Application.Services.Admin;

/// <summary>
/// Окно, в котором работает административная статистика: часовой пояс смотрящего администратора и
/// вычисленная в нём граница периода. Один экземпляр на запрос — <see cref="UserSettingsService"/>
/// кеширует настройки у себя, так что несколько обращений стоят одного запроса.
/// </summary>
/// <remarks>
/// Пояс берётся из настроек администратора, а не из query-параметра — ровно так же, как это делает
/// персональная статистика. Иначе одни и те же сутки называются по-разному в двух разделах.
/// </remarks>
public class AdminStatisticsScope(IApplicationDbContext db, UserSettingsService settings)
{
    public async Task<AdminPeriodWindow> ResolveAsync(StatisticsPeriod period, CancellationToken ct)
    {
        var timeZone = (await settings.GetAsync(ct)).TimeZone;
        var from = await StatisticsPeriods.StartAsync(db, period, timeZone, ct);

        return new AdminPeriodWindow(period, from, timeZone);
    }
}

/// <param name="From">null означает «за всё время»: нижней границы нет.</param>
public record AdminPeriodWindow(StatisticsPeriod Period, DateTimeOffset? From, string TimeZone);
