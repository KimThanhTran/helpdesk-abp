using System;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Helpdesk.Hosting;

/// <summary>
/// The keys that sign and encrypt access tokens, derived from one secret. Without a fixed key the server makes new ones
/// every time it starts, so every token issued before a restart stops working and everyone is signed out: on a free
/// host that sleeps when idle, that is every quarter of an hour. With the same secret the same keys come back.
/// </summary>
public static class TokenKeys
{
    /// <summary>A short or guessable secret makes the keys guessable; generate one with at least 24 characters.</summary>
    public const int MinSecretLength = 24;

    /// <summary>256 bits for one purpose ("signing" or "encryption"); the two purposes never get the same key.</summary>
    public static byte[] Derive(string secret, string purpose)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < MinSecretLength)
        {
            throw new ArgumentException($"The token key secret must be at least {MinSecretLength} characters long.", nameof(secret));
        }

        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(secret), outputLength: 32, salt: null, info: Encoding.UTF8.GetBytes("helpdesk-" + purpose));
    }

    /// <summary>
    /// The RSA key that signs tokens (OpenIddict wants an asymmetric one), made from the secret alone: the primes are searched
    /// in a stream of bytes derived from it, so the same secret always gives the same key. Takes a second or so.
    /// </summary>
    public static RSAParameters DeriveRsa(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < MinSecretLength)
        {
            throw new ArgumentException($"The token key secret must be at least {MinSecretLength} characters long.", nameof(secret));
        }

        var p = Prime(secret, "p");
        var q = Prime(secret, "q");
        var e = new BigInteger(65537);
        var d = ModInverse(e, BigInteger.Multiply(p - 1, q - 1) / BigInteger.GreatestCommonDivisor(p - 1, q - 1));
        var half = 128;
        byte[] Bytes(BigInteger v, int length)
        {
            var raw = v.ToByteArray(isUnsigned: true, isBigEndian: true);
            return new byte[length - raw.Length].Concat(raw).ToArray();
        }

        return new RSAParameters
        {
            Modulus = Bytes(p * q, 2 * half),
            Exponent = Bytes(e, 3),
            D = Bytes(d, 2 * half),
            P = Bytes(p, half),
            Q = Bytes(q, half),
            DP = Bytes(d % (p - 1), half),
            DQ = Bytes(d % (q - 1), half),
            InverseQ = Bytes(ModInverse(q, p), half),
        };
    }

    private static BigInteger Prime(string secret, string name)
    {
        for (var i = 0; ; i++)
        {
            var bytes = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(secret), 128, null, Encoding.UTF8.GetBytes($"helpdesk-rsa-{name}-{i}"));
            bytes[0] |= 0xC0; // the two top bits keep p * q at 2048 bits
            bytes[^1] |= 1;
            var candidate = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
            if (IsPrime(candidate) && BigInteger.GreatestCommonDivisor(candidate - 1, 65537) == 1)
            {
                return candidate;
            }
        }
    }

    private static readonly int[] SmallPrimes = Enumerable.Range(3, 1000).Where(n => Enumerable.Range(2, (int)Math.Sqrt(n) - 1).All(f => n % f != 0)).ToArray();

    private static bool IsPrime(BigInteger n)
    {
        if (SmallPrimes.Any(sp => n % sp == 0))
        {
            return false;
        }

        var d = n - 1;
        var r = 0;
        while (d.IsEven)
        {
            d >>= 1;
            r++;
        }

        // the bases are fixed, which is fine here: the candidates come from a secret, nobody can pick them to fool the test
        foreach (var a in SmallPrimes.Take(24))
        {
            var x = BigInteger.ModPow(a, d, n);
            if (x == 1 || x == n - 1)
            {
                continue;
            }

            var composite = true;
            for (var i = 1; i < r && composite; i++)
            {
                x = BigInteger.ModPow(x, 2, n);
                composite = x != n - 1;
            }

            if (composite)
            {
                return false;
            }
        }

        return true;
    }

    private static BigInteger ModInverse(BigInteger a, BigInteger m)
    {
        BigInteger oldR = m, r = a % m, oldS = 0, s = 1;
        while (r != 0)
        {
            var quotient = oldR / r;
            (oldR, r) = (r, oldR - quotient * r);
            (oldS, s) = (s, oldS - quotient * s);
        }

        return ((oldS % m) + m) % m;
    }
}
