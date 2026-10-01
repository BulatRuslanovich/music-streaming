// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Domain.Entities;

namespace App.Abstractions;

public interface ITokenService
{
    IssuedToken CreateAccessToken(User user);
    IssuedRefreshToken CreateRefreshToken(Guid userId);
    string HashRefreshToken(string rawValue);
}

public record IssuedToken(string Value, DateTimeOffset ExpiresAt);

public record IssuedRefreshToken(string RawValue, RefreshToken Entity);
