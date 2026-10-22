namespace Modularr.Core.Qualities;

public static class QualityFinder
{
    public static Quality Find(QualitySource source, int resolution, Modifier modifier)
    {
        Quality? exact = Quality.All.SingleOrDefault(q => q.Source == source && q.Resolution == resolution && q.Modifier == modifier);
        if (exact is not null)
        {
            return exact;
        }

        // SD television and web releases must not roll up to Bluray-576p.
        if (resolution < 720 && modifier == Modifier.None)
        {
            Quality? sd = source switch
            {
                QualitySource.Television => Quality.Sdtv,
                QualitySource.WebDl => Quality.WebDl480p,
                QualitySource.WebRip => Quality.WebRip480p,
                _ => null,
            };

            if (sd is not null)
            {
                return sd;
            }
        }

        Quality? anyResolution = Quality.All.FirstOrDefault(q =>
            q.Source == source && q.Resolution == 0 && q.Modifier == modifier && q != Quality.Unknown);

        return anyResolution
            ?? Quality.All
                .Where(q => q.Modifier == modifier && q.Resolution == resolution)
                .OrderBy(q => q.Source)
                .FirstOrDefault(q => q.Source >= source)
            ?? Quality.Unknown;
    }
}
