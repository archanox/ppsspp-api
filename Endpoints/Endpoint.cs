namespace ppsspp_api.Endpoints;

public abstract class Endpoint
{
	protected readonly Ppsspp _ppsspp;

	private protected Endpoint(Ppsspp ppsspp)
	{
		_ppsspp = ppsspp;
	}
}