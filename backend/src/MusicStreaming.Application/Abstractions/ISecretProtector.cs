// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Abstractions;

/// <summary>Reversible protection for secrets stored at rest, such as third-party session keys.</summary>
public interface ISecretProtector
{
    string Protect(string value);
    string? Unprotect(string protectedValue);
}
