using HalfEdgeMesh;
using System;

namespace Causal;

/// <summary>
/// Drives one or more hanging wires: a rope joint per wire plus a runtime
/// tube render. The engine <see cref="CableComponent"/> mesh never recooks in
/// play, so only its static collision is used; the visible wire below is
/// rebuilt from the node positions. Two modes per link:
/// <list type="bullet">
/// <item>With a <c>HangBody</c>: hanger mode. The cable carries no collision
/// (a swinging prop must never hit the frozen authored tube) and the joint
/// anchors through a small auto-created static knob unless AnchorBody points
/// at something with a body already, hanging the authored prop.</item>
/// <item>Without a <c>HangBody</c>: dangling mode. The cable still hangs: a
/// tiny dynamic tip (Rigidbody + SphereCollider) is auto-created at the
/// HangNode end and the joint hangs that tip, so the wire sways on its own.</item>
/// </list>
/// Intended hierarchy (see the HangingLight setup in causal.scene):
/// <list type="bullet">
/// <item>A manager object (e.g. HangingLight) holds this component. It is pure
/// plumbing and may sit anywhere.</item>
/// <item>Each <see cref="CableLink"/> points at a Cable root placed at a ceiling
/// anchor, with Cable Node 1 at local origin (static top end) and Cable Node 2
/// as the driven bottom end.</item>
/// <item><c>HangBody</c> is the shared dynamic prop (e.g. LightFixture, needs a
/// <see cref="Rigidbody"/>). All links may hang the same body; leave it empty
/// and a tiny tip body is created per link so the wire still dangles.</item>
/// <item>Leave every object untagged ("world") so <see cref="TimeShiftManager"/>
/// never disables them across CAUSE/EFFECT shifts.</item>
/// </list>
/// </summary>
public sealed class HangingCable : Component
{
	/// <summary>
	/// One entry per wire. Fill in the editor: Cable and HangNode are required,
	/// HangBody decides the mode (with = hanging prop, without = static hull).
	/// </summary>
	public class CableLink
	{
		/// <summary>
		/// The Cable root at the ceiling anchor. Must have at least two
		/// CableNodeComponent children (Node 1 top, Node 2 bottom).
		/// </summary>
		[Property] public CableComponent Cable { get; set; }

		/// <summary>
		/// The bottom node child of <see cref="Cable"/> (e.g. Cable Node 2).
		/// Its authored placement IS the hang offset: on start it is converted
		/// to prop-local space and used for both the joint frame and the
		/// per-frame pin, then pinned back onto the prop every frame. Must be
		/// a direct child of the Cable object or the engine cable ignores it.
		/// </summary>
		[Property] public GameObject HangNode { get; set; }

		/// <summary>
		/// The dynamic prop being hung (e.g. LightFixture). Must carry a
		/// <see cref="Rigidbody"/>; its collider may be a <see cref="Collider"/>
		/// component or a <see cref="MeshComponent"/> with Hull/Mesh collision.
		/// This is also the physics side of the joint. Leave empty to hang
		/// the cable itself: a tiny dynamic tip is auto-created at the
		/// HangNode end and the joint sways that tip.
		/// </summary>
		[Property] public GameObject HangBody { get; set; }

		/// <summary>
		/// Static anchor side of the joint. Leave empty to use the Cable object,
		/// whose generated mesh carries static collision. A node (e.g. Cable
		/// Node 1) also works: like the engine, the body resolves through
		/// parents, so it lands on the Cable's static mesh body.
		/// </summary>
		[Property] public GameObject AnchorBody { get; set; }

		/// <summary>
		/// Extra joint length beyond the anchor-to-prop distance. Small positive
		/// values let the prop sag; total length never drops below 1.
		/// </summary>
		[Property] public float JointSlack { get; set; }

		/// <summary>
		/// Spring stiffness in Hz. Low values act like a rope (soft pull back
		/// past rest length); high values act like a rigid rod.
		/// </summary>
		[Property] public float JointFrequency { get; set; } = 2f;

		/// <summary>
		/// Spring damping ratio, usually 0 to 1. Settles swing without
		/// freezing it; 0 lets the prop oscillate freely.
		/// </summary>
		[Property] public float JointDamping { get; set; } = 1f;

		/// <summary>
		/// How the tip joint pushes and pulls when no HangBody is assigned.
		/// Both lets a shove up compress the cable with slack before the
		/// spring pushes back; Pull is rope-like (only resists stretch).
		/// Prop links always use Pull.
		/// </summary>
		[Property] public SpringJoint.SpringForceMode TipForceMode { get; set; } = SpringJoint.SpringForceMode.Both;

		/// <summary>
		/// Allowed upward travel for a tip-only cable before the joint hits
		/// its MinLength rod limit. Larger values mean more vertical slack
		/// when the tip is pushed up.
		/// </summary>
		[Property] public float TipCompression { get; set; } = 12f;

		/// <summary>
		/// Stiffness for tip-only cables, separate from <see cref="JointFrequency"/>.
		/// Lift is noticeably stronger than before.
		/// </summary>
		[Property] public float TipFrequency { get; set; } = 5f;

		/// <summary>
		/// Damping for tip-only cables, separate from <see cref="JointDamping"/>.
		/// </summary>
		[Property] public float TipDamping { get; set; } = 0.7f;

