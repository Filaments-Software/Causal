namespace Causal;

public sealed class CausalGameManager : Component
{
	[Property] public GameObject Player { get; set; }
	[Property] public float MenuFadeSeconds { get; set; } = 0.6f;
	[Property] public float CameraFlightSeconds { get; set; } = 2.2f;

	public static CausalGameManager Instance { get; private set; }

	public GameState State { get; private set; } = GameState.PreVideo;

	public bool IsActive => State == GameState.Active;
	public bool IsInIntro => State == GameState.Intro;

	private PlayerController _controller;
	private CameraComponent _menuCam;
	private MenuDichotomyRig _rig;
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
		EnterPreVideo();
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

	public void EnterPreVideo()
	{
		State = GameState.PreVideo;
		ApplyInputState();
		SetMenuDichotomy( true );
	}

	public void EnterMenu()
	{
		State = GameState.Menu;
		ApplyInputState();
		SetMenuDichotomy( true );
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

		_menuCam = FindMenuCamera();
		_rig = FindRig();

		if ( _rig.IsValid() && _rig.MenuCam.IsValid() )
		{
			_menuCam = _rig.MenuCam;
		}

		if ( !_menuCam.IsValid() )
		{
			EnterGame();
			return;
		}

		if ( _rig.IsValid() && _rig.IsSplitActive )
		{
			// Split wipe flight: the rig stays up and mirrors the menu
			// camera while the line rotates vertical and sweeps to Cause.
		}
		else
		{
			// Legacy single-state flight from the real menu camera.
			SetMenuDichotomy( false );
			DisableEffectIntroRoots();

			if ( !Scene.Camera.IsValid() )
			{
				EnterGame();
				return;
			}
		}

		_flightFromPosition = _menuCam.WorldPosition;
		_flightFromRotation = _menuCam.WorldRotation;
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
		SetMenuDichotomy( false );
		DisableEffectIntroRoots();
	}

	private void TickIntro()
	{
		if ( !_controller.IsValid() || !_menuCam.IsValid() )
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
		_menuCam.WorldPosition = Vector3.Lerp( _flightFromPosition, target.Position, eased );
		_menuCam.WorldRotation = Rotation.Slerp( _flightFromRotation, target.Rotation, eased );

		if ( _rig.IsValid() )
		{
			float rotateT = MenuFadeSeconds <= 0.01f ? 1f : _timeSinceIntro / MenuFadeSeconds;
			if ( rotateT < 0f )
			{
				rotateT = 0f;
			}
			if ( rotateT > 1f )
			{
				rotateT = 1f;
			}

			_rig.DriveIntroSplit( rotateT, eased );
		}

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

	private CameraComponent FindMenuCamera()
	{
		var scene = Scene;
		if ( !scene.IsValid() )
		{
			return null;
		}

		foreach ( var cam in scene.GetAllComponents<CameraComponent>() )
		{
			if ( cam.IsValid() && cam.IsMainCamera )
			{
				return cam;
			}
		}

		return scene.Camera;
	}

	private MenuDichotomyRig FindRig()
	{
		var scene = Scene;
		if ( !scene.IsValid() )
		{
			return null;
		}

		foreach ( var rig in scene.GetAllComponents<MenuDichotomyRig>() )
		{
			if ( rig.IsValid() )
			{
				return rig;
			}
		}

		return null;
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

	private void DisableEffectIntroRoots()
	{
		// Game start is always Cause with shifting locked, so kill the
		// effect intro roots directly instead of relying on bucket state.
		var scene = Scene;
		if ( !scene.IsValid() )
		{
			return;
		}

		foreach ( var go in scene.FindAllWithTag( TimeShiftManager.EffectTag ) )
		{
			if ( !go.IsValid() || !go.Tags.Has( TimeShiftManager.IntroTag, false ) )
			{
				continue;
			}

			var parent = go.Parent;
			if ( parent.IsValid() && parent.Tags.Has( TimeShiftManager.EffectTag ) )
			{
				continue;
			}

			go.Enabled = false;
		}
	}

	private void SetMenuDichotomy( bool menu )
	{
		var scene = Scene;
		if ( !scene.IsValid() )
		{
			return;
		}

		var shift = scene.GetSystem<TimeShiftManager>();
		if ( shift is not null )
		{
			shift.SetMenuDichotomy( menu );
		}

		foreach ( var rig in scene.GetAllComponents<MenuDichotomyRig>() )
		{
			if ( !rig.IsValid() )
			{
				continue;
			}

			rig.SetMenuActive( menu );
		}
	}

	public enum GameState
	{
		Menu,
		Intro,
		Active,
		PreVideo
	}
}
