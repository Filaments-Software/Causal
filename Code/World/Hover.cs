using System;

namespace Causal;

public sealed class Hover : Component, Component.ExecuteInEditor
{
	[Property] public float HoverHeight { get; set; } = 8f;
	[Property] public float HoverSpeed { get; set; } = 1.5f;
	[Property] public float SpinSpeed { get; set; }

	private bool _hasBase;
	private Vector3 _basePosition;
	private Rotation _baseRotation;
	private Vector3 _lastWrittenPosition;
	private Rotation _lastWrittenRotation;
	private bool _hasLastWritten;

	protected override void OnUpdate()
	{
		float offset = MathF.Sin( Time.Now * HoverSpeed ) * HoverHeight;

		if ( !_hasBase || (_hasLastWritten && GameObject.WorldPosition != _lastWrittenPosition) )
		{
			_basePosition = GameObject.WorldPosition - Vector3.Up * offset;
			_baseRotation = GameObject.WorldRotation;
			_hasBase = true;
		}
		else if ( _hasLastWritten && GameObject.WorldRotation != _lastWrittenRotation )
		{
			_baseRotation = GameObject.WorldRotation * Rotation.FromYaw( -SpinSpeed * Time.Delta );
		}

		_lastWrittenPosition = _basePosition + Vector3.Up * offset;
		GameObject.WorldPosition = _lastWrittenPosition;

		if ( SpinSpeed != 0f )
		{
			GameObject.WorldRotation *= Rotation.FromYaw( SpinSpeed * Time.Delta );
		}

		_lastWrittenRotation = GameObject.WorldRotation;
		_hasLastWritten = true;
	}

	protected override void OnDisabled()
	{
		RestoreAuthoredTransform();
	}

	protected override void OnDestroy()
	{
		RestoreAuthoredTransform();
	}

	private void RestoreAuthoredTransform()
	{
		if ( !_hasBase )
		{
			return;
		}

		GameObject.WorldPosition = _basePosition;
		GameObject.WorldRotation = _baseRotation;
		_hasLastWritten = false;
	}
}
