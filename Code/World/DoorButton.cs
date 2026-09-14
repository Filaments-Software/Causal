namespace Causal;

public sealed class DoorButton : Component, Component.IPressable
{
	[Property] public GameObject DoorObject { get; set; }

	private HighlightOutline _highlight;
	private BlastDoor _door;

	protected override void OnStart()
	{
		Component visual = (Component)GetComponent<ModelRenderer>() ?? GetComponent<MeshComponent>();
		if ( !visual.IsValid() )
		{
			Log.Warning( $"DoorButton on '{GameObject.Name}' found no ModelRenderer or MeshComponent." );
		}

		if ( !DoorObject.IsValid() )
		{
			Log.Warning( $"DoorButton on '{GameObject.Name}' has no door GameObject assigned." );
		}
		else
		{
			_door = DoorObject.GetComponent<BlastDoor>() ?? DoorObject.GetComponentInChildren<BlastDoor>();
			if ( !_door.IsValid() )
			{
				Log.Warning( $"DoorButton on '{GameObject.Name}' found no BlastDoor on '{DoorObject.Name}' or its children." );
			}
		}

		_highlight = GetComponent<HighlightOutline>();
		if ( !_highlight.IsValid() )
		{
			Log.Warning( $"DoorButton on '{GameObject.Name}' needs a HighlightOutline for hover feedback." );
		}
		else
		{
			_highlight.Enabled = false;
		}
	}

	public bool CanPress( Component.IPressable.Event e )
	{
		return _door.IsValid() && (!_door.IsOpen || _door.CanClose) && (_door.AllowInterrupt || !_door.IsAnimating);
	}

	public bool Press( Component.IPressable.Event e )
	{
		if ( !CanPress( e ) )
		{
			return false;
		}

		_door.Toggle();
		return true;
	}

	public bool Pressing( Component.IPressable.Event e )
	{
		return true;
	}

	public void Release( Component.IPressable.Event e )
	{
	}

	public void Hover( Component.IPressable.Event e )
	{
		if ( _highlight.IsValid() )
		{
			_highlight.Enabled = true;
		}
	}

	public void Look( Component.IPressable.Event e )
	{
	}

	public void Blur( Component.IPressable.Event e )
	{
		if ( _highlight.IsValid() )
		{
			_highlight.Enabled = false;
		}
	}

	public Component.IPressable.Tooltip? GetTooltip( Component.IPressable.Event e )
	{
		if ( !_door.IsValid() )
		{
			return null;
		}

		string title = _door.IsOpen && _door.CanClose ? "Close" : "Open";
		return new Component.IPressable.Tooltip( title, "", "", true, this );
	}
}
