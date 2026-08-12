using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/artists")]
public class ArtistsController(CatalogService catalog, ArtistProfileService profiles, StreamingService streaming) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ArtistDto>>> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await catalog.GetArtistsAsync(new PageRequest(page, pageSize), ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ArtistDetailDto>> Get(
        Guid id,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct = default) =>
        Ok(await catalog.GetArtistAsync(id, new PageRequest(page, pageSize), ct));

    [HttpGet("{id:guid}/image")]
    public async Task<IActionResult> Image(Guid id, CancellationToken ct) =>
        this.ImageFile(await streaming.OpenArtistImageAsync(id, ct));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.Admin)]
    public async Task<ActionResult<ArtistDto>> Update(
        Guid id, UpdateArtistRequest request, CancellationToken ct) =>
        Ok(await profiles.RenameAsync(id, request, ct));

    [HttpPost("{id:guid}/image")]
    [Authorize(Policy = AppPolicies.Admin)]
    public async Task<ActionResult<ArtistDto>> UploadImage(Guid id, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new ValidationException("No image was provided.");

        await using var stream = file.OpenReadStream();
        return Ok(await profiles.SetImageAsync(
            id, stream, file.ContentType, file.FileName, file.Length, ct));
    }

    [HttpDelete("{id:guid}/image")]
    [Authorize(Policy = AppPolicies.Admin)]
    public async Task<IActionResult> DeleteImage(Guid id, CancellationToken ct)
    {
        await profiles.RemoveImageAsync(id, ct);
        return NoContent();
    }
}
