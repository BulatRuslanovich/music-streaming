// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Abstractions;

public interface ICurrentUser
{
    Guid Id { get; }
}
