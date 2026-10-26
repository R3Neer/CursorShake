namespace CursorShake.Core.Cursors;

public readonly record struct ScaleSample(double Scale, TimeSpan EvaluatedAt, long GestureEpoch);

/// <summary>The domain publishes semantic scale; no native cursor handle crosses this port.</summary>
public interface ICursorEngine { void SetScale(ScaleSample sample); }
