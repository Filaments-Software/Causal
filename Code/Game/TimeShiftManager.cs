namespace Causal;

internal interface ITimeShiftEvent : ISceneEvent<ITimeShiftEvent>
{
	void OnTimeShiftRequested() { }
	void OnTimeShifted( bool isCause ) { }
}

public sealed class TimeShiftManager : GameObjectSystem<TimeShiftManager>, ITimeShiftEvent, ISceneStartup
{
	public const string CauseTag = "cause";
	public const string EffectTag = "effect";

	[Property] public float SwitchCooldown { get; set; } = 2f;
	[Property] public bool StartInCause { get; set; } = true;
	[Property] public bool StartShiftUnlocked { get; set; } = false;

	public bool IsCause { get; private set; } = true;
	public bool ShiftUnlocked { get; private set; }

	private readonly List<GameObject> _causeRoots = new();
	private readonly List<GameObject> _effectRoots = new();
	private TimeSince _timeSinceShift = 99f;

	public TimeShiftManager( Scene scene ) : base( scene )
	{
	}

	void ISceneStartup.OnHostInitialize()
	{
		RefreshRoots();
		IsCause = StartInCause;
		ShiftUnlocked = StartShiftUnlocked;
		_timeSinceShift = 99f;
		WarmRoots();
		ApplyState();
	}

	void ITimeShiftEvent.OnTimeShiftRequested()
	{
		RequestShift();
	}

	public void RequestShift()
	{
		if ( !ShiftUnlocked )
		{
			return;
		}

		float elapsed = _timeSinceShift;
		if ( elapsed >= 0f && elapsed <= SwitchCooldown )
		{
			return;
		}

		_timeSinceShift = 0f;
		IsCause = !IsCause;
		ApplyState();
		ITimeShiftEvent.Post( x => x.OnTimeShifted( IsCause ) );
	}

	public void UnlockShift()
	{
		ShiftUnlocked = true;
	}

	[ConCmd( "unlock_shift", Help = "Debug: unlock time shifting before the device pickup exists." )]
	public static void UnlockShiftCommand()
	{
		var manager = Game.ActiveScene?.GetSystem<TimeShiftManager>();
		if ( manager is null )
		{
			Log.Warning( "causal_unlock_shift found no TimeShiftManager." );
			return;
		}

		manager.UnlockShift();
	}

	private void RefreshRoots()
	{
		_causeRoots.Clear();
		_effectRoots.Clear();

		foreach ( var go in Scene.FindAllWithTag( CauseTag ) )
		{
			if ( IsTopmostTagged( go, CauseTag ) )
			{
				_causeRoots.Add( go );
			}
		}

		foreach ( var go in Scene.FindAllWithTag( EffectTag ) )
		{
			if ( IsTopmostTagged( go, EffectTag ) )
			{
				_effectRoots.Add( go );
			}
		}
	}

	private static bool IsTopmostTagged( GameObject go, string tag )
	{
		var parent = go.Parent;
		return !parent.IsValid() || !parent.Tags.Has( tag );
	}

	private void WarmRoots()
	{
		foreach ( var go in _causeRoots )
		{
			if ( go.IsValid() )
			{
				go.Enabled = true;
			}
		}

		foreach ( var go in _effectRoots )
		{
			if ( go.IsValid() )
			{
				go.Enabled = true;
			}
		}
	}

	private void ApplyState()
	{
		if ( _causeRoots.Count == 0 || _effectRoots.Count == 0 )
		{
			RefreshRoots();
		}

		foreach ( var go in _causeRoots )
		{
			if ( go.IsValid() )
			{
				go.Enabled = IsCause;
			}
		}

		foreach ( var go in _effectRoots )
		{
			if ( go.IsValid() )
			{
				go.Enabled = !IsCause;
			}
		}
	}
}
