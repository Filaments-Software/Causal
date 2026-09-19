using Sandbox.Rendering;

namespace Causal;

public sealed class DichotomyComposite : BasePostProcess
{
	public Material Material { get; set; }
	public Texture CauseFeed { get; set; }
	public Vector2 Bounds { get; set; }
	public float Angle { get; set; } = -0.7f;
	public float Offset { get; set; }
	public float SeamWidth { get; set; } = 0.006f;
	public float WavePhase { get; set; }
	public float WaveAmplitude { get; set; }
	public float WaveFrequency { get; set; }
	public float VoronoiCellSize { get; set; } = 120f;
	public float VoronoiCoverage { get; set; }
	public float VoronoiEmpty { get; set; }
	public float VoronoiShadow { get; set; }
	public float VoronoiShadowWidth { get; set; } = 0.25f;
	public float VoronoiPulse { get; set; } = 0.08f;
	public float VoronoiAngle { get; set; }
	public float VoronoiDrift { get; set; }
	public float SplitMix { get; set; }
	public float IntroWave { get; set; } = -1f;
	public int Order { get; set; }

	public override void Render()
	{
		if ( !Material.IsValid() || CauseFeed is null )
		{
			return;
		}

		Attributes.Set( "CauseFeed", CauseFeed );
		Attributes.Set( "DichotomyBounds", Bounds );
		Attributes.Set( "DichotomyAngle", Angle );
		Attributes.Set( "DichotomyOffset", Offset );
		Attributes.Set( "DichotomySeamWidth", SeamWidth );
		Attributes.Set( "DichotomyWavePhase", WavePhase );
		Attributes.Set( "DichotomyWaveAmplitude", WaveAmplitude );
		Attributes.Set( "DichotomyWaveFrequency", WaveFrequency );
		Attributes.Set( "DichotomyVoronoiCellSize", VoronoiCellSize );
		Attributes.Set( "DichotomyVoronoiCoverage", VoronoiCoverage );
		Attributes.Set( "DichotomyVoronoiEmpty", VoronoiEmpty );
		Attributes.Set( "DichotomyVoronoiShadow", VoronoiShadow );
		Attributes.Set( "DichotomyVoronoiShadowWidth", VoronoiShadowWidth );
		Attributes.Set( "DichotomyVoronoiPulse", VoronoiPulse );
		Attributes.Set( "DichotomyVoronoiAngle", VoronoiAngle );
		Attributes.Set( "DichotomyVoronoiDrift", VoronoiDrift );
		Attributes.Set( "DichotomySplitMix", SplitMix );
		Attributes.Set( "DichotomyIntroWave", IntroWave );
		Attributes.Set( "blend", 1f );
		Attributes.SetComboEnum( "D_BLENDMODE", BlendMode.Normal );

		var blit = BlitMode.WithBackbuffer( Material, Stage.AfterPostProcess, Order, true );
		Blit( blit, "Menu Dichotomy" );
	}
}