		/// <summary>
		/// Allowed downward stretch below rest for tip-only cables before
		/// hitting MaxLength. Gives the spring displacement needed for a
		/// strong upward rebound.
		/// </summary>
		[Property] public float TipDownTravel { get; set; } = 8f;

		/// <summary>
		/// Max prop spin speed in radians per second. Faster rotation is
		/// clamped instantly, so the body can sway but never whir. Zero
		/// locks rotation entirely.
		/// </summary>
		[Property] public float HangMaxSpin { get; set; } = 3f;

		/// <summary>
		/// Exponential spin damping rate per second. Heavy values kill
		/// rotation fast while leaving position swing to the joint.
		/// </summary>
		[Property] public float HangSpinDamping { get; set; } = 6f;

		/// <summary>
		/// Exponential correction rate per second pulling the prop back to
		/// its startup orientation. Counters tumble; gentle values let it
		/// lean with the swing.
		/// </summary>
		[Property] public float HangRestCorrection { get; set; } = 1.5f;

		/// <summary>
		/// Number of simulated points along the rope, including the two
		/// endpoints. Higher values give a smoother bend but cost more.
		/// Set to 2 to disable interior simulation (straight wire).
		/// </summary>
		[Property, Range( 2, 32 )] public int RopeSegments { get; set; } = 12;

		/// <summary>
		/// Constraint iterations per physics step. Higher values make the
		/// rope stiffer and less stretchy.
		/// </summary>
		[Property, Range( 1, 20 )] public int RopeIterations { get; set; } = 12;

		/// <summary>
		/// Downward acceleration for the rope interior. Produces a subtle
		/// catenary so the wire is not perfectly straight at rest.
		/// </summary>
		[Property] public float RopeGravity { get; set; } = 350f;

		/// <summary>
		/// Velocity damping for interior rope points per step (0 = no
		/// damping, 1 = frozen). Small values keep whip from ringing.
		/// </summary>
		[Property, Range( 0f, 0.5f )] public float RopeDamping { get; set; } = 0.03f;

		/// <summary>HangNode converted to HangBody-local space on start.</summary>
		[Hide] internal Vector3 HangOffset;

		/// <summary>Resolved anchor side, rebuilt every <see cref="OnStart"/>.</summary>
		[Hide] internal GameObject Anchor;
		/// <summary>Runtime joint holder, rebuilt every <see cref="OnStart"/>.</summary>
		[Hide] internal GameObject JointObject;
		/// <summary>Auto-created static anchor knob, if the anchor side needed one.</summary>
		[Hide] internal GameObject AnchorObject;
		/// <summary>Auto-created dynamic tip when no HangBody was assigned.</summary>
		[Hide] internal GameObject HangObject;

		/// <summary>Runtime wire render object, rebuilt every <see cref="OnStart"/>.</summary>
		[Hide] internal SceneObject WireObject;
		/// <summary>Wire path ends at the last render rebuild, for change detection.</summary>
		[Hide] internal Vector3 LastWireA;
		/// <summary>Wire path ends at the last render rebuild, for change detection.</summary>
		[Hide] internal Vector3 LastWireB;
		/// <summary>Throttles wire render rebuilds; settled wires cost nothing.</summary>
		[Hide] internal TimeSince TimeSinceWireBuild;
		/// <summary>Whether the wire has been rendered at least once.</summary>
		[Hide] internal bool WireBuilt;

		/// <summary>Prop world orientation at startup; the correction target.</summary>
		[Hide] internal Rotation HangRestRotation;

		/// <summary>Set once the link survives <see cref="OnStart"/> validation.</summary>
		[Hide] internal bool Started;

		/// <summary>Verlet rope points in world space, including endpoints.</summary>
		[Hide] internal List<Vector3> RopePoints;
		/// <summary>Previous positions for verlet integration.</summary>
		[Hide] internal List<Vector3> RopePrevPoints;
		/// <summary>Rest length between anchor and tip at spawn.</summary>
		[Hide] internal float RopeRestLength;
		/// <summary>True after the rope has been seeded.</summary>
		[Hide] internal bool RopeReady;
		/// <summary>Set when the rope moved enough to require a wire rebuild.</summary>
		[Hide] internal bool RopeDirty;
	}

	/// <summary>
	/// Every wire this component drives. Add one entry per Cable root.
	/// </summary>
	[Property] public List<CableLink> Cables { get; set; } = new();

	/// <summary>Minimum seconds between wire render rebuilds.</summary>
	private const float WireRebuildInterval = 1f / 30f;

	/// <summary>Bodies already stabilized this physics step (links may share one).</summary>
	private readonly List<GameObject> _stabilizedBodies = new();

	/// <summary>
	/// Validates every link, then builds each joint, snaps its hang node to
	/// the prop (or the auto tip) so the first frame has no visible pop,
	/// hides the engine cable render (frozen in play) and draws the first
	/// live wire frame.
	/// </summary>
	protected override void OnStart()
	{
		for ( int i = 0; i < Cables.Count; i++ )
		{
			var link = Cables[i];
			if ( link is null || !Validate( link, i ) )
			{
				continue;
			}

			EnsureCablePhysics( link );
			HideEngineCableMesh( link );
			EnsureAnchorBody( link );
			EnsureHangTip( link );
			BuildJoint( link );
			PinHangNode( link, true );
			InitializeRope( link );
			UpdateWire( link, true );
			var hang = GetEffectiveHangBody( link );
			if ( hang.IsValid() )
			{
				link.HangRestRotation = hang.WorldRotation;
			}

			link.Started = true;
		}
	}

