// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;

namespace MusicStreaming.Infrastructure.Persistence;

public class ApplicationDbContextFactory(IDbContextFactory<ApplicationDbContext> inner)
    : IApplicationDbContextFactory
{
    public IApplicationDbContext Create() => inner.CreateDbContext();
}
