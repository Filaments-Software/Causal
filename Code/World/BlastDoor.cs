using System;
using Sandbox.MovieMaker;

namespace Causal;

public sealed class BlastDoor : Component
{
	[Property] public MovieResource OpenMovie { get; set; }
	[Property] public MovieResource CloseMovie { get; set; }
	[Property] public float ActionCooldown { get; set; } = 2f;
	[Property] public GameObject VfxObject { get; set; }
	[Property] public float VfxSeconds { get; set; } = 2f;
	[Property] public bool StartOpen { get; set; }
	[Property] public bool AllowInterrupt { get; set; } = false;
	[Property] public bool AdvanceWhileHidden { get; set; } = true;

	public bool IsOpen { get; private set; }

	public bool IsAnimating => _player.IsValid() && _player.IsPlaying;

	public bool CanClose => CloseMovie is not null && CloseMovie.IsValid();

	private MoviePlayer _player;
	private GameObject _vfx;
	private TimeSince _timeSinceAction = 99f;
	private TimeSince _timeSinceVfx = 99f;
	private bool _vfxShowing;
	private float _storedPosition;
	private bool _wasPlaying;
	private MovieResource _savedMovie;
	private float _savedPosition;
	private float _savedTimeScale = 1f;
	private bool _savedWasPlaying;
	private bool _hasSaved;
	private TimeSince _timeSinceHidden;

	protected override void OnStart()
	{
		_player = GetComponent<MoviePlayer>();

		if ( !_player.IsValid() )
		{
			Log.Warning( $"BlastDoor on '{GameObject.Name}' needs a MoviePlayer on the same GameObject." );
			return;
		}

		_player.CreateTargets = false;
		IsOpen = StartOpen;
		_timeSinceAction = 99f;
		ResolveVfx();
	}

	public void Open()
	{
		Play( OpenMovie, true );
	}

	public void Close()
	{
		Play( CloseMovie, false );
	}

	public void Toggle()
	{
		if ( IsOpen )
		{
			Close();
		}
		else
		{
			Open();
		}
	}

	protected override void OnDisabled()
	{
		if ( !_player.IsValid() )
		{
			return;
		}

		_savedMovie = _player.Resource as MovieResource;
		if ( !_savedMovie.IsValid() )
		{
			_savedMovie = IsOpen ? OpenMovie : CloseMovie;
		}

		_savedPosition = _player.PositionSeconds;
		_savedTimeScale = _player.TimeScale;
		_savedWasPlaying = _player.IsPlaying || _storedPosition > 0.05f;
		_timeSinceHidden = 0f;
		_hasSaved = true;
	}

	protected override void OnEnabled()
	{
		if ( !_hasSaved )
		{
			return;
		}

		_hasSaved = false;

		if ( !_savedWasPlaying || !_savedMovie.IsValid() || !_player.IsValid() )
		{
			return;
		}

		float end = MovieGate.MovieDurationSeconds( _savedMovie );
		float target = AdvanceWhileHidden ? _savedPosition + (float)_timeSinceHidden * _savedTimeScale : _savedPosition;
		target = end > 0f ? Math.Clamp( target, 0f, Math.Max( 0f, end - 0.05f ) ) : 0f;
		_player.Play( _savedMovie );
		_player.TimeScale = _savedTimeScale;
		_player.PositionSeconds = target;
		_player.IsPlaying = true;
		_storedPosition = target;
		_wasPlaying = true;
	}

	private void Play( MovieResource movie, bool targetState )
	{
		if ( !GameObject.IsValid() )
		{
			return;
		}

		if ( !_player.IsValid() )
		{
			Log.Warning( $"BlastDoor on '{GameObject.Name}' has no MoviePlayer." );
			return;
		}

		if ( movie is null || !movie.IsValid() )
		{
			Log.Warning( $"BlastDoor on '{GameObject.Name}' is missing its {(targetState ? "OpenMovie" : "CloseMovie")}." );
			return;
		}

		if ( IsOpen == targetState )
		{
			return;
		}

		if ( _timeSinceAction < ActionCooldown )
		{
			return;
		}

		if ( !AllowInterrupt && _player.IsPlaying )
		{
			return;
		}

		_player.Play( movie );
		_timeSinceAction = 0f;
		_storedPosition = 0f;
		IsOpen = targetState;
		if ( targetState )
		{
			ShowVfx();
		}
	}

	protected override void OnUpdate()
	{
		TickVfx();

		if ( !_player.IsValid() )
		{
			return;
		}

		if ( _player.IsPlaying )
		{
			_storedPosition = _player.PositionSeconds;
			_wasPlaying = true;
			return;
		}

		if ( !_wasPlaying )
		{
			return;
		}

		_wasPlaying = false;
		float pos = _player.PositionSeconds;
		float end = ClipEndSeconds();
		bool finished = end > 0f ? pos >= end - 0.05f : pos >= _storedPosition - 0.05f;
		if ( _storedPosition > 0.05f && !finished )
		{
			_player.PositionSeconds = _storedPosition;
			_player.IsPlaying = true;
		}
		else
		{
			_storedPosition = 0f;
		}
	}

	private float ClipEndSeconds()
	{
		var clip = _player.Clip;
		if ( clip is null )
		{
			return 0f;
		}

		return (float)clip.Duration.TotalSeconds;
	}

	private void ResolveVfx()
	{
		_vfx = VfxObject;

		if ( !_vfx.IsValid() )
		{
			Log.Warning( $"BlastDoor on '{GameObject.Name}' has no VfxObject assigned." );
			return;
		}

		_vfx.Enabled = false;
	}

	private void ShowVfx()
	{
		if ( !_vfx.IsValid() )
		{
			return;
		}

		_vfx.Enabled = true;
		_timeSinceVfx = 0f;
		_vfxShowing = true;
	}

	private void TickVfx()
	{
		if ( !_vfxShowing )
		{
			return;
		}

		if ( !_vfx.IsValid() )
		{
			_vfxShowing = false;
			return;
		}

		if ( _timeSinceVfx >= VfxSeconds )
		{
			_vfx.Enabled = false;
			_vfxShowing = false;
		}
	}
}
