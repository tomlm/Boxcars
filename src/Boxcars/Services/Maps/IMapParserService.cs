using Boxcars.Data.Maps;

namespace Boxcars.Services.Maps;

public interface IMapParserService
{
    Task<MapLoadResult> ParseAsync(string fileName, Stream contentStream, CancellationToken cancellationToken);
}
