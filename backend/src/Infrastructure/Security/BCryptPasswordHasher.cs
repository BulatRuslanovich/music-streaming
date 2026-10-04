// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich


namespace Infrastructure.Security;

public class BCryptPasswordHasher
{
    public string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, 12);

    private static readonly string AbsentAccountHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString(), 12);

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(hash))
        {
            BCrypt.Net.BCrypt.Verify(password, AbsentAccountHash);
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}
