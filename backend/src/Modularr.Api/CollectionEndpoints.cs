using Modularr.Core.Library;

namespace Modularr.Api;

// Collections and franchises. {id} is signed: a TMDB collection id, or a TMDB keyword id negated (-180547 is the MCU).
public static class CollectionEndpoints
{
    // One of TmdbId (a collection) or KeywordId (a franchise). AddMissing adds the released parts not in the library, with a search.
    public sealed record FollowRequest(int? TmdbId = null, int? KeywordId = null, bool Monitored = true, int? QualityProfileId = null, bool AddMissing = false);

    public sealed record CollectionUpdate(bool? Monitored = null, int? QualityProfileId = null);

    public static RouteGroupBuilder MapCollectionApi(this RouteGroupBuilder api)
    {
        // ?suggested=true adds the curated popular sagas.
        api.MapGet("/collections", (CollectionService collections, CancellationToken ct, bool suggested = false) => collections.ListAsync(suggested, ct));
        api.MapGet("/collections/lookup", (string term, CollectionService collections, CancellationToken ct) => collections.SearchAsync(term, ct))
            .RequireRateLimiting(AuthEndpoints.OutboundRateLimit);
        api.MapGet("/collections/{id:int}", (int id, CollectionService collections, CancellationToken ct) => collections.GetAsync(id, ct));
        api.MapPost("/collections", (FollowRequest request, CollectionService collections, CancellationToken ct) =>
        {
            int id = (request.TmdbId, request.KeywordId) switch
            {
                (int tmdbId and > 0, null) => tmdbId,
                (null, int keywordId and > 0) => -keywordId,
                _ => throw new ArgumentException("Give either a positive tmdbId or keywordId.", nameof(request)),
            };
            return collections.FollowAsync(id, request.Monitored, request.QualityProfileId, request.AddMissing, ct);
        });
        api.MapPatch("/collections/{id:int}", (int id, CollectionUpdate update, CollectionService collections, CancellationToken ct) =>
            collections.UpdateAsync(id, update.Monitored, update.QualityProfileId, ct));
        api.MapDelete("/collections/{id:int}", async (int id, CollectionService collections, CancellationToken ct) =>
        {
            await collections.UnfollowAsync(id, ct);
            return Results.NoContent();
        });
        api.MapPost("/collections/{id:int}/add-missing", async (int id, CollectionService collections, CancellationToken ct) =>
            new { added = (await collections.AddMissingAsync(id, includeUpcoming: false, fresh: false, ct)).Count });
        return api;
    }
}
