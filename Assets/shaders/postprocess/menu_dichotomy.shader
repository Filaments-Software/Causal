HEADER
{
	Description = "Causal menu dichotomy: diagonal cause/effect split with styled seam";
}

MODES
{
	Default();
	Forward();
}

COMMON
{
	#include "postprocess/shared.hlsl"
}

struct VertexInput
{
	float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
	float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

struct PixelInput
{
	float2 uv : TEXCOORD0;

	// VS only
	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs		: SV_Position;
	#endif

	// PS only
	#if ( ( PROGRAM == VFX_PROGRAM_PS ) )
		float4 vPositionSs		: SV_Position;
	#endif
};

VS
{
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o;

		o.vPositionPs = float4( i.vPositionOs.xy, 0.0f, 1.0f );
		o.uv = i.vTexCoord;
		return o;
	}
}

PS
{
	#include "postprocess/common.hlsl"
	#include "postprocess/functions.hlsl"
	#include "procedural.hlsl"

	#include "common/blendmode.hlsl"

	// Effect feed: the backbuffer this overlay blits over.
	Texture2D g_tColorBuffer < Attribute( "ColorBuffer" ); SrgbRead( true ); >;

	// Cause feed: render target fed per-frame by MenuDichotomyRig.
	Texture2D g_tCauseFeed < Attribute( "CauseFeed" ); SrgbRead( true ); >;

	float g_flBlend < Attribute( "blend" ); Default( 1.0 ); >;
	float g_flAngle < Attribute( "DichotomyAngle" ); Default( -0.7854 ); >;
	float g_flOffset < Attribute( "DichotomyOffset" ); Default( 0.0 ); >;
	float2 g_vBounds < Attribute( "DichotomyBounds" ); Default2( 1920.0, 1080.0 ); >;
	float g_flSeamWidth < Attribute( "DichotomySeamWidth" ); Default( 0.006 ); >;
	float4 g_vCauseTint < Attribute( "DichotomyCauseTint" ); Default4( 1.0, 0.72, 0.42, 1.0 ); >;
	float4 g_vEffectTint < Attribute( "DichotomyEffectTint" ); Default4( 0.55, 0.82, 1.0, 1.0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 vScreenUv = CalculateViewportUv( i.vPositionSs.xy );

		float4 effectColor = g_tColorBuffer.Sample( g_sBilinearMirror, vScreenUv );
		float4 causeColor = g_tCauseFeed.Sample( g_sBilinearMirror, vScreenUv );

		// Signed distance from the diagonal split line in pixels, so the
		// angle stays corner to corner at any viewport size. Positive side is cause.
		float2 centered = (vScreenUv - float2( 0.5 + g_flOffset, 0.5 )) * g_vBounds;
		float2 lineDir = normalize( float2( cos( g_flAngle ), sin( g_flAngle ) ) );
		float2 lineNormal = float2( -lineDir.y, lineDir.x );
		float dist = dot( centered, lineNormal );

		float halfSeam = g_flSeamWidth * 0.5 * g_vBounds.x;
		float causeMask = smoothstep( -halfSeam, halfSeam, dist );

		float3 color = lerp( effectColor.rgb, causeColor.rgb, causeMask );

		// Styled seam: amber on the cause edge, cyan on the effect edge.
		float seam = 1.0 - smoothstep( 0.0, halfSeam, abs( dist ) );
		float3 seamColor = lerp( g_vEffectTint.rgb, g_vCauseTint.rgb, smoothstep( -halfSeam, halfSeam, dist ) );
		color = lerp( color, seamColor, seam * 0.9 );

		return float4( color, g_flBlend );
	}
}
