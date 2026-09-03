// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicStreaming.Api.Startup;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

/// <summary>Загрузка файлов: предварительная проверка и приём тела запроса.</summary>
[ApiController]
[Route("api/tracks/upload")]
public class TrackUploadsController(
    TrackUploadService upload,
    UploadProbeService uploadProbe,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpPost("check")]
    [EnableRateLimiting(RequestPipelineSetup.UploadPolicy)]
    public async Task<ActionResult<UploadProbeResultDto>> Check(
        UploadProbeRequest request, CancellationToken ct) =>
        Ok(await uploadProbe.ProbeAsync(request.Files ?? [], ct));

    /// <remarks>
    /// 400 с телом <see cref="UploadResultDto"/> — это не ошибка формата, а осознанная форма
    /// частичного успеха: клиенту нужен список отвергнутых файлов с причинами, и он приходит тем
    /// же DTO, что и при успехе.
    /// </remarks>
    [HttpPost]
    [EnableRateLimiting(RequestPipelineSetup.UploadPolicy)]
    [ProducesResponseType<UploadResultDto>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<UploadResultDto>> Upload(CancellationToken ct)
    {
        var candidate = new UploadCandidate(
            UploadHeaders.FileName(Request),
            Request.ContentType,
            Request.ContentLength ?? -1,
            () => Request.Body);

        var result = await upload.UploadAsync(
            candidate, UploadOrigin.WebUpload(currentUser.Id), ct);

        return result.Uploaded.Count == 0 ? BadRequest(result) : Ok(result);
    }
}
