// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using App.Abstractions;

namespace Infrastructure.Persistence;

public class ApplicationDbContextFactory(IDbContextFactory<ApplicationDbContext> inner)
    : IApplicationDbContextFactory
{
    public IApplicationDbContext Create() => inner.CreateDbContext();
}