	/// <summary>
	/// Re-pins every hang node to its prop and refreshes wires that moved.
	/// Cheap when settled: the epsilon checks skip both the node write and
	/// the mesh rebuild.
	/// </summary>
	protected override void OnUpdate()
	{
		foreach ( var link in Cables )
		{
			PinHangNode( link, false );
			UpdateWire( link, false );
		}
	}

	/// <summary>
	/// Clamps and damps prop spin and eases orientation back to rest, once
	/// per body per step. Runs in the physics step so velocity edits stick.
	/// Also advances the interior rope simulation before the render pass.
	/// </summary>
	protected override void OnFixedUpdate()
	{
		_stabilizedBodies.Clear();
		foreach ( var link in Cables )
		{
			StabilizeHangBody( link );
			UpdateRope( link );
		}
	}

	protected override void OnDisabled()
	{
		ClearJoints();
		ClearWires();
	}

	protected override void OnDestroy()
	{
		ClearJoints();
		ClearWires();
	}

	/// <summary>
	/// Checks one link's references. Missing Cable/HangNode fails the link.
	/// Missing HangBody is allowed: a tiny dynamic tip will be auto-created
	/// and hung instead. A Rigidbody-less prop fails the link in hanger mode;
	/// a bodiless anchor or misparented node only warns, since a small static
	/// knob can be auto-created.
	/// </summary>
	private bool Validate( CableLink link, int index )
	{
		if ( !link.Cable.IsValid() )
		{
			Log.Warning( $"HangingCable on '{GameObject.Name}' cable #{index} needs a CableComponent." );
			return false;
		}

		if ( !link.HangNode.IsValid() )
		{
			Log.Warning( $"HangingCable on '{GameObject.Name}' cable #{index} needs a HangNode." );
			return false;
		}

		if ( link.HangNode.Parent != link.Cable.GameObject )
		{
			Log.Warning( $"HangingCable on '{GameObject.Name}' cable #{index} needs HangNode as a direct child of the Cable object." );
		}

		link.Anchor = link.AnchorBody.IsValid() ? link.AnchorBody : link.Cable.GameObject;

		if ( !link.HangBody.IsValid() )
		{
			if ( !HasJointBody( link.Anchor, false ) )
			{
				Log.Warning( $"HangingCable on '{GameObject.Name}' cable #{index} anchor '{link.Anchor.Name}' has no physics body yet; chain: {DescribeBodyChain( link.Anchor )} - a small static knob will be created." );
			}

			return true;
		}

		if ( !HasJointBody( link.HangBody, true ) )
		{
			Log.Warning( $"HangingCable on '{GameObject.Name}' cable #{index} needs a Rigidbody on or above HangBody '{link.HangBody.Name}'." );
			return false;
		}

		if ( !HasCollisionShapeOnSelfOrAncestors( link.HangBody ) )
		{
			Log.Warning( $"HangingCable on '{GameObject.Name}' cable #{index} HangBody '{link.HangBody.Name}' has no collision; give it a Collider or a MeshComponent with Hull/Mesh collision." );
		}

		if ( !HasJointBody( link.Anchor, false ) )
		{
			Log.Warning( $"HangingCable on '{GameObject.Name}' cable #{index} anchor '{link.Anchor.Name}' has no physics body yet; chain: {DescribeBodyChain( link.Anchor )} - a small static knob will be created." );
		}

		return true;
	}

	/// <summary>
	/// Mirrors how the engine resolves joint bodies (<c>Joint.FindPhysicsBody</c>):
	/// Rigidbody first, then collision, climbing parents. A node like Cable
	/// Node 1 is therefore a valid anchor: it resolves through the Cable
	/// object's static mesh collision.
	/// </summary>
	private static bool HasJointBody( GameObject go, bool requireRigidbody )
	{
		for ( var current = go; current.IsValid(); current = current.Parent )
		{
			if ( current.GetComponent<Rigidbody>().IsValid() )
			{
				return true;
			}

			if ( !requireRigidbody && HasCollisionShape( current ) )
			{
				return true;
			}
		}

		return false;
	}

