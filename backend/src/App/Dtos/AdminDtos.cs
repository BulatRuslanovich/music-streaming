// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Dtos;

public record AuthUserDto(
    Guid Id,
    string Username,
    bool IsAdmin,
    bool IsActive,
    DateTimeOffset CreatedAt);

public record CreateUserRequest(
    string Username,
    string Password,
    bool IsAdmin);

public record SetUserActiveRequest(bool IsActive);

public record SetUserRoleRequest(bool IsAdmin);

public record ResetPasswordRequest(string NewPassword);
