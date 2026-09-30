// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Abstractions;

public interface IApplicationDbContextFactory
{
    IApplicationDbContext Create();
}