	private static bool HasCollisionShapeOnSelfOrAncestors( GameObject go )
	{
		for ( var current = go; current.IsValid(); current = current.Parent )
		{
			if ( HasCollisionShape( current ) )
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// True when the object provides a physics shape: any enabled
	/// <see cref="Collider"/>, including a <see cref="MeshComponent"/> whose
	/// <see cref="MeshComponent.Collision"/> is Hull or Mesh.
	/// </summary>
	private static bool HasCollisionShape( GameObject go )
	{
		foreach ( var collider in go.GetComponents<Collider>() )
		{
			if ( !collider.IsValid() || !collider.Enabled )
			{
				continue;
			}

			if ( collider is MeshComponent mesh && mesh.Collision == MeshComponent.CollisionType.None )
			{
				continue;
			}

			return true;
		}

		return false;
	}

	/// <summary>
	/// One-line dump of the anchor resolution chain for the warning above:
	/// each self-or-ancestor object with its colliders, so a false positive
	/// names exactly what the check saw.
	/// </summary>
	private static string DescribeBodyChain( GameObject go )
	{
		string chain = "";
		for ( var current = go; current.IsValid(); current = current.Parent )
		{
			string shapes = "";
			foreach ( var collider in current.GetComponents<Collider>() )
			{
				if ( !collider.IsValid() )
				{
					continue;
				}

				string detail = collider.GetType().Name + (collider.Enabled ? "" : ":disabled");
				if ( collider is MeshComponent mesh )
				{
					detail += $"(collision={mesh.Collision})";
				}

				shapes += (shapes.Length > 0 ? "," : "") + detail;
			}

			string rigid = current.GetComponent<Rigidbody>().IsValid() ? "+rigidbody" : "";
			chain += (chain.Length > 0 ? " < " : "") + $"'{current.Name}'[{shapes}{rigid}]";
		}

		return chain;
	}

	private static GameObject GetEffectiveHangBody( CableLink link )
	{
		if ( link is null )
		{
			return null;
		}

		if ( link.HangObject.IsValid() )
		{
			return link.HangObject;
		}

		return link.HangBody;
	}

	/// <summary>
	/// Creates the rope-style <see cref="SpringJoint"/> for one link: a soft
	/// spring sized from the anchor to the hang attach point plus
	/// <c>JointSlack</c>. Prop links are pull-only (a wire never pushes);
	/// tip-only cables use <see cref="CableLink.TipForceMode"/> (default
	/// Both) with <see cref="CableLink.TipCompression"/> slack so a shove up
	/// compresses and rebounds while the cable keeps its stiff length.
	/// Frames are explicit (<see cref="Joint.AttachmentMode.LocalFrames"/>)
	/// so the joint pulls the hang body at the hang node, not at its origin.
	/// </summary>
	private void BuildJoint( CableLink link )
	{
		var hang = GetEffectiveHangBody( link );
		link.HangOffset = hang.WorldTransform.PointToLocal( link.HangNode.WorldPosition );

		var anchorWorld = link.Cable.GameObject.WorldPosition;
		var attach = hang.WorldTransform.PointToWorld( link.HangOffset );
		float len = anchorWorld.Distance( attach ) + link.JointSlack;
		if ( len < 1f )
		{
			len = 1f;
		}

		bool isTip = !link.HangBody.IsValid();
		link.JointObject = new GameObject( "CableJoint" );
		link.JointObject.Parent = link.Cable.GameObject;
		link.JointObject.LocalPosition = Vector3.Zero;

		var joint = link.JointObject.AddComponent<SpringJoint>();
		joint.Attachment = Joint.AttachmentMode.LocalFrames;
		joint.LocalFrame1 = new Transform( link.Anchor.WorldTransform.PointToLocal( anchorWorld ) );
		joint.LocalFrame2 = new Transform( link.HangOffset );
		joint.AnchorBody = link.Anchor;
		joint.Body = hang;
		joint.ForceMode = isTip ? link.TipForceMode : SpringJoint.SpringForceMode.Pull;
		joint.MinLength = isTip ? Math.Max( 0f, len - Math.Max( 0f, link.TipCompression ) ) : 0f;
		joint.MaxLength = isTip ? len + Math.Max( 0f, link.TipDownTravel ) : len;
		joint.RestLength = len;
		joint.Frequency = isTip ? link.TipFrequency : link.JointFrequency;
		joint.Damping = isTip ? link.TipDamping : link.JointDamping;
		joint.EnableCollision = true;
	}

	/// <summary>
	/// Moves the link's hang node onto the hang body's attach point. Skips the
	/// write when already there (<paramref name="force"/> bypasses the check
	/// for the initial snap in <see cref="OnStart"/>).
	/// </summary>
	private static void PinHangNode( CableLink link, bool force )
	{
		if ( link is null || !link.HangNode.IsValid() )
		{
			return;
		}

		var hang = GetEffectiveHangBody( link );
		if ( !hang.IsValid() )
		{
			return;
		}

		var target = hang.WorldTransform.PointToWorld( link.HangOffset );
		if ( !force && link.HangNode.WorldPosition.AlmostEqual( target ) )
		{
			return;
		}

		link.HangNode.WorldPosition = target;
	}

	/// <summary>
	/// Keeps one link's hang body from spinning out: hard-clamps spin speed,
	/// then exponentially damps what remains, then eases orientation toward
	/// the startup pose for prop links. Skips links that never started and
	/// bodies already handled this step. Auto tips keep spin damping but
	/// skip rotation correction so vertical push slack is not fought.
	/// </summary>
	private void StabilizeHangBody( CableLink link )
	{
		if ( link is null || !link.Started )
		{
			return;
		}

		var hang = GetEffectiveHangBody( link );
		if ( !hang.IsValid() )
		{
			return;
		}

		if ( _stabilizedBodies.Contains( hang ) )
		{
			return;
		}

		_stabilizedBodies.Add( hang );
		var rigid = hang.GetComponent<Rigidbody>();
		if ( !rigid.IsValid() )
		{
			return;
		}

		float delta = Time.Delta;
		Vector3 spin = rigid.AngularVelocity;
		float speed = spin.Length;
		if ( speed > link.HangMaxSpin && speed > 0.0001f )
		{
			spin *= link.HangMaxSpin / speed;
		}

		spin *= MathF.Exp( -link.HangSpinDamping * delta );
		rigid.AngularVelocity = spin;

		bool isTip = link.HangObject.IsValid();
		if ( isTip )
		{
			return;
		}

		float correction = 1f - MathF.Exp( -link.HangRestCorrection * delta );
		if ( correction > 0f )
		{
			hang.WorldRotation = Rotation.Slerp( hang.WorldRotation, link.HangRestRotation, correction );
		}
	}

	/// <summary>
	/// The cable never provides collision: in hanger mode the prop must never
	/// hit the frozen authored tube, and in dangling mode the tiny tip is
	/// the only physics shape. Disables the Cable's <see cref="MeshComponent"/>
	/// collision every start.
	/// </summary>
	private static void EnsureCablePhysics( CableLink link )
	{
		var mesh = link.Cable.GameObject.GetComponent<MeshComponent>();
		if ( !mesh.IsValid() )
		{
			return;
		}

		if ( mesh.Collision != MeshComponent.CollisionType.None )
		{
			mesh.Collision = MeshComponent.CollisionType.None;
		}
	}

	/// <summary>
	/// Guarantees a static body for the hanger joint. The Cable's own mesh
	/// has no collision, so an explicit anchor object would otherwise be
	/// bodiless. Creates a tiny static sphere knob at the ceiling anchor when
	/// the resolved anchor has no body and points the joint at it.
	/// </summary>
	private void EnsureAnchorBody( CableLink link )
	{
		if ( HasJointBody( link.Anchor, false ) )
		{
			return;
		}

		if ( link.AnchorObject.IsValid() )
		{
			link.Anchor = link.AnchorObject;
			return;
		}

		var anchorWorld = link.Cable.GameObject.WorldPosition;
		link.AnchorObject = new GameObject( "CableAnchor" );
		link.AnchorObject.Parent = link.Cable.GameObject;
		link.AnchorObject.WorldPosition = anchorWorld;
		var sphere = link.AnchorObject.AddComponent<SphereCollider>();
		sphere.Radius = 2f;
		link.Anchor = link.AnchorObject;
	}

	/// <summary>
	/// When no <see cref="CableLink.HangBody"/> was assigned, the cable still
	/// hangs: auto-creates a tiny dynamic tip at the HangNode end so the joint
	/// has something to sway. The tip is a small Rigidbody + SphereCollider
	/// with heavy damping; the visual wire is pinned to it every frame just
	/// like the prop case.
	/// </summary>
	private void EnsureHangTip( CableLink link )
	{
		if ( link.HangBody.IsValid() || link.HangObject.IsValid() )
		{
			return;
		}

		var tipWorld = link.HangNode.WorldPosition;
		link.HangObject = new GameObject( "CableTip" );
		link.HangObject.Parent = GameObject;
		link.HangObject.WorldPosition = tipWorld;
		var body = link.HangObject.AddComponent<Rigidbody>();
		body.MassOverride = 0.8f;
		body.LinearDamping = 0.05f;
		body.AngularDamping = 2f;
		var tip = link.HangObject.AddComponent<SphereCollider>();
		float size = link.Cable.IsValid() ? link.Cable.Size : 1f;
		tip.Radius = Math.Max( 1.5f, size * 1.2f );
	}

	private static Vector3 GetRopeAnchor( CableLink link )
	{
		if ( !link.Cable.IsValid() )
		{
			return Vector3.Zero;
		}

		foreach ( var child in link.Cable.GameObject.Children )
		{
			if ( child.GetComponent<CableNodeComponent>() is not null )
			{
				return child.WorldPosition;
			}
		}

		return link.Cable.GameObject.WorldPosition;
	}

	private static Vector3 GetRopeEnd( CableLink link )
	{
		var hang = GetEffectiveHangBody( link );
		if ( hang.IsValid() )
		{
			return hang.WorldTransform.PointToWorld( link.HangOffset );
		}

		if ( link.HangNode.IsValid() )
		{
			return link.HangNode.WorldPosition;
		}

		return GetRopeAnchor( link );
	}

	private static void InitializeRope( CableLink link )
	{
		if ( link is null || !link.Cable.IsValid() )
		{
			return;
		}

		int segments = Math.Clamp( link.RopeSegments, 2, 32 );
		if ( segments < 3 )
		{
			link.RopePoints = null;
			link.RopePrevPoints = null;
			link.RopeReady = false;
			link.RopeDirty = false;
			link.RopeRestLength = 0f;
			return;
		}

		var anchor = GetRopeAnchor( link );
		var end = GetRopeEnd( link );
		float dist = anchor.Distance( end );
		if ( dist < 1f )
		{
			dist = Math.Max( 1f, (anchor - end).Length + 1f );
		}

		link.RopeRestLength = dist;
		link.RopePoints = new List<Vector3>( segments );
		link.RopePrevPoints = new List<Vector3>( segments );
		float sagBase = link.RopeGravity > 0f ? Math.Min( 6f, dist * 0.05f ) : 0f;
		for ( int i = 0; i < segments; i++ )
		{
			float t = i / (float)(segments - 1);
			var pos = Vector3.Lerp( anchor, end, t );
			float sag = MathF.Sin( t * MathF.PI ) * sagBase;
			pos += Vector3.Down * sag;
			link.RopePoints.Add( pos );
			link.RopePrevPoints.Add( pos );
		}

		link.RopeReady = true;
		link.RopeDirty = true;
	}

	private void UpdateRope( CableLink link )
	{
		if ( link is null || !link.Started || !link.RopeReady || link.RopePoints is null || link.RopePrevPoints is null )
		{
			return;
		}

		int count = link.RopePoints.Count;
		if ( count < 3 )
		{
			return;
		}

		var anchor = GetRopeAnchor( link );
		var end = GetRopeEnd( link );
		float delta = Time.Delta;
		if ( delta <= 0f )
		{
			return;
		}

		if ( link.RopeSegments != count )
		{
			InitializeRope( link );
			return;
		}

		float currentDist = anchor.Distance( end );
		if ( link.RopeRestLength < 0.1f )
		{
			link.RopeRestLength = Math.Max( 1f, currentDist );
		}

		float targetTotal = Math.Max( link.RopeRestLength, currentDist );
		float segLen = targetTotal / (count - 1);

		Vector3 prevEndPos = link.RopePoints[count - 1];
		link.RopePoints[0] = anchor;
		link.RopePrevPoints[0] = anchor;
		link.RopePoints[count - 1] = end;
		link.RopePrevPoints[count - 1] = prevEndPos;

		float damping = Math.Clamp( link.RopeDamping, 0f, 0.5f );
		float gravity = link.RopeGravity;
		Vector3 gravityStep = Vector3.Down * gravity * delta * delta;
		bool moved = false;
		for ( int i = 1; i < count - 1; i++ )
		{
			var pos = link.RopePoints[i];
			var prev = link.RopePrevPoints[i];
			var vel = (pos - prev) * (1f - damping);
			var next = pos + vel + gravityStep;
			if ( (next - pos).LengthSquared > 0.000001f )
			{
				moved = true;
			}

			link.RopePrevPoints[i] = pos;
			link.RopePoints[i] = next;
		}

		int iters = Math.Clamp( link.RopeIterations, 1, 20 );
		for ( int iter = 0; iter < iters; iter++ )
		{
			for ( int i = 0; i < count - 1; i++ )
			{
				var p1 = link.RopePoints[i];
				var p2 = link.RopePoints[i + 1];
				var d = p2 - p1;
				float len = d.Length;
				if ( len < 0.001f )
				{
					continue;
				}

				float diff = (len - segLen) / len;
				bool pin1 = i == 0;
				bool pin2 = i + 1 == count - 1;
				if ( pin1 && pin2 )
				{
					var c = d * diff * 0.5f;
					link.RopePoints[i] += c;
					link.RopePoints[i + 1] -= c;
				}
				else if ( pin1 )
				{
					link.RopePoints[i + 1] -= d * diff;
				}
				else if ( pin2 )
				{
					link.RopePoints[i] += d * diff;
				}
				else
				{
					var c = d * diff * 0.5f;
					link.RopePoints[i] += c;
					link.RopePoints[i + 1] -= c;
				}
			}

			link.RopePoints[0] = anchor;
			link.RopePoints[count - 1] = end;
		}

		if ( moved )
		{
			link.RopeDirty = true;
		}
		else
		{
			float maxSq = 0f;
			for ( int i = 1; i < count - 1; i++ )
			{
				var v = link.RopePoints[i] - link.RopePrevPoints[i];
				float sq = v.LengthSquared;
				if ( sq > maxSq )
				{
					maxSq = sq;
				}
			}

			link.RopeDirty = maxSq > 0.0004f;
		}
	}

	/// <summary>
	/// Destroys every runtime-built joint, anchor knob and auto tip. The
	/// scene-authored Cable, nodes and prop are left untouched.
	/// </summary>
	private void ClearJoints()
	{
		foreach ( var link in Cables )
		{
			if ( link is null )
			{
				continue;
			}

			if ( link.JointObject.IsValid() )
			{
				link.JointObject.Destroy();
				link.JointObject = null;
			}

			if ( link.AnchorObject.IsValid() )
			{
				link.AnchorObject.Destroy();
				link.AnchorObject = null;
			}

			if ( link.HangObject.IsValid() )
			{
				link.HangObject.Destroy();
				link.HangObject = null;
			}

			link.RopePoints = null;
			link.RopePrevPoints = null;
			link.RopeReady = false;
			link.RopeDirty = false;
			link.RopeRestLength = 0f;
		}
	}

	/// <summary>
	/// Deletes every runtime-built wire render object.
	/// </summary>
	private void ClearWires()
	{
		foreach ( var link in Cables )
		{
			if ( link is null || !link.WireObject.IsValid() )
			{
				continue;
			}

			link.WireObject.Delete();
			link.WireObject = null;
			link.WireBuilt = false;
		}
	}

	/// <summary>
	/// Hides the engine cable's own render mesh. Its <see cref="MeshComponent"/>
	/// never recooks in play (editor-gated), so it would sit frozen at the
	/// authored pose while the live wire below tracks the prop. Render-only:
	/// collision is managed separately by <see cref="EnsureCablePhysics"/>.
	/// </summary>
	private static void HideEngineCableMesh( CableLink link )
	{
		var mesh = link.Cable.GameObject.GetComponent<MeshComponent>();
		if ( mesh.IsValid() && !mesh.HideInGame )
		{
			mesh.HideInGame = true;
		}
	}

	/// <summary>
	/// Rebuilds the link's wire render when its ends moved or the rope
	/// interior swayed. Skips the rebuild while settled and throttles to
	/// <see cref="WireRebuildInterval"/> mid-swing.
	/// </summary>
	private void UpdateWire( CableLink link, bool force )
	{
		if ( link is null || !link.Cable.IsValid() || !link.HangNode.IsValid() )
		{
			return;
		}

		Vector3 a = GetRopeAnchor( link );
		Vector3 b = GetRopeEnd( link );
		bool ropeActive = link.RopeReady && link.RopePoints is not null && link.RopePoints.Count >= 3;
		bool endpointMoved = !link.WireBuilt || !link.LastWireA.AlmostEqual( a ) || !link.LastWireB.AlmostEqual( b );
		if ( !force && link.WireBuilt && !endpointMoved && !(ropeActive && link.RopeDirty) )
		{
			return;
		}

		if ( !force && link.WireBuilt && link.TimeSinceWireBuild < WireRebuildInterval )
		{
			return;
		}

		var polygon = BuildWireMesh( link );
		if ( polygon is null )
		{
			return;
		}

		var model = polygon.Rebuild();
		if ( model.MeshCount == 0 )
		{
			return;
		}

		if ( !link.WireObject.IsValid() )
		{
			link.WireObject = new SceneObject( Scene.SceneWorld, model, new Transform( Vector3.Zero, Rotation.Identity, 1f ) );
		}
		else
		{
			link.WireObject.Model = model;
		}

		link.LastWireA = a;
		link.LastWireB = b;
		link.TimeSinceWireBuild = 0;
		link.WireBuilt = true;
		link.RopeDirty = false;
	}

	/// <summary>One tube path sample in world space.</summary>
	private struct WireSample
	{
		public Vector3 Position;
		public float RadiusScale;
		public float Roll;
	}

	/// <summary>
	/// Builds the wire tube in world space. When the verlet rope is active,
	/// the rope points drive the tube directly (including gravity sag and
	/// whip); otherwise it falls back to the engine-style node path with
	/// Catmull-Rom and fake slack.
	/// </summary>
	private static PolygonMesh BuildWireMesh( CableLink link )
	{
		var cable = link.Cable;
		List<WireSample> samples;
		float slack = cable.Slack;
		if ( link.RopeReady && link.RopePoints is not null && link.RopePoints.Count >= 2 )
		{
			samples = new List<WireSample>( link.RopePoints.Count );
			foreach ( var p in link.RopePoints )
			{
				samples.Add( new WireSample { Position = p, RadiusScale = 1f, Roll = 0f } );
			}

			slack = 0f;
		}
		else
		{
			samples = new List<WireSample>();
			foreach ( var child in cable.GameObject.Children )
			{
				var node = child.GetComponent<CableNodeComponent>();
				if ( node is null )
				{
					continue;
				}

				samples.Add( new WireSample { Position = child.WorldPosition, RadiusScale = node.RadiusScale, Roll = node.Roll } );
			}

			Vector3 hangPos = link.HangNode.WorldPosition;
			bool hangSampled = false;
			foreach ( var sample in samples )
			{
				if ( sample.Position.AlmostEqual( hangPos ) )
				{
					hangSampled = true;
					break;
				}
			}

			if ( !hangSampled )
			{
				samples.Add( new WireSample { Position = hangPos, RadiusScale = 1f, Roll = 0f } );
			}

			if ( samples.Count < 2 )
			{
				return null;
			}
		}

		if ( samples.Count < 2 )
		{
			return null;
		}

		var path = BuildWirePath( samples, cable.PathDetail, slack );
		if ( path.Count < 2 )
		{
			return null;
		}

		int sides = Math.Max( 3, cable.Subdivisions );
		float radius = Math.Max( 0.1f, cable.Size );
		var material = cable.Material;
		var mesh = new PolygonMesh();
		var firstRing = new Vector3[sides];
		var lastRing = new Vector3[sides];
		VertexHandle[] prevHandles = null;
		float prevU = 0f;
		var tangent = (path[1].Position - path[0].Position).Normal;
		var normal = BuildInitialNormal( tangent );
		float length = 0f;

		for ( int i = 0; i < path.Count; i++ )
		{
			var point = path[i].Position;
			if ( i > 0 )
			{
				length += point.Distance( path[i - 1].Position );
			}

			tangent = BuildTangent( path, i );
			normal = BuildNormalFromPrevious( tangent, normal );
			normal = Rotation.FromAxis( tangent, path[i].Roll ) * normal;
			var bitangent = tangent.Cross( normal ).Normal;
			float nodeRadius = radius * Math.Max( 0.01f, path[i].RadiusScale );
			float u = length * cable.TextureScale + cable.TextureOffsetAlongPath;
			var ring = new Vector3[sides];
			for ( int j = 0; j < sides; j++ )
			{
				float angle = (MathF.PI * 2f * j) / sides;
				ring[j] = point + (normal * MathF.Cos( angle ) + bitangent * MathF.Sin( angle )) * nodeRadius;
			}

			var handles = mesh.AddVertices( ring );
			if ( prevHandles is not null )
			{
				for ( int j = 0; j < sides; j++ )
				{
					int next = (j + 1) % sides;
					var face = mesh.AddFace( prevHandles[j], prevHandles[next], handles[next], handles[j] );
					mesh.SetFaceMaterial( face, material );
					mesh.SetFaceTextureCoords( face, [BuildWireUv( prevU, j, sides, cable ), BuildWireUv( prevU, j + 1, sides, cable ), BuildWireUv( u, j + 1, sides, cable ), BuildWireUv( u, j, sides, cable )] );
				}
			}

			prevHandles = handles;
			prevU = u;
			if ( i == 0 )
			{
				firstRing = ring;
			}

			lastRing = ring;
		}

		if ( cable.CapEnds )
		{
			var startCap = new Vector3[sides];
			for ( int i = 0; i < sides; i++ )
			{
				startCap[i] = firstRing[sides - 1 - i];
			}

			var startFace = mesh.AddFace( mesh.AddVertices( startCap ) );
			mesh.SetFaceMaterial( startFace, material );
			var endFace = mesh.AddFace( mesh.AddVertices( lastRing ) );
			mesh.SetFaceMaterial( endFace, material );
		}

		mesh.SetSmoothingAngle( 180f );
		return mesh;
	}

	/// <summary>
	/// Length-based cord UV. The side index is intentionally unwrapped so the
	/// seam (side == sides) lands exactly one repeat past side zero.
	/// </summary>
	private static Vector2 BuildWireUv( float u, int side, int sides, CableComponent cable )
	{
		float v = side / (float)sides * cable.TextureRepeatsCircumference + cable.TextureOffsetCircumference;
		return cable.TextureOrientation == CableComponent.CableTextureOrientation.Vertical ? new Vector2( v, u ) : new Vector2( u, v );
	}

	/// <summary>
	/// Subdivides control samples into a smooth path, applying the engine's
	/// fake-slack sag. Ports <c>CableComponent.BuildPathPoints</c>.
	/// </summary>
	private static List<WireSample> BuildWirePath( List<WireSample> controlPoints, int pathDetail, float slack )
	{
		var path = new List<WireSample>();
		if ( controlPoints.Count <= 1 )
		{
			path.AddRange( controlPoints );
			return path;
		}

		if ( pathDetail <= 0 && MathF.Abs( slack ) <= 0.0001f )
		{
			path.AddRange( controlPoints );
			return path;
		}

		int minSteps = MathF.Abs( slack ) > 0.0001f ? 2 : 1;
		int steps = Math.Max( minSteps, pathDetail + 1 );
		for ( int i = 0; i < controlPoints.Count - 1; i++ )
		{
			var p0 = controlPoints[Math.Max( i - 1, 0 )];
			var p1 = controlPoints[i];
			var p2 = controlPoints[i + 1];
			var p3 = controlPoints[Math.Min( i + 2, controlPoints.Count - 1 )];
			for ( int s = 0; s < steps; s++ )
			{
				float t = s / (float)steps;
				float sag = 4f * t * (1f - t);
				path.Add( new WireSample
				{
					Position = CatmullRom( p0.Position, p1.Position, p2.Position, p3.Position, t ) + Vector3.Down * (sag * slack),
					RadiusScale = CatmullRom( p0.RadiusScale, p1.RadiusScale, p2.RadiusScale, p3.RadiusScale, t ),
					Roll = CatmullRom( p0.Roll, p1.Roll, p2.Roll, p3.Roll, t )
				} );
			}
		}

		path.Add( controlPoints[^1] );
		return path;
	}

	private static Vector3 CatmullRom( Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t )
	{
		float t2 = t * t;
		float t3 = t2 * t;
		return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
	}

	private static float CatmullRom( float p0, float p1, float p2, float p3, float t )
	{
		float t2 = t * t;
		float t3 = t2 * t;
		return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
	}

	private static Vector3 BuildTangent( List<WireSample> points, int i )
	{
		if ( points.Count < 2 )
		{
			return Vector3.Forward;
		}

		if ( i == 0 )
		{
			return (points[1].Position - points[0].Position).Normal;
		}

		if ( i == points.Count - 1 )
		{
			return (points[^1].Position - points[^2].Position).Normal;
		}

		var tangent = ((points[i].Position - points[i - 1].Position).Normal + (points[i + 1].Position - points[i].Position).Normal).Normal;
		return tangent.LengthSquared > 0.0001f ? tangent : (points[i + 1].Position - points[i].Position).Normal;
	}

	private static Vector3 BuildInitialNormal( Vector3 tangent )
	{
		var up = MathF.Abs( tangent.Dot( Vector3.Up ) ) > 0.98f ? Vector3.Right : Vector3.Up;
		return tangent.Cross( up ).Normal;
	}

	private static Vector3 BuildNormalFromPrevious( Vector3 tangent, Vector3 previousNormal )
	{
		var projected = previousNormal - tangent * previousNormal.Dot( tangent );
		if ( projected.LengthSquared > 0.0001f )
		{
			return projected.Normal;
		}

		return BuildInitialNormal( tangent );
	}
}
