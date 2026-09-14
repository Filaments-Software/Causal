namespace Causal;

public sealed class ManualMovieCondition : Component, IMovieCondition
{
	[Property] public bool Failed { get; set; }

	public bool HasFailed()
	{
		return Failed;
	}
}
