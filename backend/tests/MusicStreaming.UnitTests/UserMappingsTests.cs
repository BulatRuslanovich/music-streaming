// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;
using Xunit;

namespace MusicStreaming.UnitTests;

public class UserMappingsTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 22, 12, 30, 0, TimeSpan.Zero);

    private static readonly User User = new()
    {
        Id = Guid.Parse("0198d1f4-e1b4-7000-8000-000000000001"),
        Username = "listener",
        DisplayName = "Music Listener",
        IsAdmin = true,
        IsActive = false,
        CreatedAt = CreatedAt,
    };

    [Fact]
    public void User_projection_and_object_mapping_expose_the_same_fields()
    {
        var expected = new UserDto(User.Id, User.Username, User.DisplayName, User.IsAdmin);

        Assert.Equal(expected, ToDto.UserProjection.Compile()(User));
        Assert.Equal(expected, ToDto.User(User));
    }

    [Fact]
    public void Admin_projection_and_object_mapping_expose_the_same_fields()
    {
        var expected = new AuthUserDto(
            User.Id,
            User.Username,
            User.DisplayName,
            User.IsAdmin,
            User.IsActive,
            User.CreatedAt);

        Assert.Equal(expected, ToDto.AuthUserProjection.Compile()(User));
        Assert.Equal(expected, ToDto.AuthUser(User));
    }
}
