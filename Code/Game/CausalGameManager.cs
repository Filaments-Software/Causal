namespace Causal;

public sealed class CausalGameManager : Component
{
	[Property] public GameObject Player { get; set; }
	[Property] public float MenuFadeSeconds { get; set; } = 0.6f;
	[Property] public float CameraFlightSeconds { get; set; } = 2.2f;

	public static CausalGameManager Instance { get; private set; }

	public GameState State { get; private set; } = GameState.Menu;

	public bool IsActive => State == GameState.Active;
	public bool IsInIntro => State == GameState.Intro;

	private PlayerController _controller;
	private TimeSince _timeSinceIntro;
	private Vector3 _flightFromPosition;
	private Rotation _flightFromRotation;

	protected override void OnAwake()
	{
		Instance = this;
	}

	protected override void OnStart()
	{
		ResolvePlayer();
		EnterMenu();
	}

	protected override void OnUpdate()
	{
		if ( State != GameState.Intro )
		{
			return;
		}

		TickIntro();
	}

	protected override void OnDestroy()
	{
		if ( Instance == this )
		{
			Instance = null;
		}
	}

	public void EnterMenu()
	{
		State = GameState.Menu;
		ApplyInputState();
	}

	public void BeginIntro()
	{
		if ( State != GameState.Menu )
		{
			return;
		}

		if ( !_controller.IsValid() )
		{
			Log.Warning( $"CausalGameManager on '{GameObject.Name}' found no PlayerController." );
			return;
		}

		if ( !Scene.Camera.IsValid() )
		{
			EnterGame();
			return;
		}

		_flightFromPosition = Scene.Camera.WorldPosition;
		_flightFromRotation = Scene.Camera.WorldRotation;
		_timeSinceIntro = 0;
		State = GameState.Intro;
		ApplyInputState();
	}

	public void EnterGame()
	{
		if ( State == GameState.Active )
		{
			return;
		}

		State = GameState.Active;
		ApplyInputState();
	}

	private void TickIntro()
	{
		if ( !_controller.IsValid() || !Scene.Camera.IsValid() )
		{
			EnterGame();
			return;
		}

		float flightDuration = CameraFlightSeconds <= 0.01f ? 0.01f : CameraFlightSeconds;
		float flightTime = _timeSinceIntro - MenuFadeSeconds;
		float t = flightTime / flightDuration;
		if ( t < 0f )
		{
			t = 0f;
		}
		if ( t > 1f )
		{
			t = 1f;
		}

		float eased = t * t * (3f - 2f * t);

		Transform target = _controller.EyeTransform;
		Scene.Camera.WorldPosition = Vector3.Lerp( _flightFromPosition, target.Position, eased );
		Scene.Camera.WorldRotation = Rotation.Slerp( _flightFromRotation, target.Rotation, eased );

		if ( t >= 1f )
		{
			EnterGame();
		}
	}

	private void ResolvePlayer()
	{
		if ( Player.IsValid() )
		{
			_controller = Player.GetComponent<PlayerController>();
		}

		if ( !_controller.IsValid() )
		{
			foreach ( var controller in Scene.GetAllComponents<PlayerController>() )
			{
				_controller = controller;
				Player = controller.GameObject;
				break;
			}
		}

		if ( !_controller.IsValid() )
		{
			Log.Warning( $"CausalGameManager on '{GameObject.Name}' found no PlayerController." );
		}
	}

	private void ApplyInputState()
	{
		if ( !_controller.IsValid() )
		{
			return;
		}

		bool active = State == GameState.Active;
		_controller.WishVelocity = 0;
		_controller.UseInputControls = active;
		_controller.UseCameraControls = active;
		_controller.UseLookControls = active;
	}

	public enum GameState
	{
		Menu,
		Intro,
		Active
	}
}
