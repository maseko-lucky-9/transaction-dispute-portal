namespace DisputePortal.Api.Shared.Security;

/// <summary>Redeems a refresh token at the identity provider.</summary>
public interface ITokenRefresher
{
    Task<RefreshResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
}
