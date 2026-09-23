using System;
using System.Threading;
using System.Threading.Tasks;

namespace Causal;

public sealed class FirstPersonCosmetics : Component
{
	[Property] public SkinnedModelRenderer ArmsRenderer { get; set; }
	[Property] public SkinnedModelRenderer ClothingRenderer { get; set; }
	[Property] public ulong ClothingBodyGroups { get; set; } = 1088;
	[Property] public List<string> ExcludedCategories { get; set; } = new() { "Gloves" };
	[Property] public List<string> RemovedClothingNames { get; set; } = new() { "y_front_pants" };
	[Property] public float CosmeticSettleTimeout { get; set; } = 30f;

	public static bool IsLoadingClothing { get; private set; }

	private const ulong BareBodyGroups = 21;
	private const ulong ClothedBodyGroups = 20;
	private int _generation;
	private CancellationTokenSource _cts;

	protected override void OnStart()
	{
		if ( !ArmsRenderer.IsValid() || !ClothingRenderer.IsValid() )
		{
			Log.Warning( "FirstPersonCosmetics has no valid arms/clothing renderer, cosmetics disabled." );
			return;
		}

		if ( !CausalSettings.FirstPersonClothing )
		{
			SetArmsBodyGroups( BareBodyGroups );
			DisableClothingObject();
			return;
		}

		_cts = new CancellationTokenSource();
		int generation = ++_generation;
		_ = ApplyCosmeticsAsync( generation, _cts.Token );
	}

	protected override void OnDestroy()
	{
		_generation++;
		if ( _cts is not null )
		{
			_cts.Cancel();
			_cts.Dispose();
			_cts = null;
		}
	}

	public void Refresh()
	{
		if ( !ArmsRenderer.IsValid() || !ClothingRenderer.IsValid() )
		{
			return;
		}

		_generation++;
		if ( _cts is not null )
		{
			_cts.Cancel();
			_cts.Dispose();
			_cts = null;
		}

		if ( !CausalSettings.FirstPersonClothing )
		{
			IsLoadingClothing = false;
			SetArmsBodyGroups( BareBodyGroups );
			DisableClothingObject();
			return;
		}

		var clothingObject = ClothingRenderer.GameObject;
		if ( clothingObject.IsValid() )
		{
			clothingObject.Enabled = true;
		}

		_cts = new CancellationTokenSource();
		int generation = ++_generation;
		_ = ApplyCosmeticsAsync( generation, _cts.Token );
	}

	private void DisableClothingObject()
	{
		if ( !ClothingRenderer.IsValid() )
		{
			return;
		}

		var clothingObject = ClothingRenderer.GameObject;
		if ( clothingObject.IsValid() )
		{
			clothingObject.Enabled = false;
		}
	}

	private async Task ApplyCosmeticsAsync( int generation, CancellationToken token )
	{
		try
		{
			var container = ClothingContainer.CreateFromLocalUser();
			if ( !IsCurrent( generation, token ) )
			{
				return;
			}

			StripExcludedCategories( container );

			IsLoadingClothing = true;

			await container.ApplyAsync( ClothingRenderer, token );
			if ( !IsCurrent( generation, token ) )
			{
				return;
			}

			await WaitForSettled( container, generation, token );
			if ( !IsCurrent( generation, token ) )
			{
				return;
			}

			SetLoadingForGeneration( generation, false );

			if ( StripExcludedCategories( container ) > 0 )
			{
				container.Apply( ClothingRenderer );
			}

			RemoveNamedClothingObjects();
			ApplyClothingRenderOptions();
			ApplyClothingBodyGroups();
			SetArmsBodyGroups( CountEntries( container ) > 0 ? ClothedBodyGroups : BareBodyGroups );
		}
		catch ( OperationCanceledException )
		{
			SetLoadingForGeneration( generation, false );
			SetArmsBodyGroupsForGeneration( generation, BareBodyGroups );
		}
		catch ( Exception exception )
		{
			SetLoadingForGeneration( generation, false );
			SetArmsBodyGroupsForGeneration( generation, BareBodyGroups );
			Log.Warning( $"FirstPersonCosmetics failed: {exception.Message}" );
		}
	}

	private bool IsCurrent( int generation, CancellationToken token )
	{
		return generation == _generation && !token.IsCancellationRequested;
	}

	private void SetArmsBodyGroupsForGeneration( int generation, ulong groups )
	{
		if ( generation == _generation )
		{
			SetArmsBodyGroups( groups );
		}
	}

	private void SetArmsBodyGroups( ulong groups )
	{
		if ( ArmsRenderer.IsValid() )
		{
			ArmsRenderer.BodyGroups = groups;
		}
	}

	private void ApplyClothingBodyGroups()
	{
		if ( ClothingRenderer.IsValid() )
		{
			ClothingRenderer.BodyGroups = ClothingBodyGroups;
		}
	}

