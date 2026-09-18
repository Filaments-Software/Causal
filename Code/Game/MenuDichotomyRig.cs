using System;

namespace Causal;

public sealed class MenuDichotomyRig : Component
{
	[Property] public string DichotomyMaterialPath { get; set; } = "materials/menu_dichotomy.vmat";
	[Property] public float DichotomyOffset { get; set; } = 0f;
	[Property] public float DichotomySeamWidth { get; set; } = 0.006f;
	[Property] public float SplitVerticalAngle { get; set; } = -1.5708f;
	[Property] public float SplitSweepOffset { get; set; } = -0.8f;

	private GameObject _causeCamGo;
	private GameObject _effectCamGo;
	private CameraComponent _menuCam;
	private DichotomyComposite _composite;
	private Material _material;
	private Texture _causeFeed;
	private Vector2 _feedSize = new( -1f, -1f );
	private bool _menuActive;
	private float _pushedAngle = float.MaxValue;
	private float _pushedOffset = float.MaxValue;
	private float _pushedSeam = float.MaxValue;
	private float _baseAngle;
	private float _splitAngle;
	private float _splitOffset;
	private bool _introDrive;
	private readonly List<ScreenPanel> _retargetedPanels = new();
	private readonly List<CameraComponent> _panelTargets = new();

	protected override void OnUpdate()
	{
		var manager = CausalGameManager.Instance;
		var state = manager.IsValid() ? manager.State : CausalGameManager.GameState.Menu;
		bool wantMenu = state == CausalGameManager.GameState.Menu
			|| state == CausalGameManager.GameState.PreVideo
			|| state == CausalGameManager.GameState.Intro;

		if ( wantMenu != _menuActive )
		{
			SetMenuActive( wantMenu );
		}

		if ( _menuActive )
		{
			TickFeed();
		}
	}

	protected override void OnDisabled()
	{
		Deactivate();
	}

	protected override void OnDestroy()
	{
		Deactivate();
	}

	public void SetMenuActive( bool menu )
	{
		if ( menu == _menuActive && (!menu || _causeCamGo.IsValid()) )
		{
			return;
		}

		_menuActive = menu;
		_introDrive = false;
		if ( menu )
		{
			Activate();
		}
		else
		{
			Deactivate();
		}
	}

	public bool IsSplitActive => _menuActive && _causeCamGo.IsValid() && _composite.IsValid();

	public CameraComponent MenuCam => _menuCam;

	public void DriveIntroSplit( float rotateT, float sweepT )
	{
		if ( !IsSplitActive )
		{
			return;
		}

		_introDrive = true;
		float a = rotateT * rotateT * (3f - 2f * rotateT);
		_splitAngle = _baseAngle + (SplitVerticalAngle - _baseAngle) * a;
		_splitOffset = SplitSweepOffset * sweepT;
	}

	private void Activate()
	{
		if ( _causeCamGo.IsValid() )
		{
			return;
		}

		_menuCam = Scene.Camera;
		if ( !_menuCam.IsValid() )
		{
			Log.Warning( "MenuDichotomyRig found no menu camera." );
			_menuActive = false;
			return;
		}

		_material = Material.Load( DichotomyMaterialPath );
		if ( _material is null )
		{
			Log.Warning( $"MenuDichotomyRig found no material at '{DichotomyMaterialPath}'." );
			_menuActive = false;
			return;
		}

		_menuCam.Enabled = false;

		_causeCamGo = new GameObject( "Menu Cause Cam" );
		var causeCam = _causeCamGo.AddComponent<CameraComponent>();
		CopyView( causeCam );
		causeCam.IsMainCamera = false;
		causeCam.Priority = 1;
		causeCam.RenderExcludeTags.Add( TimeShiftManager.EffectTag );
		CopyTonemapping( _causeCamGo.AddComponent<Tonemapping>() );

		_effectCamGo = new GameObject( "Menu Effect Cam" );
		var effectCam = _effectCamGo.AddComponent<CameraComponent>();
		CopyView( effectCam );
		effectCam.IsMainCamera = false;
		effectCam.Priority = 2;
		effectCam.RenderExcludeTags.Add( TimeShiftManager.CauseTag );
		CopyTonemapping( _effectCamGo.AddComponent<Tonemapping>() );

		var overlay = _effectCamGo.AddComponent<DichotomyComposite>();
		overlay.Material = _material;
		// Composite last so the cause feed is tonemapped exactly once.
		overlay.Order = 1000;
		_composite = overlay;

		RetargetInterface( effectCam );

		_baseAngle = DiagonalAngle( Screen.Size );
		_splitAngle = _baseAngle;
		_splitOffset = 0f;
		_introDrive = false;
		_pushedAngle = float.MaxValue;
		_pushedOffset = float.MaxValue;
		_pushedSeam = float.MaxValue;
		_feedSize = new Vector2( -1f, -1f );
		TickFeed();
	}

