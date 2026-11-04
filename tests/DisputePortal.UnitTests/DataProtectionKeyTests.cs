using DisputePortal.Api.Shared.Persistence;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using NSubstitute;

namespace DisputePortal.UnitTests;

public sealed class DataProtectionKeyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

    private readonly IKeyManager keyManager = Substitute.For<IKeyManager>();

    [Fact]
    public void AnEmptyKeyRingGetsAKeyActiveNow()
    {
        keyManager.GetAllKeys().Returns([]);

        MigrationRunner.EnsureDataProtectionKey(keyManager, Now);

        keyManager.Received(1).CreateNewKey(Now, Arg.Is<DateTimeOffset>(expiry => expiry > Now.AddDays(30)));
    }

    [Fact]
    public void AUsableKeyIsKept()
    {
        // Built before Returns(): configuring a substitute inside another's Returns() confuses NSubstitute.
        IKey usable = Key(Now.AddDays(-1), Now.AddDays(80), revoked: false);
        keyManager.GetAllKeys().Returns([usable]);

        MigrationRunner.EnsureDataProtectionKey(keyManager, Now);

        keyManager.DidNotReceiveWithAnyArgs().CreateNewKey(default, default);
    }

    [Theory]
    [InlineData(true, -1, 80)]
    [InlineData(false, -100, -1)]
    [InlineData(false, 2, 90)]
    public void ARevokedExpiredOrNotYetActiveKeyIsReplaced(bool revoked, int activatedDays, int expiresDays)
    {
        IKey unusable = Key(Now.AddDays(activatedDays), Now.AddDays(expiresDays), revoked);
        keyManager.GetAllKeys().Returns([unusable]);

        MigrationRunner.EnsureDataProtectionKey(keyManager, Now);

        keyManager.ReceivedWithAnyArgs(1).CreateNewKey(default, default);
    }

    private static IKey Key(DateTimeOffset activation, DateTimeOffset expiration, bool revoked)
    {
        IKey key = Substitute.For<IKey>();
        key.ActivationDate.Returns(activation);
        key.ExpirationDate.Returns(expiration);
        key.IsRevoked.Returns(revoked);
        return key;
    }
}
