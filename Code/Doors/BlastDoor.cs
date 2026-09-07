using Sandbox.MovieMaker;

namespace Causal;

public sealed class BlastDoor : Component
{
	[Property] public MovieResource OpenMovie { get; set; }
	[Property] public MovieResource CloseMovie { get; set; }
	[Property] public float ActionCooldown { get; set; } = 2f;
	[Property] public bool StartOpen { get; set; }
	[Property] public bool AllowInterrupt { get; set; } = false;

	public bool IsOpen { get; private set; }

	public bool IsAnimating => _player.IsValid() && _player.IsPlaying;

	private MoviePlayer _player;
	private TimeSince _timeSinceAction = 99f;

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
		IsOpen = targetState;
	}
}
