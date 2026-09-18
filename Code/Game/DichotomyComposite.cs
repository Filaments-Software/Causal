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
		Attributes.Set( "blend", 1f );
		Attributes.SetComboEnum( "D_BLENDMODE", BlendMode.Normal );

		var blit = BlitMode.WithBackbuffer( Material, Stage.AfterPostProcess, Order, true );
		Blit( blit, "Menu Dichotomy" );
	}
}
