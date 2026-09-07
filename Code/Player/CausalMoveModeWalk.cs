using System;
using Sandbox.Movement;

namespace Causal;

[Icon( "directions_walk" )]
[Group( "Movement" )]
[Title( "MoveMode - Walk-C" )]
[Description( "Walk and sprint with strafe and backpedal speed penalties" )]
public sealed class CausalMoveModeWalk : MoveModeWalk
{
	[Property] public float SideSpeedMultiplier { get; set; } = 0.65f;
	[Property] public float BackwardSpeedMultiplier { get; set; } = 0.65f;

	private Vector3.SmoothDamped _smoothedMovement;

	public override int Score( PlayerController controller )
	{
		return base.Score( controller ) + 1;
	}

	public override Vector3 UpdateMove( Rotation eyes, Vector3 input )
	{
		eyes = eyes.Angles() with { pitch = 0 };

		input = input.ClampLength( 1 );

		var direction = eyes * input;
		var velocity = GetBalancedSpeed( input );

		if ( direction.IsNearlyZero( 0.1f ) )
		{
			direction = 0;
		}
		else
		{
			_smoothedMovement.Current = direction.Normal * _smoothedMovement.Current.Length;
		}

		_smoothedMovement.Target = direction * velocity;
		_smoothedMovement.SmoothTime = _smoothedMovement.Target.Length < _smoothedMovement.Current.Length
			? Controller.DeaccelerationTime
			: Controller.AccelerationTime;
		_smoothedMovement.Update( Time.Delta );

		if ( _smoothedMovement.Current.IsNearlyZero( 0.01f ) )
		{
			_smoothedMovement.Current = 0;
		}

		return _smoothedMovement.Current;
	}

	private float GetBalancedSpeed( Vector3 input )
	{
		var run = Input.Down( Controller.AltMoveButton );

		if ( Controller.RunByDefault )
		{
			run = !run;
		}

		var velocity = run ? Controller.RunSpeed : Controller.WalkSpeed;

		if ( Controller.IsDucking )
		{
			velocity = Controller.DuckedSpeed;
		}

		return velocity * GetDirectionSpeedMultiplier( input );
	}

	private float GetDirectionSpeedMultiplier( Vector3 input )
	{
		var speedMultiplier = 1f;

		if ( input.x < 0f )
		{
			speedMultiplier *= BackwardSpeedMultiplier;
		}

		if ( MathF.Abs( input.y ) > 0f )
		{
			speedMultiplier *= MathX.Lerp( 1f, SideSpeedMultiplier, MathF.Abs( input.y ) );
		}

		return speedMultiplier;
	}
}
