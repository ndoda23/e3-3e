using System.Security.Cryptography;
using CreditFlow.Application.Users;

namespace CreditFlow.Infrastructure.Identity;

/// <summary>
/// Cryptographically random temporary passwords that satisfy the password policy (length, upper, lower, digit, symbol).
/// Look-alike characters (0/O, 1/l/I) are left out because the password is read out to the user once.
/// </summary>
internal sealed class TemporaryPasswordGenerator : ITemporaryPasswordGenerator
{
    public const int Length = 16;

    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%*?-_+=";
    private const string All = Upper + Lower + Digits + Symbols;

    public string Generate()
    {
        var characters = new char[Length];
        characters[0] = Pick(Upper);
        characters[1] = Pick(Lower);
        characters[2] = Pick(Digits);
        characters[3] = Pick(Symbols);
        for (var i = 4; i < Length; i++)
        {
            characters[i] = Pick(All);
        }

        RandomNumberGenerator.Shuffle(characters.AsSpan());
        return new string(characters);
    }

    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}
