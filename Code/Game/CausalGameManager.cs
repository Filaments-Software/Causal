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
	private bool _paused;

	protected override void OnAwake()
	{
		Instance = this;
	}

	protected override void OnStart()
	{
		CausalSettings.Load();
		ResolvePlayer();
		EnterPreVideo();
	}

	protected override void OnUpdate()
	{
		if ( State == GameState.Active )
		{
			TickPauseInput();
		}

		if ( State != GameState.Intro )
		{
			return;
		}

		TickIntro();
	}

	private void TickPauseInput()
	{
		if ( !Input.EscapePressed )
		{
			return;
		}

		Input.EscapePressed = false;

		var pause = CausalPauseMenu.Instance;
		if ( pause.IsValid() )
		{
			pause.Toggle();
		}
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
		_paused = false;
		ApplyInputState();
		SetMenuDichotomy( true );
	}

	public void EnterMenu()
	{
		State = GameState.Menu;
		_paused = false;
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

		EnsurePlayerAtSpawn();
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

			if ( !PrimaryScene().Camera.IsValid() )
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
		_paused = false;
		ApplyInputState();
		SetMenuDichotomy( false );
		DisableEffectIntroRoots();
		EnsurePlayerAtSpawn();
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
			foreach ( var scene in LookupScenes() )
			{
				if ( !scene.IsValid() )
				{
					continue;
				}

				foreach ( var controller in scene.GetAllComponents<PlayerController>() )
				{
					_controller = controller;
					Player = controller.GameObject;
					break;
				}

				if ( _controller.IsValid() )
				{
					break;
				}
			}
		}

		if ( !_controller.IsValid() )
		{
			Log.Warning( $"CausalGameManager on '{GameObject.Name}' found no PlayerController." );
		}
	}

	private void EnsurePlayerAtSpawn()
	{
		if ( !_controller.IsValid() )
		{
			return;
		}

		var spawn = PickSpawn();
		if ( !spawn.IsValid() )
		{
			Log.Warning( $"CausalGameManager on '{GameObject.Name}' found no active SpawnPoint." );
			return;
		}

		var playerObject = _controller.GameObject;
		if ( !playerObject.IsValid() )
		{
			return;
		}

		playerObject.WorldPosition = spawn.WorldPosition;
		playerObject.WorldRotation = spawn.WorldRotation;
		_controller.WishVelocity = 0;
	}

	// Single decision point for spawn selection. The current rule prefers the
	// Cause branch so game start always lands in Cause. Later priorities such
	// as checkpoints or round state only need to change the ordering here.
	private SpawnPoint PickSpawn()
	{
		SpawnPoint activeFallback = null;
		SpawnPoint causeFallback = null;

		foreach ( var scene in LookupScenes() )
		{
			if ( !scene.IsValid() )
			{
				continue;
			}

			foreach ( var spawn in scene.GetAllComponents<SpawnPoint>() )
			{
				if ( !spawn.IsValid() || !spawn.Enabled )
				{
					continue;
				}

				var spawnObject = spawn.GameObject;
				if ( !spawnObject.IsValid() || !spawnObject.Enabled )
				{
					continue;
				}

				bool branchActive = AreAncestorsEnabled( spawnObject );
				bool cause = IsInCauseBranch( spawnObject );

				if ( branchActive && cause )
				{
					return spawn;
				}

				if ( branchActive && activeFallback is null )
				{
					activeFallback = spawn;
				}

				if ( cause && causeFallback is null )
				{
					causeFallback = spawn;
				}
			}
		}

		return activeFallback ?? causeFallback;
	}

	private static bool AreAncestorsEnabled( GameObject go )
	{
		var parent = go.Parent;
		while ( parent.IsValid() )
		{
			if ( !parent.Enabled )
			{
				return false;
			}

			parent = parent.Parent;
		}

		return true;
	}

	private static bool IsInCauseBranch( GameObject go )
	{
		var current = go;
		while ( current.IsValid() )
		{
			if ( current.Tags.Has( TimeShiftManager.CauseTag ) )
			{
				return true;
			}

			current = current.Parent;
		}

		return false;
	}

	private Scene PrimaryScene()
	{
		var active = Game.ActiveScene;
		if ( active is not null && active.IsValid() )
		{
			return active;
		}

		return Scene;
	}

	private IEnumerable<Scene> LookupScenes()
	{
		var primary = PrimaryScene();
		if ( primary.IsValid() )
		{
			yield return primary;
		}

		if ( Scene.IsValid() && !ReferenceEquals( Scene, primary ) )
		{
			yield return Scene;
		}
	}

	private CameraComponent FindMenuCamera()
	{
		foreach ( var scene in LookupScenes() )
		{
			if ( !scene.IsValid() )
			{
				continue;
			}

			foreach ( var cam in scene.GetAllComponents<CameraComponent>() )
			{
				if ( cam.IsValid() && cam.IsMainCamera )
				{
					return cam;
				}
			}
		}

		var primary = PrimaryScene();
		if ( !primary.IsValid() )
		{
			return null;
		}

		return primary.Camera;
	}

	private MenuDichotomyRig FindRig()
	{
		foreach ( var scene in LookupScenes() )
		{
			if ( !scene.IsValid() )
			{
				continue;
			}

			foreach ( var rig in scene.GetAllComponents<MenuDichotomyRig>() )
			{
				if ( rig.IsValid() )
				{
					return rig;
				}
			}
		}

		return null;
	}

	public void SetPaused( bool paused )
	{
		_paused = paused;
		ApplyInputState();
	}

	private void ApplyInputState()
	{
		if ( !_controller.IsValid() )
		{
			return;
		}

		bool active = State == GameState.Active && !_paused;
		_controller.WishVelocity = 0;
		_controller.UseInputControls = active;
		_controller.UseCameraControls = active;
		_controller.UseLookControls = active;
	}

	private void DisableEffectIntroRoots()
	{
		// Game start is always Cause with shifting locked, so kill the
		// effect intro roots directly instead of relying on bucket state.
		foreach ( var scene in LookupScenes() )
		{
			if ( !scene.IsValid() )
			{
				continue;
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
	}

	private void SetMenuDichotomy( bool menu )
	{
		var primary = PrimaryScene();
		if ( !primary.IsValid() )
		{
			return;
		}

		var shift = primary.GetSystem<TimeShiftManager>();
		if ( shift is not null )
		{
			shift.SetMenuDichotomy( menu );
		}

		foreach ( var scene in LookupScenes() )
		{
			if ( !scene.IsValid() )
			{
				continue;
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
	}

	public enum GameState
	{
		Menu,
		Intro,
		Active,
		PreVideo
	}
}
