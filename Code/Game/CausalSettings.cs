using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Causal;

public static class CausalSettings
{
	private const string SettingsFile = "causal-settings.json";

	[JsonPropertyName( "FirstPersonClothing" )]
	public static bool FirstPersonClothing { get; set; } = false;

	public static void Load()
	{
		try
		{
			var raw = FileSystem.Data.ReadJson<Dictionary<string, JsonElement>>( SettingsFile );
			if ( raw is null )
			{
				return;
			}

			if ( !raw.TryGetValue( "FirstPersonClothing", out var element ) )
			{
				return;
			}

			try
			{
				FirstPersonClothing = element.Deserialize<bool>();
			}
			catch ( Exception e )
			{
				Log.Warning( e, "CausalSettings failed to apply 'FirstPersonClothing'." );
			}
		}
		catch ( Exception ex )
		{
			Log.Warning( $"CausalSettings failed to load: {ex.Message}" );
		}
	}

	public static void Save()
	{
		try
		{
			var map = new Dictionary<string, object>
			{
				["FirstPersonClothing"] = FirstPersonClothing
			};

			FileSystem.Data.WriteJson( SettingsFile, map );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"CausalSettings failed to save: {ex.Message}" );
		}
	}

	public static void ApplyAll()
	{
	}
}
