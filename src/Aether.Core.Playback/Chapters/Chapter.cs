namespace Aether.Core.Playback.Chapters;

/// <summary>One chapter: where it starts (seconds) and its title, which may be empty.</summary>
public readonly record struct Chapter(double Start, string Title);
