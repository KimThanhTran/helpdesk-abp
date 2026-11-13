using System;
using System.Security.Cryptography;
using Shouldly;
using Xunit;

namespace Helpdesk.Hosting;

public class TokenKeysTests
{
    private const string Secret = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void The_same_secret_always_gives_the_same_keys_so_tokens_survive_a_restart()
    {
        TokenKeys.Derive(Secret, "signing").ShouldBe(TokenKeys.Derive(Secret, "signing"));
        TokenKeys.Derive(Secret, "encryption").ShouldBe(TokenKeys.Derive(Secret, "encryption"));
    }

    [Fact]
    public void Signing_and_encryption_never_share_a_key_and_another_secret_gives_other_keys()
    {
        TokenKeys.Derive(Secret, "signing").ShouldNotBe(TokenKeys.Derive(Secret, "encryption"));
        TokenKeys.Derive(Secret, "signing").ShouldNotBe(TokenKeys.Derive(Secret + "x", "signing"));
    }

    [Fact]
    public void A_key_is_256_bits()
    {
        TokenKeys.Derive(Secret, "signing").Length.ShouldBe(32);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-short")]
    [InlineData("12345678901234567890123")] // 23 characters
    public void A_short_or_empty_secret_is_refused(string secret)
    {
        Should.Throw<ArgumentException>(() => TokenKeys.Derive(secret, "signing"));
    }

    [Fact]
    public void Exactly_the_minimum_length_is_accepted()
    {
        TokenKeys.Derive(new string('a', TokenKeys.MinSecretLength), "signing").Length.ShouldBe(32);
    }

    [Fact]
    public void The_rsa_key_is_the_same_for_the_same_secret_and_really_signs()
    {
        var one = TokenKeys.DeriveRsa(Secret);
        TokenKeys.DeriveRsa(Secret).Modulus.ShouldBe(one.Modulus);
        TokenKeys.DeriveRsa(Secret + "x").Modulus.ShouldNotBe(one.Modulus);
        one.Modulus!.Length.ShouldBe(256);

        using var rsa = RSA.Create();
        rsa.ImportParameters(one); // throws if the parts do not belong together
        var data = new byte[] { 1, 2, 3 };
        rsa.VerifyData(data, rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).ShouldBeTrue();
    }

    [Fact]
    public void A_short_secret_is_refused_for_the_rsa_key_too()
    {
        Should.Throw<ArgumentException>(() => TokenKeys.DeriveRsa("short"));
    }
}
