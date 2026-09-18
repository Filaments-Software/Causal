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
	public const string IntroTag = "intro";

	[Property] public float SwitchCooldown { get; set; } = 2f;
	[Property] public bool StartInCause { get; set; } = true;
	[Property] public bool StartShiftUnlocked { get; set; } = false;

	public bool IsCause { get; private set; } = true;
	public bool ShiftUnlocked { get; private set; }

	private readonly List<GameObject> _causeRoots = new();
	private readonly List<GameObject> _effectRoots = new();
	private readonly List<GameObject> _introCauseRoots = new();
	private readonly List<GameObject> _introEffectRoots = new();
	private bool _menuDichotomy;
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
		RestoreMenuDichotomy();
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

	public void SetMenuDichotomy( bool menu )
	{
		if ( (_causeRoots.Count == 0 && _introCauseRoots.Count == 0)
			|| (_effectRoots.Count == 0 && _introEffectRoots.Count == 0) )
		{
			RefreshRoots();
		}

		_menuDichotomy = menu;
		if ( menu )
		{
			ApplyDichotomy();
		}
		else
		{
			ApplyState();
		}
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
		_introCauseRoots.Clear();
		_introEffectRoots.Clear();

		foreach ( var go in Scene.FindAllWithTag( CauseTag ) )
		{
			if ( !IsTopmostTagged( go, CauseTag ) )
			{
				continue;
			}

			if ( go.Tags.Has( IntroTag, false ) )
			{
				_introCauseRoots.Add( go );
			}
			else
			{
				_causeRoots.Add( go );
			}
		}

		foreach ( var go in Scene.FindAllWithTag( EffectTag ) )
		{
			if ( !IsTopmostTagged( go, EffectTag ) )
			{
				continue;
			}

			if ( go.Tags.Has( IntroTag, false ) )
			{
				_introEffectRoots.Add( go );
			}
			else
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

		foreach ( var go in _introCauseRoots )
		{
			if ( go.IsValid() )
			{
				go.Enabled = true;
			}
		}

		foreach ( var go in _introEffectRoots )
		{
			if ( go.IsValid() )
			{
				go.Enabled = true;
			}
		}
	}

	private void RestoreMenuDichotomy()
	{
		// Host init can land after CausalGameManager.OnStart, which would
		// clobber the menu dichotomy with the exclusive state above.
		var manager = CausalGameManager.Instance;
		bool menu = !manager.IsValid()
			|| manager.State == CausalGameManager.GameState.Menu
			|| manager.State == CausalGameManager.GameState.PreVideo;

		if ( menu )
		{
			SetMenuDichotomy( true );
		}
	}

	private void ApplyDichotomy()
	{
		foreach ( var go in _introCauseRoots )
		{
			if ( go.IsValid() )
			{
				go.Enabled = true;
			}
		}

		foreach ( var go in _introEffectRoots )
		{
			if ( go.IsValid() )
			{
				go.Enabled = true;
			}
		}

		foreach ( var go in _causeRoots )
		{
			if ( go.IsValid() )
			{
				go.Enabled = false;
			}
		}

		foreach ( var go in _effectRoots )
		{
			if ( go.IsValid() )
			{
				go.Enabled = false;
			}
		}
	}

	private void ApplyState()
	{
		if ( (_causeRoots.Count == 0 && _introCauseRoots.Count == 0)
			|| (_effectRoots.Count == 0 && _introEffectRoots.Count == 0) )
		{
			RefreshRoots();
		}

		if ( _menuDichotomy )
		{
			ApplyDichotomy();
			return;
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

		foreach ( var go in _introCauseRoots )
		{
			if ( go.IsValid() )
			{
				go.Enabled = IsCause;
			}
		}

		foreach ( var go in _introEffectRoots )
		{
			if ( go.IsValid() )
			{
				go.Enabled = !IsCause;
			}
		}
	}
}
