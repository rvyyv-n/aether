namespace Aether.PvP.Review.Clipping;

// Fraction runs 0..1. Speed is the encode speed as a multiple of real time (0 when unknown).
public sealed record ClipProgress(double Fraction, double Speed, TimeSpan? Remaining);
