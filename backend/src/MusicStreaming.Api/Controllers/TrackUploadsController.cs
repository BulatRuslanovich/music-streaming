// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

/// <summary>File upload: the pre-flight probe and the request body itself.</summary>
[ApiController]
[Route("api/tracks/upload")]
public class TrackUploadsController(
    TrackUploadService upload,
    UploadProbeService uploadProbe) : ControllerBase
{
    [HttpPost("check")]
    public async Task<ActionResult<UploadProbeResultDto>> Check(
        UploadProbeRequest request, CancellationToken ct) =>
        Ok(await uploadProbe.ProbeAsync(request.Files ?? [], ct));

    [HttpPost]
    [ProducesResponseType<UploadResultDto>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<UploadResultDto>> Upload(
        [FromHeader(Name = "X-File-Name")] string fileName, CancellationToken ct)
    {
        var candidate = new UploadCandidate(
            Uri.UnescapeDataString(fileName),
            Request.ContentType,
            Request.ContentLength ?? -1,
            () => Request.Body);

        var result = await upload.UploadAsync(candidate, ct);

        return result.Uploaded.Count == 0 ? BadRequest(result) : Ok(result);
    }
}
