using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Decisions;
using Modularr.Core.Persistence;
using Modularr.Core.Qualities;

namespace Modularr.Core.Library;

// Quality profiles and the per-category defaults (MediaOptions) that point at them.
public sealed class QualityProfileService(ModularrDbContext db, SettingsFile settings, IOptionsMonitor<ModularrOptions> options)
{
    public async Task<IReadOnlyList<QualityProfile>> ListAsync(CancellationToken cancellationToken) =>
        await db.QualityProfiles.AsNoTracking().OrderBy(p => p.Id).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<QualityProfile> CreateAsync(QualityProfile profile, CancellationToken cancellationToken)
    {
        Validate(profile);
        await EnsureUniqueNameAsync(profile.Name, id: 0, cancellationToken).ConfigureAwait(false);
        QualityProfile created = profile with { Id = 0, Name = profile.Name.Trim() };
        db.QualityProfiles.Add(created);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return created;
    }

    public async Task<QualityProfile> UpdateAsync(int id, QualityProfile profile, CancellationToken cancellationToken)
    {
        Validate(profile);
        QualityProfile existing = await db.QualityProfiles.FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Quality profile {id} does not exist.");
        await EnsureUniqueNameAsync(profile.Name, id, cancellationToken).ConfigureAwait(false);
        QualityProfile updated = profile with { Id = id, Name = profile.Name.Trim() };
        db.Entry(existing).CurrentValues.SetValues(updated);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return updated;
    }

    // A profile used by a series, a movie or a category default stays: the foreign keys restrict, and the defaults must resolve.
    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        MediaOptions media = options.CurrentValue.Media;
        if (media.Movies.ProfileId == id || media.Series.ProfileId == id || media.Anime.ProfileId == id)
        {
            throw new ArgumentException($"Quality profile {id} is a default in the media settings.", nameof(id));
        }

        if (await db.Series.AnyAsync(s => s.QualityProfileId == id, cancellationToken).ConfigureAwait(false)
            || await db.Movies.AnyAsync(m => m.QualityProfileId == id, cancellationToken).ConfigureAwait(false))
        {
            throw new ArgumentException($"Quality profile {id} is used by the library.", nameof(id));
        }

        if (await db.QualityProfiles.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) == 0)
        {
            throw new KeyNotFoundException($"Quality profile {id} does not exist.");
        }
    }

    public async Task SaveMediaAsync(MediaOptions media, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);
        media.EnsureValid();
        int[] ids = [media.Movies.ProfileId, media.Series.ProfileId, media.Anime.ProfileId];
        List<int> known = await db.QualityProfiles.Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (ids.Except(known).FirstOrDefault() is int missing and > 0)
        {
            throw new ArgumentException($"Quality profile {missing} does not exist.", nameof(media));
        }

        await settings.SaveMediaAsync(media, cancellationToken).ConfigureAwait(false);
    }

    // Every quality ranked exactly once (a missing one would rank first, upstream's GetIndex quirk), groups numbered from 1000
    // and holding qualities only, at least one allowed item, and the cutoff on an allowed item.
    public static void Validate(QualityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new ArgumentException("A quality profile needs a name.", nameof(profile));
        }

        foreach (QualityProfileQualityItem item in profile.Items)
        {
            bool single = item.Quality is not null && item.Items.Count == 0;
            bool group = item.Quality is null && item.Id >= QualityProfileQualityItem.FirstGroupId && item.Items.Count > 0 && item.Items.All(q => q.Quality is not null && q.Items.Count == 0);
            if (!single && !group)
            {
                throw new ArgumentException($"'{item}' is neither a quality nor a group (id 1000 or more) of qualities.", nameof(profile));
            }
        }

        Quality[] ranked = [.. profile.Items.SelectMany(i => i.Quality is null ? i.Items.Select(q => q.Quality!) : [i.Quality])];
        if (ranked.Length != Quality.All.Count || ranked.Distinct().Count() != ranked.Length)
        {
            throw new ArgumentException("Each quality must appear exactly once in the profile.", nameof(profile));
        }

        int[] groupIds = [.. profile.Items.Where(i => i.Quality is null).Select(i => i.Id)];
        if (groupIds.Distinct().Count() != groupIds.Length)
        {
            throw new ArgumentException("Group ids must be unique.", nameof(profile));
        }

        if (!profile.Items.Any(i => i.Allowed))
        {
            throw new ArgumentException("A quality profile needs at least one allowed quality.", nameof(profile));
        }

        if (!profile.Items.Any(i => i.Allowed && (i.Quality?.Id ?? i.Id) == profile.Cutoff))
        {
            throw new ArgumentException("The cutoff must be an allowed quality or group.", nameof(profile));
        }
    }

    private async Task EnsureUniqueNameAsync(string name, int id, CancellationToken cancellationToken)
    {
        string trimmed = name.Trim();
        if (await db.QualityProfiles.AnyAsync(p => p.Name == trimmed && p.Id != id, cancellationToken).ConfigureAwait(false))
        {
            throw new ArgumentException($"A quality profile named '{trimmed}' already exists.", nameof(name));
        }
    }
}
