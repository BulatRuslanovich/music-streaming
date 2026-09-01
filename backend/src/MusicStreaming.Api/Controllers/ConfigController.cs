// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/config")]
public class ConfigController(ClientConfigService config) : ControllerBase
{
    [HttpGet]
    public ActionResult<ClientConfigDto> Get() => Ok(config.Get());
}
