using Sandbox.MovieMaker;

namespace Causal;

public sealed class ShiftController : Component
{
	[Property] public MoviePlayer ShiftPlayer { get; set; }
	[Property] public float ShiftAtSeconds { get; set; } = 0.5f;

	private MoviePlayer _shiftPlayer;
	private TimeShiftManager _manager;
	private TimeSince _timeSinceShiftStart;
	private bool _shiftPending;
	private bool _wasPlaying;

	protected override void OnStart()
	{
		_shiftPlayer = ShiftPlayer.IsValid() ? ShiftPlayer : GetComponentInChildren<MoviePlayer>();
		_manager = Game.ActiveScene?.GetSystem<TimeShiftManager>();
		if ( _shiftPlayer.IsValid() )
		{
			var clip = _shiftPlayer.Clip;
			if ( clip is not null )
			{
				_ = clip.Duration;
			}
		}
	}

	protected override void OnUpdate()
	{
		if ( Input.Pressed( "timeshift" ) )
		{
			TryBeginShift();
		}

		PollShiftTrigger();
	}

	private void TryBeginShift()
	{
		if ( CausalGameManager.Instance.IsValid() && !CausalGameManager.Instance.IsActive )
		{
			return;
		}

		var manager = GetManager();
		if ( manager is not null && !manager.ShiftUnlocked )
		{
			return;
		}

		if ( !_shiftPlayer.IsValid() )
		{
			manager?.RequestShift();
			return;
		}

		if ( _shiftPlayer.IsPlaying )
		{
			return;
		}

		_shiftPlayer.Play();
		_timeSinceShiftStart = 0;
		_shiftPending = true;
	}

	private void PollShiftTrigger()
	{
		if ( !_shiftPlayer.IsValid() )
		{
			return;
		}

		bool playing = _shiftPlayer.IsPlaying;

		if ( !_shiftPending )
		{
			_wasPlaying = playing;
			return;
		}

		if ( _timeSinceShiftStart >= ShiftAtSeconds )
		{
			FireShift();
		}
		else if ( _wasPlaying && !playing )
		{
			FireShift();
		}

		_wasPlaying = playing;
	}

	private void FireShift()
	{
		_shiftPending = false;
		GetManager()?.RequestShift();
	}

	private TimeShiftManager GetManager()
	{
		if ( _manager is not null )
		{
			return _manager;
		}

		_manager = Game.ActiveScene?.GetSystem<TimeShiftManager>();
		return _manager;
	}
}
