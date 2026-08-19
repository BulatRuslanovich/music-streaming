// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// Метаданные запущенной сборки. Отдельно от <see cref="ConfigController"/>: тот описывает
/// настройки, влияющие на поведение клиента, а здесь — сведения, нужные только разработчику.
/// </summary>
[ApiController]
[Route("api/system")]
public class SystemController : ControllerBase
{
    [HttpGet]
    public ActionResult<SystemInfoDto> Get() => Ok(BuildInfo.Current);
}

public record SystemInfoDto(string Version, string? Commit, DateTimeOffset? BuiltAt);