	private void SetLoadingForGeneration( int generation, bool loading )
	{
		if ( generation == _generation )
		{
			IsLoadingClothing = loading;
		}
	}

	private void ApplyClothingRenderOptions()
	{
		if ( !ClothingRenderer.IsValid() )
		{
			return;
		}

		ApplyClothingRenderOptions( ClothingRenderer.GameObject );
	}

	private static void ApplyClothingRenderOptions( GameObject root )
	{
		if ( !root.IsValid() || root.IsDestroyed )
		{
			return;
		}

		var renderer = root.GetComponent<SkinnedModelRenderer>();
		if ( renderer.IsValid() )
		{
			renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
			var options = renderer.RenderOptions;
			options.Game = false;
			options.Overlay = true;
			options.Bloom = false;
			options.AfterUI = false;
		}

		foreach ( GameObject child in root.Children )
		{
			ApplyClothingRenderOptions( child );
		}
	}

	private void RemoveNamedClothingObjects()
	{
		if ( RemovedClothingNames is null || RemovedClothingNames.Count == 0 )
		{
			return;
		}

		if ( !ClothingRenderer.IsValid() )
		{
			return;
		}

		int removed = 0;
		RemoveNamedClothingObjects( ClothingRenderer.GameObject, ref removed );
		if ( removed > 0 )
		{
			Log.Info( $"FirstPersonCosmetics removed {removed} unwanted clothing objects." );
		}
	}

	private void RemoveNamedClothingObjects( GameObject root, ref int removed )
	{
		if ( !root.IsValid() || root.IsDestroyed )
		{
			return;
		}

		foreach ( GameObject child in root.Children )
		{
			if ( !child.IsValid() || child.IsDestroyed )
			{
				continue;
			}

			if ( IsRemovedClothingName( child.Name ) )
			{
				child.Destroy();
				removed++;
				continue;
			}

			RemoveNamedClothingObjects( child, ref removed );
		}
	}

	private bool IsRemovedClothingName( string name )
	{
		if ( string.IsNullOrEmpty( name ) || RemovedClothingNames is null )
		{
			return false;
		}

		foreach ( string removed in RemovedClothingNames )
		{
			if ( !string.IsNullOrEmpty( removed ) && name.IndexOf( removed, StringComparison.OrdinalIgnoreCase ) >= 0 )
			{
				return true;
			}
		}

		return false;
	}

	private static int CountEntries( ClothingContainer container )
	{
		if ( container?.Clothing is null )
		{
			return 0;
		}

		int count = 0;
		foreach ( var entry in container.Clothing )
		{
			if ( entry is not null )
			{
				count++;
			}
		}

		return count;
	}

	private int StripExcludedCategories( ClothingContainer container )
	{
		if ( container?.Clothing is null || ExcludedCategories is null || ExcludedCategories.Count == 0 )
		{
			return 0;
		}

		int stripped = 0;
		var seen = new List<string>();
		var entries = new List<ClothingContainer.ClothingEntry>( container.Clothing );
		foreach ( var entry in entries )
		{
			string category = entry?.Clothing?.Category.ToString();
			if ( string.IsNullOrEmpty( category ) )
			{
				continue;
			}

			if ( !seen.Contains( category ) )
			{
				seen.Add( category );
			}

			foreach ( string excluded in ExcludedCategories )
			{
				if ( string.Equals( category, excluded, StringComparison.OrdinalIgnoreCase ) )
				{
					container.Clothing.Remove( entry );
					stripped++;
					break;
				}
			}
		}

		if ( stripped > 0 )
		{
			Log.Info( $"FirstPersonCosmetics excluded {stripped} clothing entries." );
		}
		else if ( entries.Count > 0 )
		{
			Log.Info( $"FirstPersonCosmetics excluded nothing, categories present: {string.Join( ", ", seen )}" );
		}

		return stripped;
	}

	private async Task WaitForSettled( ClothingContainer container, int generation, CancellationToken token )
	{
		TimeSince elapsed = 0;
		while ( HasPendingEntries( container ) )
		{
			if ( elapsed > CosmeticSettleTimeout )
			{
				Log.Warning( $"FirstPersonCosmetics timed out after {CosmeticSettleTimeout:0}s waiting for downloads, applying what is loaded." );
				return;
			}

			await GameTask.DelaySeconds( 0.5f );
			if ( !IsCurrent( generation, token ) )
			{
				return;
			}
		}
	}

	private static bool HasPendingEntries( ClothingContainer container )
	{
		if ( container?.Clothing is null )
		{
			return false;
		}

		foreach ( var entry in container.Clothing )
		{
			if ( entry is not null && entry.Clothing is null && entry.ItemDefinitionId != 0 )
			{
				return true;
			}
		}

		return false;
	}
}
