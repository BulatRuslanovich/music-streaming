// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using App.Dtos;
using App.Services;

namespace Api.Controllers;

[ApiController]
[Route("api/config")]
public class ConfigController(ClientConfigService config) : ControllerBase
{
    [HttpGet]
    public ActionResult<ClientConfigDto> Get() => Ok(config.Get());
}
