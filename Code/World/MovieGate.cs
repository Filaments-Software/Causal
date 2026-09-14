using System;
using Sandbox.MovieMaker;

namespace Causal;

public sealed class MovieGate : Component
{
	public enum HandoffMode
	{
		Absolute,
		Proportional,
		Restart
	}

	[Property] public MoviePlayer Player { get; set; }
	[Property] public MovieResource BaseMovie { get; set; }
	[Property] public MovieResource FailMovie { get; set; }
	[Property] public HandoffMode Handoff { get; set; } = HandoffMode.Absolute;
	[Property] public float FailOffset { get; set; }
	[Property] public float FailCheckFromSeconds { get; set; } = 1f;
	[Property] public float FailCheckToSeconds { get; set; } = 3f;
	[Property] public bool AllowInterrupt { get; set; } = true;
	[Property] public float ActionCooldown { get; set; } = 2f;
	[Property] public bool AdvanceWhileHidden { get; set; } = true;

	public bool IsPlaying => _player.IsValid() && _player.IsPlaying;
	public bool HasBranched => _hasBranched;

	private MoviePlayer _player;
	private IMovieCondition _condition;
	private TimeSince _timeSinceAction = 99f;
	private TimeSince _timeSinceHidden;
	private float _storedPosition;
	private bool _wasPlaying;
	private bool _hasBranched;
	private MovieResource _savedMovie;
	private float _savedPosition;
	private float _savedTimeScale = 1f;
	private bool _savedWasPlaying;
	private bool _hasSaved;

	protected override void OnStart()
	{
		ResolvePlayer();
		ResolveCondition();
	}

	public void PlayBase()
	{
		if ( !GameObject.IsValid() )
		{
			return;
		}

		if ( !_player.IsValid() )
		{
			Log.Warning( $"MovieGate on '{GameObject.Name}' has no MoviePlayer." );
			return;
		}

		if ( BaseMovie is null || !BaseMovie.IsValid() )
		{
			Log.Warning( $"MovieGate on '{GameObject.Name}' is missing its BaseMovie." );
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

		MovieResource selected = BaseMovie;
		if ( FailMovie.IsValid() && HasFailed() )
		{
			selected = FailMovie;
		}

		PlayAt( selected, 0f, true, _player.TimeScale );
		_hasBranched = selected == FailMovie;
		_timeSinceAction = 0f;
	}

	protected override void OnUpdate()
	{
		if ( !_player.IsValid() )
		{
			return;
		}

		if ( _player.IsPlaying )
		{
			_storedPosition = _player.PositionSeconds;
			_wasPlaying = true;
			TickBranch();
			return;
		}

		if ( !_wasPlaying )
		{
			return;
		}

		_wasPlaying = false;
		float pos = _player.PositionSeconds;
		float end = ClipDurationSeconds( _player.Clip );
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

	protected override void OnDisabled()
	{
		if ( !_player.IsValid() )
		{
			return;
		}

		_savedMovie = CurrentMovie();
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

		ResolvePlayer();

		if ( !_savedWasPlaying || !_savedMovie.IsValid() || !_player.IsValid() )
		{
			return;
		}

		float end = MovieDurationSeconds( _savedMovie );
		float target = AdvanceWhileHidden ? _savedPosition + (float)_timeSinceHidden * _savedTimeScale : _savedPosition;
		target = end > 0f ? Math.Clamp( target, 0f, Math.Max( 0f, end - 0.05f ) ) : 0f;
		PlayAt( _savedMovie, target, true, _savedTimeScale );
	}

	private void TickBranch()
	{
		if ( _hasBranched || !FailMovie.IsValid() )
		{
			return;
		}

		float pos = _storedPosition;
		if ( pos < FailCheckFromSeconds || pos > FailCheckToSeconds )
		{
			return;
		}

		if ( !HasFailed() )
		{
			return;
		}

		float target = MapPosition( pos, ClipDurationSeconds( _player.Clip ), MovieDurationSeconds( FailMovie ), Handoff, FailOffset );
		PlayAt( FailMovie, target, true, _player.TimeScale );
		_hasBranched = true;
		_timeSinceAction = 0f;
	}

	private void PlayAt( MovieResource movie, float position, bool resumePlaying, float timeScale )
	{
		if ( !_player.IsValid() || !movie.IsValid() )
		{
			return;
		}

		_player.Play( movie );
		_player.TimeScale = timeScale;
		_player.PositionSeconds = position;
		_player.IsPlaying = resumePlaying;
		_storedPosition = resumePlaying ? position : 0f;
		_wasPlaying = resumePlaying;
	}

	private bool HasFailed()
	{
		return _condition is not null && _condition.HasFailed();
	}

	private MovieResource CurrentMovie()
	{
		var resource = _player.Resource as MovieResource;
		if ( resource.IsValid() )
		{
			return resource;
		}

		return _hasBranched ? FailMovie : BaseMovie;
	}

	private void ResolvePlayer()
	{
		_player = Player.IsValid() ? Player : GetComponent<MoviePlayer>();

		if ( !_player.IsValid() )
		{
			Log.Warning( $"MovieGate on '{GameObject.Name}' needs a MoviePlayer on the same GameObject." );
			return;
		}

		_player.CreateTargets = false;
	}

	private void ResolveCondition()
	{
		_condition = null;

		foreach ( var component in Components.GetAll() )
		{
			if ( component is IMovieCondition condition )
			{
				_condition = condition;
				break;
			}
		}

		if ( _condition is null )
		{
			Log.Warning( $"MovieGate on '{GameObject.Name}' found no IMovieCondition." );
		}
	}

	private static float MapPosition( float basePosition, float baseDuration, float failDuration, HandoffMode mode, float offset )
	{
		float mapped = mode switch
		{
			HandoffMode.Absolute => basePosition + offset,
			HandoffMode.Proportional => baseDuration > 0.001f ? basePosition / baseDuration * failDuration : 0f,
			_ => 0f,
		};

		return failDuration > 0f ? Math.Clamp( mapped, 0f, Math.Max( 0f, failDuration - 0.05f ) ) : 0f;
	}

	private static float ClipDurationSeconds( IMovieClip clip )
	{
		if ( clip is null )
		{
			return 0f;
		}

		return (float)clip.Duration.TotalSeconds;
	}

	internal static float MovieDurationSeconds( MovieResource movie )
	{
		if ( !movie.IsValid() || movie.Compiled is null )
		{
			return 0f;
		}

		return (float)movie.Compiled.Duration.TotalSeconds;
	}
}
