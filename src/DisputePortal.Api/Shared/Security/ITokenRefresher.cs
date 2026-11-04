namespace DisputePortal.Api.Shared.Security;

/// <summary>Redeems a refresh token at the identity provider. Null means the session can no longer be extended.</summary>
public interface ITokenRefresher
{
    Task<RefreshedSession?> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
}
