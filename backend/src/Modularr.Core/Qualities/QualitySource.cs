namespace Modularr.Core.Qualities;

// Order matters: QualityFinder falls back to the nearest source at or above the requested one.
public enum QualitySource
{
    Unknown,
    Cam,
    Telesync,
    Telecine,
    Workprint,
    Dvd,
    Television,
    WebDl,
    WebRip,
    Bluray,
}
