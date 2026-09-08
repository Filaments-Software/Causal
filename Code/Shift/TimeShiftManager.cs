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

	public bool IsCause { get; private set; } = true;

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
		_timeSinceShift = 99f;
		ApplyState();
	}

	void ITimeShiftEvent.OnTimeShiftRequested()
	{
		RequestShift();
	}

	public void RequestShift()
	{
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
