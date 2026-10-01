// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using App.Dtos;
using App.Services;

namespace Api.Controllers;

[ApiController]
[Route("api/tracks/upload")]
public class TrackUploadsController(
    TrackUploadService upload,
    UploadProbeService uploadProbe) : ControllerBase
{
    [HttpPost("check")]
    public async Task<ActionResult<UploadProbeResultDto>> Check(
        UploadProbeRequest request, CancellationToken ct) =>
        Ok(await uploadProbe.ProbeAsync(request.Files, ct));

    [HttpPost]
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
