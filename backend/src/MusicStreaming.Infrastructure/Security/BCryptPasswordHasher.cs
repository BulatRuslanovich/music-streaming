using MusicStreaming.Application.Abstractions;

namespace MusicStreaming.Infrastructure.Security;

/// <summary>
/// BCrypt password hashing. The work factor is deliberately above the library default so a
/// stolen database costs real time to attack; each hash embeds its own salt and cost.
/// </summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // A malformed stored hash must read as "wrong password", never as an error.
            return false;
        }
    }
}
