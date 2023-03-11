namespace ppsspp_api.Endpoints;

public abstract class Endpoint
{
	protected readonly PPSSPP _ppsspp;

	internal Endpoint(PPSSPP ppsspp)
	{
		_ppsspp = ppsspp;
	}
}