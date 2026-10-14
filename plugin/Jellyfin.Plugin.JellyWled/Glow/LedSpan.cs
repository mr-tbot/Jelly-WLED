namespace Jellyfin.Plugin.JellyWled.Glow;

/// <summary>
/// The strip in LED order: which edge each LED sits on and which fraction of that edge it covers,
/// measured in picture coordinates (left to right for top and bottom, top to bottom for left and right).
/// Mirrors the ButterFlyEffect-Player glow layout and PROTOCOL.md section 3.
/// </summary>
/// <param name="Side">The edge the LED sits on.</param>
/// <param name="T0">Start of the covered fraction (0..1), always less than <paramref name="T1"/>.</param>
/// <param name="T1">End of the covered fraction (0..1).</param>
public readonly record struct LedSpan(Side Side, double T0, double T1);