	private void Deactivate()
	{
		_menuActive = false;
		RestoreInterface();

		if ( _causeCamGo.IsValid() )
		{
			_causeCamGo.Destroy();
			_causeCamGo = null;
		}

		if ( _effectCamGo.IsValid() )
		{
			_effectCamGo.Destroy();
			_effectCamGo = null;
		}

		_causeFeed = null;
		_material = null;
		_composite = null;

		if ( _menuCam.IsValid() && !_menuCam.Enabled )
		{
			_menuCam.Enabled = true;
		}

		_menuCam = null;
	}

	private static float DiagonalAngle( Vector2 size )
	{
		if ( size.x <= 0f || size.y <= 0f )
		{
			return -MathF.PI / 4f;
		}

		return -MathF.Atan2( size.y, size.x );
	}

	private void CopyView( CameraComponent cam )
	{
		cam.WorldPosition = _menuCam.WorldPosition;
		cam.WorldRotation = _menuCam.WorldRotation;
		cam.FieldOfView = _menuCam.FieldOfView;
		cam.ZNear = _menuCam.ZNear;
		cam.ZFar = _menuCam.ZFar;
		cam.BackgroundColor = _menuCam.BackgroundColor;
		cam.ClearFlags = _menuCam.ClearFlags;
		cam.EnablePostProcessing = _menuCam.EnablePostProcessing;
		cam.Viewport = new Vector4( 0f, 0f, 1f, 1f );
	}

	private void CopyTonemapping( Tonemapping tone )
	{
		var source = _menuCam.GetComponent<Tonemapping>();
		if ( !source.IsValid() )
		{
			return;
		}

		tone.Mode = source.Mode;
		tone.ExposureMethod = source.ExposureMethod;
		tone.AutoExposureEnabled = source.AutoExposureEnabled;
		tone.ExposureCompensation = source.ExposureCompensation;
		tone.MinimumExposure = source.MinimumExposure;
		tone.MaximumExposure = source.MaximumExposure;
		tone.Rate = source.Rate;
	}

	private void RetargetInterface( CameraComponent cam )
	{
		RestoreInterface();

		foreach ( var panel in Scene.GetAllComponents<ScreenPanel>() )
		{
			if ( !panel.IsValid() )
			{
				continue;
			}

			_retargetedPanels.Add( panel );
			_panelTargets.Add( panel.TargetCamera );
			panel.TargetCamera = cam;
		}
	}

	private void RestoreInterface()
	{
		for ( int i = 0; i < _retargetedPanels.Count; i++ )
		{
			var panel = _retargetedPanels[i];
			if ( panel.IsValid() )
			{
				panel.TargetCamera = _panelTargets[i];
			}
		}

		_retargetedPanels.Clear();
		_panelTargets.Clear();
	}

	private void TickFeed()
	{
		if ( !_composite.IsValid() )
		{
			_menuActive = false;
			return;
		}

		var causeCam = _causeCamGo?.GetComponent<CameraComponent>();
		if ( !causeCam.IsValid() )
		{
			_menuActive = false;
			return;
		}

		var effectCam = _effectCamGo?.GetComponent<CameraComponent>();
		if ( !effectCam.IsValid() )
		{
			_menuActive = false;
			return;
		}

		if ( _menuCam.IsValid() )
		{
			causeCam.WorldPosition = _menuCam.WorldPosition;
			causeCam.WorldRotation = _menuCam.WorldRotation;
			effectCam.WorldPosition = _menuCam.WorldPosition;
			effectCam.WorldRotation = _menuCam.WorldRotation;
		}

		Vector2 size = Screen.Size;
		if ( _causeFeed is null || _feedSize != size )
		{
			_causeFeed = Texture.CreateRenderTarget( "MenuDichotomyCause", ImageFormat.RGBA8888, size, _causeFeed );
			_feedSize = size;
			_baseAngle = DiagonalAngle( size );
		}

		causeCam.RenderTarget = _causeFeed;
		_composite.CauseFeed = _causeFeed;
		_composite.Bounds = size;

		float wantAngle = _introDrive ? _splitAngle : _baseAngle;
		float wantOffset = _introDrive ? _splitOffset : DichotomyOffset;

		if ( wantAngle != _pushedAngle )
		{
			_pushedAngle = wantAngle;
			_composite.Angle = _pushedAngle;
		}

		if ( wantOffset != _pushedOffset )
		{
			_pushedOffset = wantOffset;
			_composite.Offset = _pushedOffset;
		}

		if ( DichotomySeamWidth != _pushedSeam )
		{
			_pushedSeam = DichotomySeamWidth;
			_composite.SeamWidth = _pushedSeam;
		}
	}
}
