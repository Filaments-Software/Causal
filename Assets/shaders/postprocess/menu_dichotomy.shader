HEADER
{
	Description = "Causal menu dichotomy: voronoi cell islands over cause, intro wave to cause";
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
	float g_flWavePhase < Attribute( "DichotomyWavePhase" ); Default( 0.0 ); >;
	float g_flWaveAmp < Attribute( "DichotomyWaveAmplitude" ); Default( 0.0 ); >;
	float g_flWaveFreq < Attribute( "DichotomyWaveFrequency" ); Default( 0.0 ); >;
	float g_flVoronoiCellSize < Attribute( "DichotomyVoronoiCellSize" ); Default( 120.0 ); >;
	float g_flVoronoiCover < Attribute( "DichotomyVoronoiCoverage" ); Default( 0.0 ); >;
	float g_flVoronoiEmpty < Attribute( "DichotomyVoronoiEmpty" ); Default( 0.5 ); >;
	float g_flVoronoiShadow < Attribute( "DichotomyVoronoiShadow" ); Default( 0.0 ); >;
	float g_flVoronoiShadowWidth < Attribute( "DichotomyVoronoiShadowWidth" ); Default( 0.25 ); >;
	float g_flVoronoiPulse < Attribute( "DichotomyVoronoiPulse" ); Default( 0.08 ); >;
	float g_flVoronoiAngle < Attribute( "DichotomyVoronoiAngle" ); Default( 0.0 ); >;
	float g_flVoronoiDrift < Attribute( "DichotomyVoronoiDrift" ); Default( 0.0 ); >;
	float g_flIntroWave < Attribute( "DichotomyIntroWave" ); Default( -1.0 ); >;
	float g_flSplitMix < Attribute( "DichotomySplitMix" ); Default( 0.0 ); >;

	float2 DichotomyHash22( float2 p )
	{
		float3 p3 = frac( float3( p.xyx ) * float3( 0.1031, 0.1030, 0.0973 ) );
		p3 += dot( p3, p3.yzx + 33.33 );
		return frac( (p3.xx + p3.yz) * p3.zy );
	}

	float DichotomyVoronoi( float2 uv, out float2 cellId, out float edge )
	{
		float2 ip = floor( uv );
		float2 fp = frac( uv );
		float nearest = 8.0;
		float second = 8.0;
		float rand = 0.5;
		cellId = ip;
		for ( int j = -1; j <= 1; j++ )
		{
			for ( int i = -1; i <= 1; i++ )
			{
				float2 g = float2( float( i ), float( j ) );
				float2 o = DichotomyHash22( ip + g );
				float2 r = g + o - fp;
				float d = dot( r, r );
				if ( d < nearest )
				{
					second = nearest;
					nearest = d;
					rand = o.x;
					cellId = ip + g;
				}
				else if ( d < second )
				{
					second = d;
				}
			}
		}
		edge = sqrt( second ) - sqrt( nearest );
		return rand;
	}

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

		// Line axis for the traveling sine that pulses the cell outlines.
		float along = dot( centered, lineDir );

		float halfSeam = g_flSeamWidth * 0.5 * g_vBounds.x;

		// Menu base is full cause. On intro the cells shuffle to a random
		// effect/cause mosaic, then flip to cause one by one.
		float causeMask = 1.0;
		float splitMix = clamp( g_flSplitMix, 0.0, 1.0 );

		// Voronoi cells throughout: sparse islands in menu, full web in
		// intro. Outlines pulse with the traveling sine and dissolve per
		// cell once flipped to cause.
		float patchSeam = 0.0;
		float cellFill = 0.0;
		float cellInner = 0.0;
		float cellSize = max( g_flVoronoiCellSize, 1.0 );
		float select = clamp( g_flVoronoiCover, 0.0, 1.0 );
		float introWave = g_flIntroWave;
		bool waveOn = introWave >= 0.0;
		float introMix = max( splitMix, waveOn ? 1.0 : 0.0 );
		if ( select > 0.001 || introMix > 0.001 )
		{
			// Rotating drift around the bottom-right corner with a linear
			// drift blended in, so cells wheel across the screen.
			float ang = g_flVoronoiAngle;
			float cosA = cos( ang );
			float sinA = sin( ang );
			float2 pivot = (float2( 1.0, 1.0 ) - float2( 0.5 + g_flOffset, 0.5 )) * g_vBounds;
			float2 baseUv = (centered - pivot) / cellSize;
			float2 driftVec = float2( 1.0, 0.37 ) * g_flVoronoiDrift;
			float2 voroUv = float2( cosA * baseUv.x - sinA * baseUv.y, sinA * baseUv.x + cosA * baseUv.y );
			voroUv += driftVec;
			float2 cellId;
			float cellEdge;
			float cellRand = DichotomyVoronoi( voroUv, cellId, cellEdge );

			float wave = sin( along * g_flWaveFreq + g_flWavePhase );
			float wave01 = 0.5 + 0.5 * wave;
			float selT = select + wave * g_flVoronoiPulse * 0.5;
			float selSub = (1.0 - smoothstep( selT - 0.12, selT, cellRand )) * step( 0.001, select );
			float sel = lerp( selSub, 1.0, splitMix );
			float lineW = max( (2.5 / cellSize) * (1.0 + wave * g_flVoronoiPulse), 0.001 );
			float outline = (1.0 - smoothstep( 0.0, lineW, cellEdge )) * sel;
			if ( waveOn )
			{
				// Intro shuffle: menu melts into a random effect/cause
				// mosaic over splitMix, then cells flip to cause in random
				// order, each dissolving smoothly once flipped.
				float progress = clamp( introWave, 0.0, 1.0 );
				float shuffle = splitMix;
				float fillPick = DichotomyHash22( cellId + 7.7 ).x;
				float menuFill = step( fillPick, 1.0 - clamp( g_flVoronoiEmpty, 0.0, 1.0 ) );
				menuFill *= smoothstep( 0.0, lineW * 2.0, cellEdge );
				float menuBase = 1.0 - selSub * menuFill;
				float mosaic = step( DichotomyHash22( cellId + 1.7 ).x, 0.5 );
				float baseCell = lerp( menuBase, mosaic, shuffle );
				float ord = 0.05 + 0.9 * DichotomyHash22( cellId + 3.3 ).x;
				float flip = smoothstep( ord - 0.08, ord + 0.08, progress );
				float causeT = max( baseCell, flip );
				causeMask = causeT;
				cellFill = selSub * menuFill * (1.0 - shuffle);
				cellInner = 1.0 - smoothstep( 0.0, max( g_flVoronoiShadowWidth, 0.001 ), cellEdge );
				float endFade = 1.0 - smoothstep( 0.9, 1.0, progress );
				patchSeam = outline * (0.45 + 0.55 * wave01) * (1.0 - causeT) * endFade;
			}
			else
			{
				float fillPick = DichotomyHash22( cellId + 7.7 ).x;
				float fill = step( fillPick, 1.0 - clamp( g_flVoronoiEmpty, 0.0, 1.0 ) );
				fill *= smoothstep( 0.0, lineW * 2.0, cellEdge );
				causeMask = 1.0 - selSub * fill;
				cellFill = selSub * fill;
				cellInner = 1.0 - smoothstep( 0.0, max( g_flVoronoiShadowWidth, 0.001 ), cellEdge );
				patchSeam = outline * (0.45 + 0.55 * wave01);
			}
		}

		float3 color = lerp( effectColor.rgb, causeColor.rgb, causeMask );

	// Inner shadow: dark rim just inside filled cells for depth. The seam
	// glow is added after so outlines stay crisp over the shadow.
	color *= 1.0 - clamp( g_flVoronoiShadow, 0.0, 1.0 ) * cellInner * cellFill;

		// Styled seam: amber on the cause side, cyan on the effect side.
		float seam = patchSeam * 0.9;
		float3 seamColor = lerp( g_vEffectTint.rgb, g_vCauseTint.rgb, smoothstep( -halfSeam, halfSeam, dist ) );
		color = lerp( color, seamColor, seam * 0.9 );

		return float4( color, g_flBlend );
	}
}
